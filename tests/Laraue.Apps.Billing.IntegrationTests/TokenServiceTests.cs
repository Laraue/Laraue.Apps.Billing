using Laraue.Apps.Billing.DataAccess;
using Laraue.Apps.Billing.DataAccess.Data;
using Laraue.Apps.Billing.DataAccess.Entities;
using Laraue.Apps.Billing.IntegrationTests.Infrastructure;
using Laraue.Apps.Billing.Services;
using Laraue.Apps.Billing.WebApiHost;
using Laraue.Core.DateTime.Services.Impl;
using Laraue.Core.Exceptions.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Billing.IntegrationTests;

public class TokenServiceTests : BillingIntegrationTest
{
    private static readonly Guid PlusTariffId = LaraueBoardsTariffsData.PersonalTariffs[1].Tariff.Id;
    private static readonly Guid SmallPackId = TokenPacksData.Packs[0].Id;
    private static readonly Guid MediumPackId = TokenPacksData.Packs[1].Id;

    private readonly WebApiTestHost _host;
    private readonly ITokenService _tokenService;

    public TokenServiceTests(WebApiTestHost host) : base(host)
    {
        _host = host;
        var dateTimeProvider = new DateTimeProvider();
        _tokenService = new TokenService(Context, new SubscriptionService(Context, dateTimeProvider), dateTimeProvider);
    }

    [Fact]
    public async Task TryReservePersonalTokensAsync_ShouldReserveFromSubscriptionFirst_WhenBothBalancesAvailable()
    {
        var paidEntityId = Guid.NewGuid();
        await SeedActiveSubscriptionAsync(paidEntityId, freeTokens: 1000, subscriptionTokens: 500);
        await SeedPurchasedPackAsync(paidEntityId, SmallPackId, DateTime.UtcNow.AddDays(30));
        await SetPurchasedBalanceAsync(paidEntityId, 100_000);

        var result = await _tokenService.TryReservePersonalTokensAsync(
            ServiceId.LaraueBoards, paidEntityId, inputTokensCount: 100, maxOutputTokensCount: 200, CancellationToken.None);

        Assert.Null(result.Error);
        Assert.NotNull(result.TokenTransactionId);

        var subscriptionBalance = await Context.BalanceSubscriptionTokens.SingleAsync();
        Assert.Equal(700, subscriptionBalance.FreeTokensCount);
        Assert.Equal(500, subscriptionBalance.SubscriptionTokensCount);

        var purchasedBalance = await Context.BalancePurchasedTokens.SingleAsync();
        Assert.Equal(100_000, purchasedBalance.Balance);

        var tokenTransaction = await Context.TokenTransactions.SingleAsync(t => t.Id == result.TokenTransactionId);
        Assert.Equal(TokenSpentStatus.Started, tokenTransaction.Status);
        Assert.Equal(TokenTransactionReason.Spend, tokenTransaction.Reason);
        Assert.Equal(300, tokenTransaction.ReservedAmount);
        Assert.Equal(100, tokenTransaction.InputTokensCount);
    }

    [Fact]
    public async Task TryReservePersonalTokensAsync_ShouldDrawPurchasedPacksBySoonestExpiry_WhenSubscriptionInsufficient()
    {
        var paidEntityId = Guid.NewGuid();
        await SeedActiveSubscriptionAsync(paidEntityId, freeTokens: 50, subscriptionTokens: 50);
        var soonPackId = await SeedPurchasedPackAsync(paidEntityId, MediumPackId, DateTime.UtcNow.AddDays(5));
        await SeedPurchasedPackAsync(paidEntityId, SmallPackId, DateTime.UtcNow.AddDays(10));
        await SetPurchasedBalanceAsync(paidEntityId, 700_000);

        var result = await _tokenService.TryReservePersonalTokensAsync(
            ServiceId.LaraueBoards, paidEntityId, inputTokensCount: 100, maxOutputTokensCount: 1000, CancellationToken.None);

        Assert.Null(result.Error);

        var subscriptionBalance = await Context.BalanceSubscriptionTokens.SingleAsync();
        Assert.Equal(0, subscriptionBalance.FreeTokensCount);
        Assert.Equal(0, subscriptionBalance.SubscriptionTokensCount);

        var purchasedSpend = await Context.TokenTransactionPurchasedTokenPacks.SingleAsync();
        Assert.Equal(soonPackId, purchasedSpend.PurchasedTokenPackId);
        Assert.Equal(1000, purchasedSpend.ChargedAmount);
        Assert.Equal(599_000, purchasedSpend.BalanceAfter);

        var purchasedBalance = await Context.BalancePurchasedTokens.SingleAsync();
        Assert.Equal(699_000, purchasedBalance.Balance);
    }

