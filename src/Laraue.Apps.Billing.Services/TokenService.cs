using Laraue.Apps.Billing.DataAccess;
using Laraue.Apps.Billing.DataAccess.Entities;
using Laraue.Apps.Billing.Services.Resources;
using Laraue.Core.Exceptions.Web;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Billing.Services;

// `Apps.Boards`
// --> "I want to reserve tokens for transaction"
// --> `Apps.Billing`
// --> "It's okay. Reservation is successful. Take the transaction Guid. Or error description."
// --> `Apps.Boards`
// --> "Okay. Money is reserved. I can start operation"
// --> | Execute operation |
// --> "We need to commit withdrawal. Actual spent: 720 of 2048 reserved"
// --> `Apps.Billing`
// --> "Commit the transaction: it changes status and updates balances in balances views"

public interface ITokenService
{
    /// <summary>
    /// Reserves up to <paramref name="inputTokensCount"/> + <paramref name="maxOutputTokensCount"/>
    /// tokens for <paramref name="paidEntityId"/>, drawing from its active subscription balance
    /// first and its unexpired purchased packs (soonest-expiring first) after that. Returns an
    /// error instead of throwing when the balance is insufficient - that's an expected outcome,
    /// not a client error.
    /// </summary>
    Task<ReservationResult> TryReserveTokensAsync(
        ServiceId serviceId,
        Guid paidEntityId,
        int inputTokensCount,
        int maxOutputTokensCount,
        CancellationToken cancellationToken);

    /// <summary>
    /// Confirms a <see cref="TokenSpentStatus.Started"/> reservation with the operation's actual
    /// output token count, refunding the unused portion of the reservation back to the balances
    /// it was drawn from.
    /// </summary>
    Task CommitTokensSpentAsync(
        Guid tokenTransactionId,
        int actualOutputTokensCount,
        CancellationToken cancellationToken);

    /// <summary>
    /// Cancels a <see cref="TokenSpentStatus.Started"/> reservation, fully refunding it back to
    /// the balances it was drawn from.
    /// </summary>
    Task CancelTokensReservationAsync(
        Guid tokenTransactionId,
        string error,
        CancellationToken cancellationToken);
}

public sealed record ReservationResult
{
    public Guid? TokenTransactionId { get; init; }
    public string? Error { get; init; }
}

