using Laraue.Apps.Billing.DataAccess;
using Laraue.Apps.Billing.DataAccess.Entities;
using Laraue.Apps.Billing.Services.Resources;
using Laraue.Core.DataAccess.Contracts;
using Laraue.Core.DataAccess.EFCore.Extensions;
using Laraue.Core.DateTime.Services.Abstractions;
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
    /// tokens for <paramref name="userId"/>'s personal balance on <paramref name="serviceId"/>,
    /// drawing from its active subscription balance first and its unexpired purchased packs
    /// (soonest-expiring first) after that. Returns an error instead of throwing when the balance
    /// is insufficient - that's an expected outcome, not a client error.
    /// </summary>
    Task<ReservationResult> TryReservePersonalTokensAsync(
        ServiceId serviceId,
        Guid userId,
        int inputTokensCount,
        int maxOutputTokensCount,
        CancellationToken cancellationToken);

    /// <summary>
    /// Same as <see cref="TryReservePersonalTokensAsync"/>, but against
    /// <paramref name="organizationId"/>'s team balance instead of a user's personal one - kept
    /// separate (rather than a personal/organization flag) since this service has no
    /// User/Organization tables and can't otherwise tell which kind of Free tariff to
    /// auto-provision for an entity with no subscription yet. <paramref name="userId"/> is the
    /// team member who actually triggered the spend - recorded as the transaction's
    /// <see cref="DataAccess.Entities.TokenTransaction.OwnerId"/>, distinct from
    /// <paramref name="organizationId"/> (the entity actually billed), so an admin can later see
    /// who on the team spent what.
    /// </summary>
    Task<ReservationResult> TryReserveOrganizationTokensAsync(
        ServiceId serviceId,
        Guid organizationId,
        Guid userId,
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

    /// <summary>
    /// Returns <paramref name="userId"/>'s current remaining balance on <paramref name="serviceId"/>
    /// - auto-provisioning the Free subscription first if none exists yet, same as
    /// <see cref="TryReservePersonalTokensAsync"/> - there's no "no balance at all" case to report.
    /// </summary>
    Task<TokenBalance> GetPersonalTokenBalanceAsync(
        ServiceId serviceId,
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Same as <see cref="GetPersonalTokenBalanceAsync"/>, but for <paramref name="organizationId"/>'s
    /// team balance instead of a user's personal one.
    /// </summary>
    Task<TokenBalance> GetOrganizationTokenBalanceAsync(
        ServiceId serviceId,
        Guid organizationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns <paramref name="paidEntityId"/>'s token transaction ledger, newest first - every
    /// balance-changing event (grants, spends, refunds via cancel/commit) regardless of which
    /// service caused it, since the ledger itself has no per-service scoping (see
    /// <see cref="DataAccess.Entities.TokenTransaction"/> - only <c>PaidEntityId</c> identifies
    /// whose balance changed). <paramref name="ownerId"/> optionally narrows this to transactions
    /// triggered by one specific team member (see <see cref="TryReserveOrganizationTokensAsync"/>) -
    /// meaningless (every row's owner already equals <paramref name="paidEntityId"/>) for a
    /// personal ledger, but useful for an organization's.
    /// </summary>
    Task<ShortPaginatedResult<TokenTransactionItem>> GetTokenTransactionsAsync(
        Guid paidEntityId,
        Guid? ownerId,
        PaginationData pagination,
        CancellationToken cancellationToken);
}

public sealed record ReservationResult
{
    public Guid? TokenTransactionId { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Current remaining balance, broken down by source - a caller reserving tokens draws from
/// <see cref="FreeTokensCount"/> and <see cref="SubscriptionTokensCount"/> first, then
/// <see cref="PurchasedTokensCount"/> (see <see cref="TokenService.TryReserveTokensCoreAsync"/>).
/// </summary>
public sealed record TokenBalance
{
    public required long FreeTokensCount { get; init; }
    public required long SubscriptionTokensCount { get; init; }
    public required long PurchasedTokensCount { get; init; }
}

/// <summary>
/// One row of a paid entity's token ledger - <see cref="Delta"/> is the balance change (negative
/// for a spend, positive for a grant/refund); <see cref="ReservedAmount"/>/<see cref="Error"/> only
/// apply to <see cref="TokenTransactionReason.Spend"/> rows and are null/zero otherwise.
/// </summary>
public sealed record TokenTransactionItem
{
    public required Guid Id { get; init; }
    public required Guid OwnerId { get; init; }
    public required TokenSpentStatus Status { get; init; }
    public required TokenTransactionReason Reason { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? FinishedAt { get; init; }
    public required long Delta { get; init; }
    public string? Error { get; init; }
}

public class TokenService(
    DatabaseContext context,
    ISubscriptionService subscriptionService,
    IDateTimeProvider dateTimeProvider) : ITokenService
{
    public Task<ReservationResult> TryReservePersonalTokensAsync(
        ServiceId serviceId,
        Guid userId,
        int inputTokensCount,
        int maxOutputTokensCount,
        CancellationToken cancellationToken)
        => TryReserveTokensCoreAsync(
            userId,
            userId,
            ct => subscriptionService.GetOrCreateActivePersonalSubscriptionIdAsync(serviceId, userId, ct),
            inputTokensCount,
            maxOutputTokensCount,
            cancellationToken);

    public Task<ReservationResult> TryReserveOrganizationTokensAsync(
        ServiceId serviceId,
        Guid organizationId,
        Guid userId,
        int inputTokensCount,
        int maxOutputTokensCount,
        CancellationToken cancellationToken)
        => TryReserveTokensCoreAsync(
            organizationId,
            userId,
            ct => subscriptionService.GetOrCreateActiveOrganizationSubscriptionIdAsync(serviceId, organizationId, ct),
            inputTokensCount,
            maxOutputTokensCount,
            cancellationToken);

    /// <summary>
    /// Shared by <see cref="TryReservePersonalTokensAsync"/>/<see cref="TryReserveOrganizationTokensAsync"/>
    /// - only how <paramref name="resolveSubscriptionIdAsync"/> looks up (and auto-provisions) the
    /// active subscription differs between the two; everything else about spending tokens is
    /// identical regardless of personal vs. organization. <paramref name="resolveSubscriptionIdAsync"/>
    /// always provisions a Free subscription if none exists yet, so unlike before Phase 2 there's
    /// no "no subscription at all" case left to handle here - only "not enough tokens on it".
    /// <paramref name="ownerId"/> is who actually triggered the spend (always equal to
    /// <paramref name="paidEntityId"/> for a personal reservation, but the acting team member for
    /// an organization one) - recorded on the transaction separately from who's billed.
    /// </summary>
    private async Task<ReservationResult> TryReserveTokensCoreAsync(
        Guid paidEntityId,
        Guid ownerId,
        Func<CancellationToken, Task<Guid>> resolveSubscriptionIdAsync,
        int inputTokensCount,
        int maxOutputTokensCount,
        CancellationToken cancellationToken)
    {
        var requested = (long)inputTokensCount + maxOutputTokensCount;
        var now = dateTimeProvider.UtcNow;

        await using var dbTransaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // Serializes every reserve/commit/cancel for this paidEntityId - without it, two
        // concurrent reservations read the same pre-decrement balance, both pass the sufficiency
        // check, and the second's UPDATE silently clobbers the first's (no optimistic-concurrency
        // token on Balance*Token), leaving the materialized balance higher than it should be.
        await context.Database.PgAdvisoryXactLock(paidEntityId.ToString(), cancellationToken);

        var subscriptionId = await resolveSubscriptionIdAsync(cancellationToken);

        var subscriptionBalance = await context.BalanceSubscriptionTokens
            .SingleAsync(b => b.SubscriptionId == subscriptionId, cancellationToken);

        var subscriptionAvailable = subscriptionBalance.FreeTokensCount + subscriptionBalance.SubscriptionTokensCount;

        var purchasedPacks = await GetUnexpiredPurchasedTokenPacksAsync(paidEntityId, now, cancellationToken);
        var purchasedAvailable = purchasedPacks.Sum(p => p.Available);

        if (subscriptionAvailable + purchasedAvailable < requested)
        {
            return new ReservationResult { Error = Errors.InsufficientTokenBalance };
        }

        var remaining = requested;
        TokenTransactionSubscriptionTokenPack? subscriptionSpend = null;

        if (subscriptionAvailable > 0)
        {
            var fromSubscription = Math.Min(remaining, subscriptionAvailable);
            var fromFree = Math.Min(fromSubscription, subscriptionBalance.FreeTokensCount);
            var fromPaid = fromSubscription - fromFree;

            subscriptionBalance.FreeTokensCount -= fromFree;
            subscriptionBalance.SubscriptionTokensCount -= fromPaid;
            remaining -= fromSubscription;

            subscriptionSpend = new TokenTransactionSubscriptionTokenPack
            {
                Id = Guid.NewGuid(),
                SubscriptionId = subscriptionId,
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

            if (pack.Available <= 0)
            {
                continue;
            }

            var fromPack = Math.Min(remaining, pack.Available);
            remaining -= fromPack;

            purchasedSpends.Add(new TokenTransactionPurchasedTokenPack
            {
                PurchasedTokenPackId = pack.Id,
                ChargedAmount = fromPack,
                BalanceAfter = pack.Available - fromPack,
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
            OwnerId = ownerId,
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

    public Task<TokenBalance> GetPersonalTokenBalanceAsync(
        ServiceId serviceId,
        Guid userId,
        CancellationToken cancellationToken)
        => GetTokenBalanceCoreAsync(
            userId,
            ct => subscriptionService.GetOrCreateActivePersonalSubscriptionIdAsync(serviceId, userId, ct),
            cancellationToken);

    public Task<TokenBalance> GetOrganizationTokenBalanceAsync(
        ServiceId serviceId,
        Guid organizationId,
        CancellationToken cancellationToken)
        => GetTokenBalanceCoreAsync(
            organizationId,
            ct => subscriptionService.GetOrCreateActiveOrganizationSubscriptionIdAsync(serviceId, organizationId, ct),
            cancellationToken);

    /// <summary>
    /// Shared by <see cref="GetPersonalTokenBalanceAsync"/>/<see cref="GetOrganizationTokenBalanceAsync"/>
    /// - a read-only counterpart to <see cref="TryReserveTokensCoreAsync"/>'s balance lookup, minus
    /// the draw-down/mutation. No lock/transaction needed since nothing is written here.
    /// </summary>
    private async Task<TokenBalance> GetTokenBalanceCoreAsync(
        Guid paidEntityId,
        Func<CancellationToken, Task<Guid>> resolveSubscriptionIdAsync,
        CancellationToken cancellationToken)
    {
        var now = dateTimeProvider.UtcNow;

        var subscriptionId = await resolveSubscriptionIdAsync(cancellationToken);

        var subscriptionBalance = await context.BalanceSubscriptionTokens
            .SingleAsync(b => b.SubscriptionId == subscriptionId, cancellationToken);

        var purchasedPacks = await GetUnexpiredPurchasedTokenPacksAsync(paidEntityId, now, cancellationToken);

        return new TokenBalance
        {
            FreeTokensCount = subscriptionBalance.FreeTokensCount,
            SubscriptionTokensCount = subscriptionBalance.SubscriptionTokensCount,
            PurchasedTokensCount = purchasedPacks.Sum(p => p.Available),
        };
    }

    /// <summary>
    /// Remaining balance per unexpired purchased pack, ordered soonest-expiring first - not
    /// stored directly, but derived as each pack's original grant minus whatever's already been
    /// charged against it across the ledger. Shared by the reserve draw-down and the read-only
    /// balance query, since both need the same "what's actually left in each pack" computation.
    /// </summary>
    private async Task<List<PurchasedTokenPackAvailability>> GetUnexpiredPurchasedTokenPacksAsync(
        Guid paidEntityId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        return await context.PurchasedTokenPacks
            .Where(p => p.PaidEntityId == paidEntityId && p.ExpiredAt > now)
            .OrderBy(p => p.ExpiredAt)
            .Select(p => new PurchasedTokenPackAvailability(
                p.Id,
                p.TokenPack!.TokensCount,
                context.TokenTransactionPurchasedTokenPacks
                    .Where(c => c.PurchasedTokenPackId == p.Id)
                    .Sum(c => (long?)c.ChargedAmount) ?? 0))
            .ToListAsync(cancellationToken);
    }

    private sealed record PurchasedTokenPackAvailability(Guid Id, long Granted, long Charged)
    {
        public long Available => Granted - Charged;
    }

    public Task<ShortPaginatedResult<TokenTransactionItem>> GetTokenTransactionsAsync(
        Guid paidEntityId,
        Guid? ownerId,
        PaginationData pagination,
        CancellationToken cancellationToken)
    {
        var query = context.TokenTransactions
            .Where(t => t.PaidEntityId == paidEntityId);

        if (ownerId is { } owner)
        {
            query = query.Where(t => t.OwnerId == owner);
        }

        return query
            // CreatedAt alone can tie for transactions created in the same tick - Id as a
            // secondary key keeps paging stable instead of an undefined tie-break order.
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .Select(t => new TokenTransactionItem
            {
                Id = t.Id,
                OwnerId = t.OwnerId,
                Status = t.Status,
                Reason = t.Reason,
                CreatedAt = t.CreatedAt,
                FinishedAt = t.FinishedAt,
                Delta = t.Delta,
                Error = t.Error,
            })
            .ShortPaginateEFAsync(pagination, cancellationToken);
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
        tokenTransaction.FinishedAt = dateTimeProvider.UtcNow;
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
        tokenTransaction.FinishedAt = dateTimeProvider.UtcNow;
        tokenTransaction.Delta = 0;
        tokenTransaction.Error = error;

        await context.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Loads just the transaction row itself - not its spend children, which
    /// <see cref="RefundAsync"/> fetches separately only when it actually needs to touch them.
    /// Locks on the transaction's <c>PaidEntityId</c> *before* the authoritative <c>Status</c>
    /// check below, not after finding it - otherwise two concurrent commit/cancel calls for the
    /// same transaction (or one racing a concurrent reservation for the same entity) could both
    /// read <see cref="TokenSpentStatus.Started"/> before either commits and both refund/finalize
    /// it, the same class of lost-update race <see cref="TryReserveTokensCoreAsync"/> guards against.
    /// </summary>
    private async Task<TokenTransaction> GetStartedTransactionOrThrowAsync(
        Guid tokenTransactionId,
        CancellationToken cancellationToken)
    {
        var paidEntityId = await context.TokenTransactions
            .Where(t => t.Id == tokenTransactionId)
            .Select(t => (Guid?)t.PaidEntityId)
            .SingleOrDefaultAsync(cancellationToken);

        if (paidEntityId is null)
        {
            throw new NotFoundException(string.Format(Errors.TokenTransactionNotFound, tokenTransactionId));
        }

        await context.Database.PgAdvisoryXactLock(paidEntityId.Value.ToString(), cancellationToken);

        var tokenTransaction = await context.TokenTransactions
            .SingleAsync(t => t.Id == tokenTransactionId, cancellationToken);

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
