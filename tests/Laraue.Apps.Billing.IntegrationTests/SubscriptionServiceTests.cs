using Laraue.Apps.Billing.DataAccess;
using Laraue.Apps.Billing.DataAccess.Entities;
using Laraue.Apps.Billing.IntegrationTests.Infrastructure;
using Laraue.Apps.Billing.Services;
using Laraue.Apps.Billing.WebApiHost;
using Laraue.Core.DateTime.Services.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Billing.IntegrationTests;

[Collection("IntegrationTest")]
public class SubscriptionServiceTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>, IAsyncLifetime
{
    private IServiceScope _scope = null!;
    private DatabaseContext _context = null!;
    private FakeDateTimeProvider _dateTimeProvider = null!;
    private ISubscriptionService _subscriptionService = null!;

    public Task InitializeAsync()
    {
        _scope = host.Services.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        _context.CleanDatabase();
        _dateTimeProvider = new FakeDateTimeProvider(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        _subscriptionService = new SubscriptionService(_context, _dateTimeProvider);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _scope.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task GetOrCreateActivePersonalSubscriptionIdAsync_ShouldAutoProvisionFreeSubscription_WhenNoneExists()
    {
        var userId = Guid.NewGuid();

        var subscriptionId = await _subscriptionService.GetOrCreateActivePersonalSubscriptionIdAsync(
            ServiceId.LaraueBoards, userId, CancellationToken.None);

        var subscription = await _context.Subscriptions.SingleAsync(s => s.Id == subscriptionId);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal(userId, subscription.PaidEntityId);

        var balance = await _context.BalanceSubscriptionTokens.SingleAsync(b => b.SubscriptionId == subscriptionId);
        Assert.Equal(2_500_000, balance.SubscriptionTokensCount);

        var grant = await _context.TokenTransactions.SingleAsync(t => t.PaidEntityId == userId);
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
        Assert.Equal(1, await _context.Subscriptions.CountAsync(s => s.PaidEntityId == userId));
        Assert.Equal(1, await _context.TokenTransactions.CountAsync(
            t => t.PaidEntityId == userId && t.Reason == TokenTransactionReason.TariffGrant));
    }

    [Fact]
    public async Task GetOrCreateActiveOrganizationSubscriptionIdAsync_ShouldProvisionTeamFreeTariff_WhenNoneExists()
    {
        var organizationId = Guid.NewGuid();

        var subscriptionId = await _subscriptionService.GetOrCreateActiveOrganizationSubscriptionIdAsync(
            ServiceId.LaraueBoards, organizationId, CancellationToken.None);

        var subscription = await _context.Subscriptions.SingleAsync(s => s.Id == subscriptionId);
        var teamFreeTariffId = await _context.LaraueBoardsTeamTariffs
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

        var balanceAfterFirstDay = await _context.BalanceSubscriptionTokens.SingleAsync(b => b.SubscriptionId == subscriptionId);
        Assert.Equal(10_000, balanceAfterFirstDay.FreeTokensCount);
        Assert.Equal(new DateOnly(2026, 1, 1), balanceAfterFirstDay.LastDailyGrantAt);

        // Spend most of today's allowance down, then advance the fake clock a day and re-provision
        // (the same call the reserve/read paths make) - the daily allowance should reset, not add.
        balanceAfterFirstDay.FreeTokensCount = 100;
        await _context.SaveChangesAsync();
        _dateTimeProvider.UtcNow = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        await _subscriptionService.GetOrCreateActivePersonalSubscriptionIdAsync(
            ServiceId.MarkdownTranslator, userId, CancellationToken.None);

        var balanceAfterSecondDay = await _context.BalanceSubscriptionTokens.SingleAsync(b => b.SubscriptionId == subscriptionId);
        Assert.Equal(10_000, balanceAfterSecondDay.FreeTokensCount);
        Assert.Equal(new DateOnly(2026, 1, 2), balanceAfterSecondDay.LastDailyGrantAt);
    }

    [Fact]
    public async Task GetActiveMarkdownTranslatorPersonalSubscriptionAsync_ShouldNotRegrantDailyFreeTokens_WhenSameDay()
    {
        var userId = Guid.NewGuid();

        var subscriptionId = await _subscriptionService.GetOrCreateActivePersonalSubscriptionIdAsync(
            ServiceId.MarkdownTranslator, userId, CancellationToken.None);

        var balance = await _context.BalanceSubscriptionTokens.SingleAsync(b => b.SubscriptionId == subscriptionId);
        balance.FreeTokensCount = 100;
        await _context.SaveChangesAsync();

        await _subscriptionService.GetOrCreateActivePersonalSubscriptionIdAsync(
            ServiceId.MarkdownTranslator, userId, CancellationToken.None);

        var balanceAfter = await _context.BalanceSubscriptionTokens.SingleAsync(b => b.SubscriptionId == subscriptionId);
        Assert.Equal(100, balanceAfter.FreeTokensCount);
    }

    [Fact]
    public async Task GetOrCreateActivePersonalSubscriptionIdAsync_ShouldNotDoubleProvision_WhenTwoFirstCallsRunConcurrently()
    {
        var userId = Guid.NewGuid();

        using var otherScope = host.Services.CreateScope();
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
        Assert.Equal(1, await _context.Subscriptions.CountAsync(s => s.PaidEntityId == userId));
        Assert.Equal(1, await _context.TokenTransactions.CountAsync(
            t => t.PaidEntityId == userId && t.Reason == TokenTransactionReason.TariffGrant));
    }

    private sealed class FakeDateTimeProvider(DateTime utcNow) : IDateTimeProvider
    {
        public DateTime UtcNow { get; set; } = utcNow;
        public DateTimeOffset UtcOffsetNow => UtcNow;
    }
}