public class TokenService(DatabaseContext context, ISubscriptionService subscriptionService) : ITokenService
{
    public async Task<ReservationResult> TryReserveTokensAsync(
        ServiceId serviceId,
        Guid paidEntityId,
        int inputTokensCount,
        int maxOutputTokensCount,
        CancellationToken cancellationToken)
    {
        var requested = (long)inputTokensCount + maxOutputTokensCount;
        var now = DateTime.UtcNow;

        await using var dbTransaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var subscriptionId = await subscriptionService
            .GetActiveSubscriptionIdAsync(serviceId, paidEntityId, cancellationToken);

        var subscriptionBalance = subscriptionId is null
            ? null
            : await context.BalanceSubscriptionTokens
                .SingleOrDefaultAsync(b => b.SubscriptionId == subscriptionId, cancellationToken);

        var subscriptionAvailable = subscriptionBalance is null
            ? 0L
            : subscriptionBalance.FreeTokensCount + subscriptionBalance.SubscriptionTokensCount;

        // Remaining balance per purchased pack isn't stored directly - it's derived as the
        // pack's original grant minus whatever's already been charged against it across the
        // ledger, ordered soonest-expiring first so those are drawn down before longer-lived ones.
        var purchasedPacks = await context.PurchasedTokenPacks
            .Where(p => p.PaidEntityId == paidEntityId && p.ExpiredAt > now)
            .OrderBy(p => p.ExpiredAt)
            .Select(p => new
            {
                p.Id,
                Granted = p.TokenPack!.TokensCount,
                Charged = context.TokenTransactionPurchasedTokenPacks
                    .Where(c => c.PurchasedTokenPackId == p.Id)
                    .Sum(c => (long?)c.ChargedAmount) ?? 0,
            })
            .ToListAsync(cancellationToken);

        var purchasedAvailable = purchasedPacks.Sum(p => p.Granted - p.Charged);

        if (subscriptionAvailable + purchasedAvailable < requested)
        {
            return new ReservationResult { Error = Errors.InsufficientTokenBalance };
        }

        var remaining = requested;
        TokenTransactionSubscriptionTokenPack? subscriptionSpend = null;

        if (subscriptionId is not null && subscriptionAvailable > 0)
        {
            var fromSubscription = Math.Min(remaining, subscriptionAvailable);
            var fromFree = Math.Min(fromSubscription, subscriptionBalance!.FreeTokensCount);
            var fromPaid = fromSubscription - fromFree;

            subscriptionBalance.FreeTokensCount -= fromFree;
            subscriptionBalance.SubscriptionTokensCount -= fromPaid;
            remaining -= fromSubscription;

            subscriptionSpend = new TokenTransactionSubscriptionTokenPack
            {
                Id = Guid.NewGuid(),
                SubscriptionId = subscriptionId.Value,
                ChargedFreeAmount = fromFree,
                ChargedAmount = fromPaid,
            };
            context.TokenTransactionSubscriptionTokenPacks.Add(subscriptionSpend);
        }

        var purchasedSpends = new List<TokenTransactionPurchasedTokenPack>();
        foreach (var pack in purchasedPacks)
        {
            if (remaining <= 0)
            {
                break;
            }

            var available = pack.Granted - pack.Charged;
            if (available <= 0)
            {
                continue;
            }

            var fromPack = Math.Min(remaining, available);
            remaining -= fromPack;

            purchasedSpends.Add(new TokenTransactionPurchasedTokenPack
            {
                PurchasedTokenPackId = pack.Id,
                ChargedAmount = fromPack,
                BalanceAfter = available - fromPack,
            });
        }

        if (purchasedSpends.Count > 0)
        {
            var purchasedBalance = await context.BalancePurchasedTokens
                .SingleAsync(b => b.PaidEntityId == paidEntityId, cancellationToken);
            purchasedBalance.Balance -= purchasedSpends.Sum(p => p.ChargedAmount);
            context.TokenTransactionPurchasedTokenPacks.AddRange(purchasedSpends);
        }

        var tokenTransaction = new TokenTransaction
        {
            Id = Guid.NewGuid(),
            PaidEntityId = paidEntityId,
            OwnerId = paidEntityId,
            Status = TokenSpentStatus.Started,
            Reason = TokenTransactionReason.Spend,
            CreatedAt = now,
            Delta = -requested,
            ReservedAmount = requested,
            InputTokensCount = inputTokensCount,
            SubscriptionTokensSpent = subscriptionSpend,
            PurchasedTokensSpent = purchasedSpends.Count > 0 ? purchasedSpends : null,
        };
        context.TokenTransactions.Add(tokenTransaction);

        await context.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);

