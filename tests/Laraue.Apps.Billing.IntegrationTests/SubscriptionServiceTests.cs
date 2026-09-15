using Laraue.Apps.Billing.DataAccess;
using Laraue.Apps.Billing.DataAccess.Entities;
using Laraue.Apps.Billing.IntegrationTests.Infrastructure;
using Laraue.Apps.Billing.Services;
using Laraue.Apps.Billing.WebApiHost;
using Laraue.Core.DateTime.Services.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Billing.IntegrationTests;

public class SubscriptionServiceTests : BillingIntegrationTest
{
    private readonly WebApiTestHost _host;
    private readonly FakeDateTimeProvider _dateTimeProvider;
    private readonly ISubscriptionService _subscriptionService;

    public SubscriptionServiceTests(WebApiTestHost host) : base(host)
    {
        _host = host;
        _dateTimeProvider = new FakeDateTimeProvider(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        _subscriptionService = new SubscriptionService(Context, _dateTimeProvider);
    }

    [Fact]
    public async Task GetOrCreateActivePersonalSubscriptionIdAsync_ShouldAutoProvisionFreeSubscription_WhenNoneExists()
    {
        var userId = Guid.NewGuid();

        var subscriptionId = await _subscriptionService.GetOrCreateActivePersonalSubscriptionIdAsync(
            ServiceId.LaraueBoards, userId, CancellationToken.None);

        var subscription = await Context.Subscriptions.SingleAsync(s => s.Id == subscriptionId);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal(userId, subscription.PaidEntityId);

        var balance = await Context.BalanceSubscriptionTokens.SingleAsync(b => b.SubscriptionId == subscriptionId);
        Assert.Equal(2_500_000, balance.SubscriptionTokensCount);

        var grant = await Context.TokenTransactions.SingleAsync(t => t.PaidEntityId == userId);
        Assert.Equal(TokenTransactionReason.TariffGrant, grant.Reason);
        Assert.Equal(TokenSpentStatus.Confirmed, grant.Status);
        Assert.Equal(2_500_000, grant.Delta);
    }

    [Fact]
    public async Task GetOrCreateActivePersonalSubscriptionIdAsync_ShouldReturnSameSubscription_WhenCalledTwice()
    {
        var userId = Guid.NewGuid();

        var firstId = await _subscriptionService.GetOrCreateActivePersonalSubscriptionIdAsync(
            ServiceId.LaraueBoards, userId, CancellationToken.None);
        var secondId = await _subscriptionService.GetOrCreateActivePersonalSubscriptionIdAsync(
            ServiceId.LaraueBoards, userId, CancellationToken.None);

        Assert.Equal(firstId, secondId);
        Assert.Equal(1, await Context.Subscriptions.CountAsync(s => s.PaidEntityId == userId));
        Assert.Equal(1, await Context.TokenTransactions.CountAsync(
            t => t.PaidEntityId == userId && t.Reason == TokenTransactionReason.TariffGrant));
    }

    [Fact]
    public async Task GetOrCreateActiveOrganizationSubscriptionIdAsync_ShouldProvisionTeamFreeTariff_WhenNoneExists()
    {
        var organizationId = Guid.NewGuid();

        var subscriptionId = await _subscriptionService.GetOrCreateActiveOrganizationSubscriptionIdAsync(
            ServiceId.LaraueBoards, organizationId, CancellationToken.None);

        var subscription = await Context.Subscriptions.SingleAsync(s => s.Id == subscriptionId);
        var teamFreeTariffId = await Context.LaraueBoardsTeamTariffs
            .Where(t => t.Tariff!.IsFree)
            .Select(t => t.Id)
            .SingleAsync();
        Assert.Equal(teamFreeTariffId, subscription.TariffId);
    }

    [Fact]
    public async Task GetActiveMarkdownTranslatorPersonalSubscriptionAsync_ShouldResetDailyFreeTokens_WhenNewDayHasStarted()
    {
        var userId = Guid.NewGuid();

        var subscriptionId = await _subscriptionService.GetOrCreateActivePersonalSubscriptionIdAsync(
            ServiceId.MarkdownTranslator, userId, CancellationToken.None);

        var balanceAfterFirstDay = await Context.BalanceSubscriptionTokens.SingleAsync(b => b.SubscriptionId == subscriptionId);
        Assert.Equal(10_000, balanceAfterFirstDay.FreeTokensCount);
        Assert.Equal(new DateOnly(2026, 1, 1), balanceAfterFirstDay.LastDailyGrantAt);

        // Spend most of today's allowance down, then advance the fake clock a day and re-provision
        // (the same call the reserve/read paths make) - the daily allowance should reset, not add.
        balanceAfterFirstDay.FreeTokensCount = 100;
        await Context.SaveChangesAsync();
        _dateTimeProvider.UtcNow = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        await _subscriptionService.GetOrCreateActivePersonalSubscriptionIdAsync(
            ServiceId.MarkdownTranslator, userId, CancellationToken.None);

        var balanceAfterSecondDay = await Context.BalanceSubscriptionTokens.SingleAsync(b => b.SubscriptionId == subscriptionId);
        Assert.Equal(10_000, balanceAfterSecondDay.FreeTokensCount);
        Assert.Equal(new DateOnly(2026, 1, 2), balanceAfterSecondDay.LastDailyGrantAt);
    }

    [Fact]
    public async Task GetActiveMarkdownTranslatorPersonalSubscriptionAsync_ShouldNotRegrantDailyFreeTokens_WhenSameDay()
    {
        var userId = Guid.NewGuid();

        var subscriptionId = await _subscriptionService.GetOrCreateActivePersonalSubscriptionIdAsync(
            ServiceId.MarkdownTranslator, userId, CancellationToken.None);

        var balance = await Context.BalanceSubscriptionTokens.SingleAsync(b => b.SubscriptionId == subscriptionId);
        balance.FreeTokensCount = 100;
        await Context.SaveChangesAsync();

        await _subscriptionService.GetOrCreateActivePersonalSubscriptionIdAsync(
            ServiceId.MarkdownTranslator, userId, CancellationToken.None);

        var balanceAfter = await Context.BalanceSubscriptionTokens.SingleAsync(b => b.SubscriptionId == subscriptionId);
        Assert.Equal(100, balanceAfter.FreeTokensCount);
    }

    [Fact]
    public async Task GetOrCreateActivePersonalSubscriptionIdAsync_ShouldNotDoubleProvision_WhenTwoFirstCallsRunConcurrently()
    {
        var userId = Guid.NewGuid();

        using var otherScope = _host.Services.CreateScope();
        var otherContext = otherScope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var otherSubscriptionService = new SubscriptionService(
            otherContext, new FakeDateTimeProvider(_dateTimeProvider.UtcNow));

        // Both calls are the *first ever* lookup for this userId - without PgAdvisoryXactLock
        // guarding provisioning, both could see "no subscription yet" concurrently and each insert
        // their own Subscription + TariffGrant, double-granting the entity's balance.
        var subscriptionIds = await Task.WhenAll(
            _subscriptionService.GetOrCreateActivePersonalSubscriptionIdAsync(ServiceId.LaraueBoards, userId, CancellationToken.None),
            otherSubscriptionService.GetOrCreateActivePersonalSubscriptionIdAsync(ServiceId.LaraueBoards, userId, CancellationToken.None));

        Assert.Equal(subscriptionIds[0], subscriptionIds[1]);
        Assert.Equal(1, await Context.Subscriptions.CountAsync(s => s.PaidEntityId == userId));
        Assert.Equal(1, await Context.TokenTransactions.CountAsync(
            t => t.PaidEntityId == userId && t.Reason == TokenTransactionReason.TariffGrant));
    }

    private sealed class FakeDateTimeProvider(DateTime utcNow) : IDateTimeProvider
    {
        public DateTime UtcNow { get; set; } = utcNow;
        public DateTimeOffset UtcOffsetNow => UtcNow;
    }
}
