using Laraue.Apps.Billing.DataAccess;
using Laraue.Apps.Billing.DataAccess.Data;
using Laraue.Apps.Billing.DataAccess.Entities;
using Laraue.Apps.Billing.IntegrationTests.Infrastructure;
using Laraue.Apps.Billing.Services;
using Laraue.Apps.Billing.WebApiHost;
using Laraue.Core.Exceptions.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Billing.IntegrationTests;

[Collection("IntegrationTest")]
public class TokenServiceTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>, IAsyncLifetime
{
    private static readonly Guid PlusTariffId = LaraueBoardsTariffsData.PersonalTariffs[1].Tariff.Id;
    private static readonly Guid SmallPackId = TokenPacksData.Packs[0].Id;
    private static readonly Guid MediumPackId = TokenPacksData.Packs[1].Id;

    private IServiceScope _scope = null!;
    private DatabaseContext _context = null!;
    private ITokenService _tokenService = null!;

    public Task InitializeAsync()
    {
        _scope = host.Services.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        _context.CleanDatabase();
        _tokenService = new TokenService(_context, new SubscriptionService(_context));
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _scope.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task TryReserveTokensAsync_ShouldReserveFromSubscriptionFirst_WhenBothBalancesAvailable()
    {
        var paidEntityId = Guid.NewGuid();
        await SeedActiveSubscriptionAsync(paidEntityId, freeTokens: 1000, subscriptionTokens: 500);
        await SeedPurchasedPackAsync(paidEntityId, SmallPackId, DateTime.UtcNow.AddDays(30));
        await SetPurchasedBalanceAsync(paidEntityId, 100_000);

        var result = await _tokenService.TryReserveTokensAsync(
            ServiceId.LaraueBoards, paidEntityId, inputTokensCount: 100, maxOutputTokensCount: 200, CancellationToken.None);

        Assert.Null(result.Error);
        Assert.NotNull(result.TokenTransactionId);

        var subscriptionBalance = await _context.BalanceSubscriptionTokens.SingleAsync();
        Assert.Equal(700, subscriptionBalance.FreeTokensCount);
        Assert.Equal(500, subscriptionBalance.SubscriptionTokensCount);

        var purchasedBalance = await _context.BalancePurchasedTokens.SingleAsync();
        Assert.Equal(100_000, purchasedBalance.Balance);

        var tokenTransaction = await _context.TokenTransactions.SingleAsync(t => t.Id == result.TokenTransactionId);
        Assert.Equal(TokenSpentStatus.Started, tokenTransaction.Status);
        Assert.Equal(TokenTransactionReason.Spend, tokenTransaction.Reason);
        Assert.Equal(300, tokenTransaction.ReservedAmount);
        Assert.Equal(100, tokenTransaction.InputTokensCount);
    }

    [Fact]
    public async Task TryReserveTokensAsync_ShouldDrawPurchasedPacksBySoonestExpiry_WhenSubscriptionInsufficient()
    {
        var paidEntityId = Guid.NewGuid();
        await SeedActiveSubscriptionAsync(paidEntityId, freeTokens: 50, subscriptionTokens: 50);
        var soonPackId = await SeedPurchasedPackAsync(paidEntityId, MediumPackId, DateTime.UtcNow.AddDays(5));
        await SeedPurchasedPackAsync(paidEntityId, SmallPackId, DateTime.UtcNow.AddDays(10));
        await SetPurchasedBalanceAsync(paidEntityId, 700_000);

        var result = await _tokenService.TryReserveTokensAsync(
            ServiceId.LaraueBoards, paidEntityId, inputTokensCount: 100, maxOutputTokensCount: 1000, CancellationToken.None);

        Assert.Null(result.Error);

        var subscriptionBalance = await _context.BalanceSubscriptionTokens.SingleAsync();
        Assert.Equal(0, subscriptionBalance.FreeTokensCount);
        Assert.Equal(0, subscriptionBalance.SubscriptionTokensCount);

        var purchasedSpend = await _context.TokenTransactionPurchasedTokenPacks.SingleAsync();
        Assert.Equal(soonPackId, purchasedSpend.PurchasedTokenPackId);
        Assert.Equal(1000, purchasedSpend.ChargedAmount);
        Assert.Equal(599_000, purchasedSpend.BalanceAfter);

        var purchasedBalance = await _context.BalancePurchasedTokens.SingleAsync();
        Assert.Equal(699_000, purchasedBalance.Balance);
    }

    [Fact]
    public async Task TryReserveTokensAsync_ShouldReturnError_WhenBalanceInsufficient()
    {
        var paidEntityId = Guid.NewGuid();

        var result = await _tokenService.TryReserveTokensAsync(
            ServiceId.LaraueBoards, paidEntityId, inputTokensCount: 100, maxOutputTokensCount: 100, CancellationToken.None);

        Assert.Null(result.TokenTransactionId);
        Assert.NotNull(result.Error);
        Assert.False(await _context.TokenTransactions.AnyAsync(t => t.PaidEntityId == paidEntityId));
    }

    [Fact]
    public async Task CommitTokensSpentAsync_ShouldRefundUnusedTokens_WhenActualLessThanReserved()
    {
        var paidEntityId = Guid.NewGuid();
        await SeedActiveSubscriptionAsync(paidEntityId, freeTokens: 1000, subscriptionTokens: 0);

        var result = await _tokenService.TryReserveTokensAsync(
            ServiceId.LaraueBoards, paidEntityId, inputTokensCount: 100, maxOutputTokensCount: 900, CancellationToken.None);

        await _tokenService.CommitTokensSpentAsync(result.TokenTransactionId!.Value, actualOutputTokensCount: 100, CancellationToken.None);

        var subscriptionBalance = await _context.BalanceSubscriptionTokens.SingleAsync();
        Assert.Equal(800, subscriptionBalance.FreeTokensCount);

        var tokenTransaction = await _context.TokenTransactions.SingleAsync(t => t.Id == result.TokenTransactionId);
        Assert.Equal(TokenSpentStatus.Confirmed, tokenTransaction.Status);
        Assert.Equal(-200, tokenTransaction.Delta);
        Assert.NotNull(tokenTransaction.FinishedAt);
    }

    [Fact]
    public async Task CommitTokensSpentAsync_ShouldConfirmFullAmount_WhenActualEqualsReserved()
    {
        var paidEntityId = Guid.NewGuid();
        await SeedActiveSubscriptionAsync(paidEntityId, freeTokens: 500, subscriptionTokens: 0);

        var result = await _tokenService.TryReserveTokensAsync(
            ServiceId.LaraueBoards, paidEntityId, inputTokensCount: 100, maxOutputTokensCount: 400, CancellationToken.None);

        await _tokenService.CommitTokensSpentAsync(result.TokenTransactionId!.Value, actualOutputTokensCount: 400, CancellationToken.None);

        var subscriptionBalance = await _context.BalanceSubscriptionTokens.SingleAsync();
        Assert.Equal(0, subscriptionBalance.FreeTokensCount);

        var tokenTransaction = await _context.TokenTransactions.SingleAsync(t => t.Id == result.TokenTransactionId);
        Assert.Equal(TokenSpentStatus.Confirmed, tokenTransaction.Status);
        Assert.Equal(-500, tokenTransaction.Delta);
    }

    [Fact]
    public async Task CancelTokensReservationAsync_ShouldFullyRestoreBalances_WhenCalled()
    {
        var paidEntityId = Guid.NewGuid();
        await SeedActiveSubscriptionAsync(paidEntityId, freeTokens: 300, subscriptionTokens: 0);

        var result = await _tokenService.TryReserveTokensAsync(
            ServiceId.LaraueBoards, paidEntityId, inputTokensCount: 50, maxOutputTokensCount: 250, CancellationToken.None);

        await _tokenService.CancelTokensReservationAsync(result.TokenTransactionId!.Value, "timeout", CancellationToken.None);

        var subscriptionBalance = await _context.BalanceSubscriptionTokens.SingleAsync();
        Assert.Equal(300, subscriptionBalance.FreeTokensCount);

        var tokenTransaction = await _context.TokenTransactions.SingleAsync(t => t.Id == result.TokenTransactionId);
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

        var result = await _tokenService.TryReserveTokensAsync(
            ServiceId.LaraueBoards, paidEntityId, inputTokensCount: 10, maxOutputTokensCount: 10, CancellationToken.None);
        await _tokenService.CommitTokensSpentAsync(result.TokenTransactionId!.Value, actualOutputTokensCount: 10, CancellationToken.None);

        await Assert.ThrowsAsync<BadRequestException>(() => _tokenService.CancelTokensReservationAsync(
            result.TokenTransactionId!.Value, "too late", CancellationToken.None));
    }

    private async Task<Guid> SeedActiveSubscriptionAsync(Guid paidEntityId, long freeTokens, long subscriptionTokens)
    {
        var subscriptionId = Guid.NewGuid();

        _context.Subscriptions.Add(new Subscription
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

        _context.BalanceSubscriptionTokens.Add(new BalanceSubscriptionToken
        {
            SubscriptionId = subscriptionId,
            FreeTokensCount = freeTokens,
            SubscriptionTokensCount = subscriptionTokens,
        });

        await _context.SaveChangesAsync();

        return subscriptionId;
    }

    private async Task<Guid> SeedPurchasedPackAsync(Guid paidEntityId, Guid tokenPackId, DateTime expiredAt)
    {
        var packId = Guid.NewGuid();

        _context.PurchasedTokenPacks.Add(new PurchasedTokenPack
        {
            Id = packId,
            PaidEntityId = paidEntityId,
            TokenPackId = tokenPackId,
            PurchasedAt = DateTime.UtcNow,
            ExpiredAt = expiredAt,
        });

        await _context.SaveChangesAsync();

        return packId;
    }

    private async Task SetPurchasedBalanceAsync(Guid paidEntityId, long balance)
    {
        var existing = await _context.BalancePurchasedTokens.SingleOrDefaultAsync(b => b.PaidEntityId == paidEntityId);
        if (existing is null)
        {
            _context.BalancePurchasedTokens.Add(new BalancePurchasedToken { PaidEntityId = paidEntityId, Balance = balance });
        }
        else
        {
            existing.Balance = balance;
        }

        await _context.SaveChangesAsync();
    }
}