        return new ReservationResult { TokenTransactionId = tokenTransaction.Id };
    }

    public async Task CommitTokensSpentAsync(
        Guid tokenTransactionId,
        int actualOutputTokensCount,
        CancellationToken cancellationToken)
    {
        await using var dbTransaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var tokenTransaction = await GetStartedTransactionOrThrowAsync(tokenTransactionId, cancellationToken);

        var actualSpent = (long)tokenTransaction.InputTokensCount + actualOutputTokensCount;
        var refund = Math.Max(0, tokenTransaction.ReservedAmount - actualSpent);

        if (refund > 0)
        {
            await RefundAsync(tokenTransaction, refund, cancellationToken);
        }

        tokenTransaction.Status = TokenSpentStatus.Confirmed;
        tokenTransaction.FinishedAt = DateTime.UtcNow;
        tokenTransaction.Delta = -actualSpent;

        await context.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);
    }

    public async Task CancelTokensReservationAsync(
        Guid tokenTransactionId,
        string error,
        CancellationToken cancellationToken)
    {
        await using var dbTransaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var tokenTransaction = await GetStartedTransactionOrThrowAsync(tokenTransactionId, cancellationToken);

        await RefundAsync(tokenTransaction, tokenTransaction.ReservedAmount, cancellationToken);

        tokenTransaction.Status = TokenSpentStatus.Canceled;
        tokenTransaction.FinishedAt = DateTime.UtcNow;
        tokenTransaction.Delta = 0;
        tokenTransaction.Error = error;

        await context.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Loads just the transaction row itself - not its spend children, which
    /// <see cref="RefundAsync"/> fetches separately only when it actually needs to touch them.
    /// </summary>
    private async Task<TokenTransaction> GetStartedTransactionOrThrowAsync(
        Guid tokenTransactionId,
        CancellationToken cancellationToken)
    {
        var tokenTransaction = await context.TokenTransactions
            .SingleOrDefaultAsync(t => t.Id == tokenTransactionId, cancellationToken);

        if (tokenTransaction is null)
        {
            throw new NotFoundException(string.Format(Errors.TokenTransactionNotFound, tokenTransactionId));
        }

        if (tokenTransaction.Status != TokenSpentStatus.Started)
        {
            throw new BadRequestException(
                nameof(tokenTransactionId),
                string.Format(Errors.TokenTransactionNotStarted, tokenTransactionId, tokenTransaction.Status));
        }

        return tokenTransaction;
    }

    /// <summary>
    /// Credits <paramref name="amount"/> back to the balances a reservation drew from, in the
    /// reverse of draw order (purchased packs - last drawn from first - before the subscription)
    /// so the same source that was charged is the one credited. Fetches only the specific spend
    /// rows this transaction owns, rather than the transaction's full graph.
    /// </summary>
    private async Task RefundAsync(TokenTransaction tokenTransaction, long amount, CancellationToken cancellationToken)
    {
        var remaining = amount;

        var purchasedSpends = await context.TokenTransactionPurchasedTokenPacks
            .Where(c => c.TokenTransactionId == tokenTransaction.Id)
            .OrderByDescending(c => c.PurchasedTokenPack!.ExpiredAt)
            .ToListAsync(cancellationToken);

        if (purchasedSpends.Count > 0)
        {
            BalancePurchasedToken? purchasedBalance = null;

            foreach (var spend in purchasedSpends)
            {
                if (remaining <= 0)
                {
                    break;
                }

                var refundFromPack = Math.Min(remaining, spend.ChargedAmount);
                spend.ChargedAmount -= refundFromPack;
                spend.BalanceAfter += refundFromPack;
                remaining -= refundFromPack;

                purchasedBalance ??= await context.BalancePurchasedTokens
                    .SingleAsync(b => b.PaidEntityId == tokenTransaction.PaidEntityId, cancellationToken);
                purchasedBalance.Balance += refundFromPack;
            }
        }

        if (remaining > 0 && tokenTransaction.SubscriptionTokensSpentId is { } subscriptionSpendId)
        {
            var subscriptionSpend = await context.TokenTransactionSubscriptionTokenPacks
                .SingleAsync(s => s.Id == subscriptionSpendId, cancellationToken);

            var subscriptionBalance = await context.BalanceSubscriptionTokens
                .SingleAsync(b => b.SubscriptionId == subscriptionSpend.SubscriptionId, cancellationToken);

            var refundToPaid = Math.Min(remaining, subscriptionSpend.ChargedAmount ?? 0);
            subscriptionSpend.ChargedAmount -= refundToPaid;
            subscriptionBalance.SubscriptionTokensCount += refundToPaid;
            remaining -= refundToPaid;

            var refundToFree = Math.Min(remaining, subscriptionSpend.ChargedFreeAmount ?? 0);
            subscriptionSpend.ChargedFreeAmount -= refundToFree;
            subscriptionBalance.FreeTokensCount += refundToFree;
        }
    }
}
