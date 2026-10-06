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
    public async Task GetOrCreateActivePersonalSubscriptionIdAsync_ShouldRequireATransactionStartedByTheCaller()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _subscriptionService.GetOrCreateActivePersonalSubscriptionIdAsync(
            ServiceId.LaraueBoards, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task GetOrCreateActivePersonalSubscriptionIdAsync_ShouldAutoProvisionFreeSubscription_WhenNoneExists()
    {
        var userId = Guid.NewGuid();

        var subscriptionId = await InTransaction(() => _subscriptionService.GetOrCreateActivePersonalSubscriptionIdAsync(
            ServiceId.LaraueBoards, userId, CancellationToken.None));

        var subscription = await Context.Subscriptions.SingleAsync(s => s.Id == subscriptionId);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal(userId, subscription.PaidEntityId);

        var balance = await Context.BalanceSubscriptionTokens.SingleAsync(b => b.SubscriptionId == subscriptionId);
        Assert.Equal(0, balance.SubscriptionTokensCount);
        Assert.Equal(25_000, balance.FreeTokensCount);
        Assert.Equal(new DateOnly(2026, 1, 1), balance.LastMonthlyGrantAt);

        var grant = await Context.TokenTransactions.SingleAsync(t => t.PaidEntityId == userId);
        Assert.Equal(TokenTransactionReason.TariffGrant, grant.Reason);
        Assert.Equal(TokenSpentStatus.Confirmed, grant.Status);
        Assert.Equal(25_000, grant.Delta);
    }

    [Fact]
    public async Task GetActivePersonalSubscriptionAsync_ShouldReturnTariffNameAndIncludedTokensCount_Always()
    {
        var userId = Guid.NewGuid();

        var subscription = await InTransaction(() => _subscriptionService.GetActivePersonalSubscriptionAsync(
            ServiceId.LaraueBoards, userId, CancellationToken.None));

        var personal = Assert.IsType<LaraueBoardsPersonalActiveSubscription>(subscription);
        Assert.Equal("Free", personal.Code);
        Assert.Equal(25_000, personal.IncludedTokensCount);
        Assert.Equal(500, personal.LimitIssuesPerMonth);
        Assert.Equal(1, personal.LimitFreeTeamOrganizationsCount);
    }

    [Fact]
    public async Task GetActiveOrganizationSubscriptionAsync_ShouldReturnTariffNameAndIncludedTokensCount_Always()
    {
        var organizationId = Guid.NewGuid();

        var subscription = await InTransaction(() => _subscriptionService.GetActiveOrganizationSubscriptionAsync(
            ServiceId.LaraueBoards, organizationId, CancellationToken.None));

        var team = Assert.IsType<LaraueBoardsTeamActiveSubscription>(subscription);
        Assert.Equal("Free", team.Code);
        Assert.Equal(25_000, team.IncludedTokensCount);
        Assert.Equal(500, team.LimitIssuesPerMonth);
    }

    [Fact]
    public async Task GetOrCreateActivePersonalSubscriptionIdAsync_ShouldReturnSameSubscription_WhenCalledTwice()
    {
        var userId = Guid.NewGuid();

        var firstId = await InTransaction(() => _subscriptionService.GetOrCreateActivePersonalSubscriptionIdAsync(
            ServiceId.LaraueBoards, userId, CancellationToken.None));
        var secondId = await InTransaction(() => _subscriptionService.GetOrCreateActivePersonalSubscriptionIdAsync(
            ServiceId.LaraueBoards, userId, CancellationToken.None));

        Assert.Equal(firstId, secondId);
        Assert.Equal(1, await Context.Subscriptions.CountAsync(s => s.PaidEntityId == userId));
        Assert.Equal(1, await Context.TokenTransactions.CountAsync(
            t => t.PaidEntityId == userId && t.Reason == TokenTransactionReason.TariffGrant));
    }

    [Fact]
    public async Task GetOrCreateActiveOrganizationSubscriptionIdAsync_ShouldProvisionTeamFreeTariff_WhenNoneExists()
    {
        var organizationId = Guid.NewGuid();

        var subscriptionId = await InTransaction(() => _subscriptionService.GetOrCreateActiveOrganizationSubscriptionIdAsync(
            ServiceId.LaraueBoards, organizationId, CancellationToken.None));

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

        var subscriptionId = await InTransaction(() => _subscriptionService.GetOrCreateActivePersonalSubscriptionIdAsync(
            ServiceId.MarkdownTranslator, userId, CancellationToken.None));

        var balanceAfterFirstDay = await Context.BalanceSubscriptionTokens.SingleAsync(b => b.SubscriptionId == subscriptionId);
        Assert.Equal(10_000, balanceAfterFirstDay.FreeTokensCount);
        Assert.Equal(new DateOnly(2026, 1, 1), balanceAfterFirstDay.LastDailyGrantAt);

        // Spend most of today's allowance down, then advance the fake clock a day and re-provision
        // (the same call the reserve/read paths make) - the daily allowance should reset, not add.
        balanceAfterFirstDay.FreeTokensCount = 100;
        await Context.SaveChangesAsync();
        _dateTimeProvider.UtcNow = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        await InTransaction(() => _subscriptionService.GetOrCreateActivePersonalSubscriptionIdAsync(
            ServiceId.MarkdownTranslator, userId, CancellationToken.None));

        var balanceAfterSecondDay = await Context.BalanceSubscriptionTokens.SingleAsync(b => b.SubscriptionId == subscriptionId);
        Assert.Equal(10_000, balanceAfterSecondDay.FreeTokensCount);
        Assert.Equal(new DateOnly(2026, 1, 2), balanceAfterSecondDay.LastDailyGrantAt);
    }

    [Fact]
    public async Task GetActiveMarkdownTranslatorPersonalSubscriptionAsync_ShouldNotRegrantDailyFreeTokens_WhenSameDay()
    {
        var userId = Guid.NewGuid();

        var subscriptionId = await InTransaction(() => _subscriptionService.GetOrCreateActivePersonalSubscriptionIdAsync(
            ServiceId.MarkdownTranslator, userId, CancellationToken.None));

        var balance = await Context.BalanceSubscriptionTokens.SingleAsync(b => b.SubscriptionId == subscriptionId);
        balance.FreeTokensCount = 100;
        await Context.SaveChangesAsync();

        await InTransaction(() => _subscriptionService.GetOrCreateActivePersonalSubscriptionIdAsync(
            ServiceId.MarkdownTranslator, userId, CancellationToken.None));

        var balanceAfter = await Context.BalanceSubscriptionTokens.SingleAsync(b => b.SubscriptionId == subscriptionId);
        Assert.Equal(100, balanceAfter.FreeTokensCount);
    }

    [Fact]
    public async Task GetOrCreateActivePersonalSubscriptionIdAsync_ShouldResetMonthlyFreeTokens_WhenNewMonthHasStarted()
    {
        var userId = Guid.NewGuid();

        var subscriptionId = await InTransaction(() => _subscriptionService.GetOrCreateActivePersonalSubscriptionIdAsync(
            ServiceId.LaraueBoards, userId, CancellationToken.None));

        var balance = await Context.BalanceSubscriptionTokens.SingleAsync(b => b.SubscriptionId == subscriptionId);
        balance.FreeTokensCount = 100;
        await Context.SaveChangesAsync();

        // Later in the same month: nothing is granted again.
        _dateTimeProvider.UtcNow = new DateTime(2026, 1, 31, 23, 0, 0, DateTimeKind.Utc);
        await InTransaction(() => _subscriptionService.GetOrCreateActivePersonalSubscriptionIdAsync(
            ServiceId.LaraueBoards, userId, CancellationToken.None));

        var balanceSameMonth = await Context.BalanceSubscriptionTokens.SingleAsync(b => b.SubscriptionId == subscriptionId);
        Assert.Equal(100, balanceSameMonth.FreeTokensCount);

        // First call of the next month: the allowance is reset to 10k (not added to the 100 left).
        _dateTimeProvider.UtcNow = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        await InTransaction(() => _subscriptionService.GetOrCreateActivePersonalSubscriptionIdAsync(
            ServiceId.LaraueBoards, userId, CancellationToken.None));

        var balanceNextMonth = await Context.BalanceSubscriptionTokens.SingleAsync(b => b.SubscriptionId == subscriptionId);
        Assert.Equal(25_000, balanceNextMonth.FreeTokensCount);
        Assert.Equal(0, balanceNextMonth.SubscriptionTokensCount);
        Assert.Equal(new DateOnly(2026, 2, 1), balanceNextMonth.LastMonthlyGrantAt);

        // The ledger carries the net change, so it still sums to the balance.
        var deltas = await Context.TokenTransactions
            .Where(t => t.PaidEntityId == userId && t.Reason == TokenTransactionReason.TariffGrant)
            .OrderBy(t => t.CreatedAt)
            .Select(t => t.Delta)
            .ToListAsync();
        Assert.Equal([25_000L, 24_900L], deltas);
    }

    [Fact]
    public async Task GetOrCreateActiveOrganizationSubscriptionIdAsync_ShouldGrantMonthlyFreeTokens_WhenNoneExists()
    {
        var organizationId = Guid.NewGuid();

        var subscriptionId = await InTransaction(() => _subscriptionService.GetOrCreateActiveOrganizationSubscriptionIdAsync(
            ServiceId.LaraueBoards, organizationId, CancellationToken.None));

        var balance = await Context.BalanceSubscriptionTokens.SingleAsync(b => b.SubscriptionId == subscriptionId);
        Assert.Equal(25_000, balance.FreeTokensCount);
        Assert.Equal(0, balance.SubscriptionTokensCount);
    }

    [Fact]
    public async Task GetOrCreateActivePersonalSubscriptionIdAsync_ShouldKeepExistingSubscriptionTokens_WhenMonthlyAllowanceIsFirstGranted()
    {
        var userId = Guid.NewGuid();

        var subscriptionId = await InTransaction(() => _subscriptionService.GetOrCreateActivePersonalSubscriptionIdAsync(
            ServiceId.LaraueBoards, userId, CancellationToken.None));

        // What a subscription provisioned before monthly allowances looks like: the old one-time
        // grant sits in the subscription balance and no monthly top-up has ever happened.
        var balance = await Context.BalanceSubscriptionTokens.SingleAsync(b => b.SubscriptionId == subscriptionId);
        balance.SubscriptionTokensCount = 2_500_000;
        balance.FreeTokensCount = 0;
        balance.LastMonthlyGrantAt = null;
        await Context.SaveChangesAsync();

        await InTransaction(() => _subscriptionService.GetOrCreateActivePersonalSubscriptionIdAsync(
            ServiceId.LaraueBoards, userId, CancellationToken.None));

        var balanceAfter = await Context.BalanceSubscriptionTokens.SingleAsync(b => b.SubscriptionId == subscriptionId);
        Assert.Equal(2_500_000, balanceAfter.SubscriptionTokensCount);
        Assert.Equal(25_000, balanceAfter.FreeTokensCount);
    }

    [Fact]
    public async Task GetOrCreateActivePersonalSubscriptionIdAsync_ShouldNotTouchDailyAllowance_ForMarkdownTranslatorInNewMonth()
    {
        var userId = Guid.NewGuid();

        var subscriptionId = await InTransaction(() => _subscriptionService.GetOrCreateActivePersonalSubscriptionIdAsync(
            ServiceId.MarkdownTranslator, userId, CancellationToken.None));

        _dateTimeProvider.UtcNow = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        await InTransaction(() => _subscriptionService.GetOrCreateActivePersonalSubscriptionIdAsync(
            ServiceId.MarkdownTranslator, userId, CancellationToken.None));

        // Markdown Translator's Free tariff has no monthly allowance: only the daily one applies.
        var balance = await Context.BalanceSubscriptionTokens.SingleAsync(b => b.SubscriptionId == subscriptionId);
        Assert.Equal(10_000, balance.FreeTokensCount);
        Assert.Equal(0, balance.SubscriptionTokensCount);
        Assert.Null(balance.LastMonthlyGrantAt);
        Assert.Equal(new DateOnly(2026, 2, 1), balance.LastDailyGrantAt);
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
            InTransaction(() => _subscriptionService.GetOrCreateActivePersonalSubscriptionIdAsync(ServiceId.LaraueBoards, userId, CancellationToken.None)),
            otherContext.Database.InTransactionAsync(() => otherSubscriptionService.GetOrCreateActivePersonalSubscriptionIdAsync(ServiceId.LaraueBoards, userId, CancellationToken.None)));

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