    [Fact]
    public async Task TryReservePersonalTokensAsync_ShouldReturnError_WhenBalanceInsufficient()
    {
        var paidEntityId = Guid.NewGuid();

        // A brand-new paidEntityId now auto-provisions onto Free (2,500,000 tokens granted) on
        // first touch, so a request has to exceed even that grant to legitimately be insufficient.
        var result = await _tokenService.TryReservePersonalTokensAsync(
            ServiceId.LaraueBoards, paidEntityId, inputTokensCount: 2_000_000, maxOutputTokensCount: 2_000_000, CancellationToken.None);

        Assert.Null(result.TokenTransactionId);
        Assert.NotNull(result.Error);
        Assert.False(await Context.TokenTransactions.AnyAsync(
            t => t.PaidEntityId == paidEntityId && t.Reason == TokenTransactionReason.Spend));
    }

    [Fact]
    public async Task TryReservePersonalTokensAsync_ShouldNotDoubleSpend_WhenTwoReservationsRunConcurrently()
    {
        var paidEntityId = Guid.NewGuid();
        await SeedActiveSubscriptionAsync(paidEntityId, freeTokens: 1000, subscriptionTokens: 0);

        // Each request alone fits (650 <= 1000), but both together (1300) don't - without the
        // pg_advisory_xact_lock in TokenService, both could read the same pre-decrement balance,
        // both pass the sufficiency check, and the second's UPDATE would silently clobber the
        // first's, leaving the balance at 1000 - 650 = 350 instead of correctly rejecting one.
        using var otherScope = _host.Services.CreateScope();
        var otherContext = otherScope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var otherDateTimeProvider = new DateTimeProvider();
        var otherTokenService = new TokenService(
            otherContext, new SubscriptionService(otherContext, otherDateTimeProvider), otherDateTimeProvider);

        var results = await Task.WhenAll(
            _tokenService.TryReservePersonalTokensAsync(
                ServiceId.LaraueBoards, paidEntityId, inputTokensCount: 100, maxOutputTokensCount: 550, CancellationToken.None),
            otherTokenService.TryReservePersonalTokensAsync(
                ServiceId.LaraueBoards, paidEntityId, inputTokensCount: 100, maxOutputTokensCount: 550, CancellationToken.None));

        Assert.Single(results, r => r.Error is null);
        Assert.Single(results, r => r.Error is not null);

        var subscriptionBalance = await Context.BalanceSubscriptionTokens.SingleAsync();
        Assert.Equal(350, subscriptionBalance.FreeTokensCount);
    }

    [Fact]
    public async Task TryReservePersonalTokensAsync_ShouldAutoProvisionFreeSubscription_WhenNoneExistsYet()
    {
        var userId = Guid.NewGuid();

        var result = await _tokenService.TryReservePersonalTokensAsync(
            ServiceId.LaraueBoards, userId, inputTokensCount: 100, maxOutputTokensCount: 200, CancellationToken.None);

        Assert.Null(result.Error);
        Assert.NotNull(result.TokenTransactionId);

        var subscription = await Context.Subscriptions.SingleAsync(s => s.PaidEntityId == userId);
        var personalFreeTariffId = await Context.LaraueBoardsPersonalTariffs
            .Where(t => t.Tariff!.IsFree)
            .Select(t => t.Id)
            .SingleAsync();
        Assert.Equal(personalFreeTariffId, subscription.TariffId);

        Assert.True(await Context.TokenTransactions.AnyAsync(
            t => t.PaidEntityId == userId && t.Reason == TokenTransactionReason.TariffGrant));
    }

    [Fact]
    public async Task TryReserveOrganizationTokensAsync_ShouldProvisionTeamFreeTariff_WhenNoneExistsYet()
    {
        var organizationId = Guid.NewGuid();

        var result = await _tokenService.TryReserveOrganizationTokensAsync(
            ServiceId.LaraueBoards, organizationId, inputTokensCount: 100, maxOutputTokensCount: 200, CancellationToken.None);

        Assert.Null(result.Error);

        var subscription = await Context.Subscriptions.SingleAsync(s => s.PaidEntityId == organizationId);
        var teamFreeTariffId = await Context.LaraueBoardsTeamTariffs
            .Where(t => t.Tariff!.IsFree)
            .Select(t => t.Id)
            .SingleAsync();
        Assert.Equal(teamFreeTariffId, subscription.TariffId);
    }

    [Fact]
    public async Task CommitTokensSpentAsync_ShouldRefundUnusedTokens_WhenActualLessThanReserved()
    {
        var paidEntityId = Guid.NewGuid();
        await SeedActiveSubscriptionAsync(paidEntityId, freeTokens: 1000, subscriptionTokens: 0);

        var result = await _tokenService.TryReservePersonalTokensAsync(
            ServiceId.LaraueBoards, paidEntityId, inputTokensCount: 100, maxOutputTokensCount: 900, CancellationToken.None);

        await _tokenService.CommitTokensSpentAsync(result.TokenTransactionId!.Value, actualOutputTokensCount: 100, CancellationToken.None);

        var subscriptionBalance = await Context.BalanceSubscriptionTokens.SingleAsync();
        Assert.Equal(800, subscriptionBalance.FreeTokensCount);

        var tokenTransaction = await Context.TokenTransactions.SingleAsync(t => t.Id == result.TokenTransactionId);
        Assert.Equal(TokenSpentStatus.Confirmed, tokenTransaction.Status);
        Assert.Equal(-200, tokenTransaction.Delta);
        Assert.NotNull(tokenTransaction.FinishedAt);
    }

    [Fact]
    public async Task CommitTokensSpentAsync_ShouldConfirmFullAmount_WhenActualEqualsReserved()
    {
        var paidEntityId = Guid.NewGuid();
        await SeedActiveSubscriptionAsync(paidEntityId, freeTokens: 500, subscriptionTokens: 0);

        var result = await _tokenService.TryReservePersonalTokensAsync(
            ServiceId.LaraueBoards, paidEntityId, inputTokensCount: 100, maxOutputTokensCount: 400, CancellationToken.None);

        await _tokenService.CommitTokensSpentAsync(result.TokenTransactionId!.Value, actualOutputTokensCount: 400, CancellationToken.None);

        var subscriptionBalance = await Context.BalanceSubscriptionTokens.SingleAsync();
        Assert.Equal(0, subscriptionBalance.FreeTokensCount);

        var tokenTransaction = await Context.TokenTransactions.SingleAsync(t => t.Id == result.TokenTransactionId);
        Assert.Equal(TokenSpentStatus.Confirmed, tokenTransaction.Status);
        Assert.Equal(-500, tokenTransaction.Delta);
    }

    [Fact]
    public async Task CancelTokensReservationAsync_ShouldFullyRestoreBalances_WhenCalled()
    {
        var paidEntityId = Guid.NewGuid();
        await SeedActiveSubscriptionAsync(paidEntityId, freeTokens: 300, subscriptionTokens: 0);

        var result = await _tokenService.TryReservePersonalTokensAsync(
            ServiceId.LaraueBoards, paidEntityId, inputTokensCount: 50, maxOutputTokensCount: 250, CancellationToken.None);

        await _tokenService.CancelTokensReservationAsync(result.TokenTransactionId!.Value, "timeout", CancellationToken.None);

        var subscriptionBalance = await Context.BalanceSubscriptionTokens.SingleAsync();
        Assert.Equal(300, subscriptionBalance.FreeTokensCount);

        var tokenTransaction = await Context.TokenTransactions.SingleAsync(t => t.Id == result.TokenTransactionId);
        Assert.Equal(TokenSpentStatus.Canceled, tokenTransaction.Status);
        Assert.Equal(0, tokenTransaction.Delta);
        Assert.Equal("timeout", tokenTransaction.Error);
        Assert.NotNull(tokenTransaction.FinishedAt);
    }

    [Fact]
    public async Task CommitTokensSpentAsync_ShouldThrowNotFoundException_WhenTransactionDoesNotExist()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _tokenService.CommitTokensSpentAsync(
            Guid.NewGuid(), actualOutputTokensCount: 1, CancellationToken.None));
    }

    [Fact]
    public async Task CancelTokensReservationAsync_ShouldThrowBadRequestException_WhenTransactionAlreadyConfirmed()
    {
        var paidEntityId = Guid.NewGuid();
        await SeedActiveSubscriptionAsync(paidEntityId, freeTokens: 100, subscriptionTokens: 0);

        var result = await _tokenService.TryReservePersonalTokensAsync(
            ServiceId.LaraueBoards, paidEntityId, inputTokensCount: 10, maxOutputTokensCount: 10, CancellationToken.None);
        await _tokenService.CommitTokensSpentAsync(result.TokenTransactionId!.Value, actualOutputTokensCount: 10, CancellationToken.None);

        await Assert.ThrowsAsync<BadRequestException>(() => _tokenService.CancelTokensReservationAsync(
            result.TokenTransactionId!.Value, "too late", CancellationToken.None));
    }

    private async Task<Guid> SeedActiveSubscriptionAsync(Guid paidEntityId, long freeTokens, long subscriptionTokens)
    {
        var subscriptionId = Guid.NewGuid();

        Context.Subscriptions.Add(new Subscription
        {
            Id = subscriptionId,
            ServiceId = ServiceId.LaraueBoards,
            TariffId = PlusTariffId,
            OwnerId = paidEntityId,
            PaidEntityId = paidEntityId,
            Status = SubscriptionStatus.Active,
            CurrentPeriodStartedAt = DateTime.UtcNow.AddDays(-1),
            CurrentPeriodFinishesAt = DateTime.UtcNow.AddDays(29),
        });

        Context.BalanceSubscriptionTokens.Add(new BalanceSubscriptionToken
        {
            SubscriptionId = subscriptionId,
            FreeTokensCount = freeTokens,
            SubscriptionTokensCount = subscriptionTokens,
        });

        await Context.SaveChangesAsync();

        return subscriptionId;
    }

    private async Task<Guid> SeedPurchasedPackAsync(Guid paidEntityId, Guid tokenPackId, DateTime expiredAt)
    {
        var packId = Guid.NewGuid();

        Context.PurchasedTokenPacks.Add(new PurchasedTokenPack
        {
            Id = packId,
            PaidEntityId = paidEntityId,
            TokenPackId = tokenPackId,
            PurchasedAt = DateTime.UtcNow,
            ExpiredAt = expiredAt,
        });

        await Context.SaveChangesAsync();

        return packId;
    }

    private async Task SetPurchasedBalanceAsync(Guid paidEntityId, long balance)
    {
        var existing = await Context.BalancePurchasedTokens.SingleOrDefaultAsync(b => b.PaidEntityId == paidEntityId);
        if (existing is null)
        {
            Context.BalancePurchasedTokens.Add(new BalancePurchasedToken { PaidEntityId = paidEntityId, Balance = balance });
        }
        else
        {
            existing.Balance = balance;
        }

        await Context.SaveChangesAsync();
    }
}
