using Laraue.Apps.Billing.DataAccess;
using Laraue.Apps.Billing.DataAccess.Entities;
using Laraue.Apps.Billing.Services.Resources;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Core.Exceptions.Web;
using Microsoft.EntityFrameworkCore;
// TariffService.cs declares its own `Subscription` DTO record in this same namespace, which
// shadows the DataAccess entity of the same name - alias the entity to keep it unambiguous.
using SubscriptionEntity = Laraue.Apps.Billing.DataAccess.Entities.Subscription;

namespace Laraue.Apps.Billing.Services;

public interface ISubscriptionService
{
    /// <summary>
    /// Returns the caller's active personal subscription on <paramref name="serviceId"/>,
    /// auto-provisioning one on the Free tariff if they don't have one yet - there's no "no
    /// subscription" case left to report, only which tariff they're on.
    /// </summary>
    Task<ActiveSubscription> GetActivePersonalSubscriptionAsync(
        ServiceId serviceId,
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Same as <see cref="GetActivePersonalSubscriptionAsync"/>, but for the organization's active
    /// team subscription.
    /// </summary>
    Task<ActiveSubscription> GetActiveOrganizationSubscriptionAsync(
        ServiceId serviceId,
        Guid organizationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the id of <paramref name="userId"/>'s active personal subscription on
    /// <paramref name="serviceId"/>, auto-provisioning one on the service's Free tariff (and
    /// granting its tokens) if none exists yet. Free is the only tariff this ever provisions -
    /// paid tariffs aren't purchasable, so there's nothing else to create automatically.
    /// </summary>
    Task<Guid> GetOrCreateActivePersonalSubscriptionIdAsync(
        ServiceId serviceId,
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Same as <see cref="GetOrCreateActivePersonalSubscriptionIdAsync"/>, but for
    /// <paramref name="organizationId"/>'s team subscription instead of a user's personal one -
    /// provisions the service's Team Free tariff, not Personal.
    /// </summary>
    Task<Guid> GetOrCreateActiveOrganizationSubscriptionIdAsync(
        ServiceId serviceId,
        Guid organizationId,
        CancellationToken cancellationToken);
}

public class SubscriptionService(DatabaseContext context, IDateTimeProvider dateTimeProvider) : ISubscriptionService
{
    public Task<ActiveSubscription> GetActivePersonalSubscriptionAsync(
        ServiceId serviceId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        return serviceId switch
        {
            ServiceId.LaraueBoards => GetActiveLaraueBoardsPersonalSubscriptionAsync(userId, cancellationToken),
            ServiceId.MarkdownTranslator => GetActiveMarkdownTranslatorPersonalSubscriptionAsync(userId, cancellationToken),
            _ => throw new BadRequestException(
                nameof(serviceId),
                string.Format(Errors.UnknownService, serviceId)),
        };
    }

    public Task<ActiveSubscription> GetActiveOrganizationSubscriptionAsync(
        ServiceId serviceId,
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        return serviceId switch
        {
            ServiceId.LaraueBoards => GetActiveLaraueBoardsTeamSubscriptionAsync(organizationId, cancellationToken),
            _ => throw new BadRequestException(
                nameof(serviceId),
                string.Format(Errors.UnknownService, serviceId)),
        };
    }

    public Task<Guid> GetOrCreateActivePersonalSubscriptionIdAsync(
        ServiceId serviceId,
        Guid userId,
        CancellationToken cancellationToken)
        => GetOrCreateActiveSubscriptionIdCoreAsync(serviceId, userId, isOrganization: false, cancellationToken);

    public Task<Guid> GetOrCreateActiveOrganizationSubscriptionIdAsync(
        ServiceId serviceId,
        Guid organizationId,
        CancellationToken cancellationToken)
        => GetOrCreateActiveSubscriptionIdCoreAsync(serviceId, organizationId, isOrganization: true, cancellationToken);

    /// <summary>
    /// Shared by <see cref="GetOrCreateActivePersonalSubscriptionIdAsync"/>/
    /// <see cref="GetOrCreateActiveOrganizationSubscriptionIdAsync"/> - <paramref name="isOrganization"/>
    /// is an internal-only implementation detail for picking which per-service Free tariff to
    /// provision, not part of either public method's signature.
    /// </summary>
    private async Task<Guid> GetOrCreateActiveSubscriptionIdCoreAsync(
        ServiceId serviceId,
        Guid paidEntityId,
        bool isOrganization,
        CancellationToken cancellationToken)
    {
        // May already be running inside a caller's transaction (e.g. TokenService's reserve path,
        // which needs this provisioning + its own balance mutation to commit atomically together)
        // or may be the only write happening (e.g. a bare "what's my plan" read-path call) - only
        // own the transaction's lifecycle when nobody else already does.
        var ownsTransaction = context.Database.CurrentTransaction is null;
        var dbTransaction = ownsTransaction
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            // Reentrant per session/key - a no-op if the caller (e.g. TokenService) already holds
            // this same lock, so this doesn't double-wait when called from inside its transaction.
            await context.Database.PgAdvisoryXactLock(paidEntityId.ToString(), cancellationToken);

            var now = dateTimeProvider.UtcNow;

            var existingId = await GetActiveSubscriptionsQuery(serviceId, paidEntityId)
                .Select(s => (Guid?)s.Id)
                .SingleOrDefaultAsync(cancellationToken);

            Guid subscriptionId;
            Guid tariffId;
            BalanceSubscriptionToken balance;

            if (existingId is { } id)
            {
                subscriptionId = id;
                tariffId = await context.Subscriptions
                    .Where(s => s.Id == id)
                    .Select(s => s.TariffId)
                    .SingleAsync(cancellationToken);
                balance = await context.BalanceSubscriptionTokens
                    .SingleAsync(b => b.SubscriptionId == id, cancellationToken);
            }
            else
            {
                tariffId = await GetFreeTariffIdAsync(serviceId, isOrganization, cancellationToken)
                    ?? throw new BadRequestException(
                        nameof(serviceId),
                        string.Format(Errors.UnknownService, serviceId));

                var tariff = await context.Tariffs
                    .Where(t => t.Id == tariffId)
                    .Select(t => new { t.IncludedTokensCount, t.IncludedTokensCountMvpOverride })
                    .SingleAsync(cancellationToken);

                var grantAmount = tariff.IncludedTokensCountMvpOverride ?? tariff.IncludedTokensCount;
                subscriptionId = Guid.NewGuid();

                context.Subscriptions.Add(new SubscriptionEntity
                {
                    Id = subscriptionId,
                    ServiceId = serviceId,
                    TariffId = tariffId,
                    OwnerId = paidEntityId,
                    PaidEntityId = paidEntityId,
                    Status = SubscriptionStatus.Active,
                    CurrentPeriodStartedAt = now,
                    // Free is BillingPeriod.Forever - null instead of a real renewal date, since
                    // this is a one-time provision with nothing to renew.
                    CurrentPeriodFinishesAt = null,
                });

                balance = new BalanceSubscriptionToken
                {
                    SubscriptionId = subscriptionId,
                    SubscriptionTokensCount = grantAmount,
                    FreeTokensCount = 0,
                };
                context.BalanceSubscriptionTokens.Add(balance);

                // Represented purely on the parent row, no SubscriptionTokensSpent/
                // PurchasedTokensSpent child - those tables' Charged* fields are spend-shaped, so
                // reusing them for a grant (an addition, not a deduction) would be backwards.
                context.TokenTransactions.Add(new TokenTransaction
                {
                    Id = Guid.NewGuid(),
                    PaidEntityId = paidEntityId,
                    OwnerId = paidEntityId,
                    Status = TokenSpentStatus.Confirmed,
                    Reason = TokenTransactionReason.TariffGrant,
                    CreatedAt = now,
                    FinishedAt = now,
                    Delta = grantAmount,
                });
            }

            await ApplyDailyGrantTopUpIfNeededAsync(tariffId, paidEntityId, balance, now, cancellationToken);

            await context.SaveChangesAsync(cancellationToken);

            if (ownsTransaction)
            {
                await dbTransaction!.CommitAsync(cancellationToken);
            }

            return subscriptionId;
        }
        finally
        {
            if (dbTransaction is not null)
            {
                await dbTransaction.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Only <see cref="MarkdownTranslatorPersonalTariff.IncludedDailyFreeTokensCount"/> has a
    /// daily allowance today - a no-op for any other tariff shape. Resets (doesn't accumulate)
    /// <see cref="BalanceSubscriptionToken.FreeTokensCount"/> once per UTC day - a daily allowance
    /// doesn't roll over. The ledger delta is the *net* change, not the full daily amount, so the
    /// ledger stays an accurate sum of the balance even though unused tokens from the prior day
    /// are being discarded, not carried forward.
    /// </summary>
    private async Task ApplyDailyGrantTopUpIfNeededAsync(
        Guid tariffId,
        Guid paidEntityId,
        BalanceSubscriptionToken balance,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var dailyAmount = await context.MarkdownTranslatorPersonalTariffs
            .Where(t => t.Id == tariffId)
            .Select(t => (long?)t.IncludedDailyFreeTokensCount)
            .SingleOrDefaultAsync(cancellationToken);

        if (dailyAmount is null or <= 0)
        {
            return;
        }

        var today = DateOnly.FromDateTime(now);
        if (balance.LastDailyGrantAt is { } lastGrant && lastGrant >= today)
        {
            return;
        }

        var delta = dailyAmount.Value - balance.FreeTokensCount;
        balance.FreeTokensCount = dailyAmount.Value;
        balance.LastDailyGrantAt = today;

        context.TokenTransactions.Add(new TokenTransaction
        {
            Id = Guid.NewGuid(),
            PaidEntityId = paidEntityId,
            OwnerId = paidEntityId,
            Status = TokenSpentStatus.Confirmed,
            Reason = TokenTransactionReason.DailyGrant,
            CreatedAt = now,
            FinishedAt = now,
            Delta = delta,
        });
    }

    /// <summary>
    /// Resolves the one <see cref="Tariff.IsFree"/> tariff for a service+personal-or-organization
    /// combination, or <see langword="null"/> for a combination that doesn't sell one (e.g.
    /// MarkdownTranslator has no team tariffs at all) - mirrors which combinations
    /// <see cref="GetActivePersonalSubscriptionAsync"/>/<see cref="GetActiveOrganizationSubscriptionAsync"/>
    /// already support.
    /// </summary>
    private Task<Guid?> GetFreeTariffIdAsync(ServiceId serviceId, bool isOrganization, CancellationToken cancellationToken) =>
        (serviceId, isOrganization) switch
        {
            (ServiceId.LaraueBoards, false) => context.LaraueBoardsPersonalTariffs
                .Where(t => t.Tariff!.IsFree)
                .Select(t => (Guid?)t.Id)
                .SingleOrDefaultAsync(cancellationToken),
            (ServiceId.LaraueBoards, true) => context.LaraueBoardsTeamTariffs
                .Where(t => t.Tariff!.IsFree)
                .Select(t => (Guid?)t.Id)
                .SingleOrDefaultAsync(cancellationToken),
            (ServiceId.MarkdownTranslator, false) => context.MarkdownTranslatorPersonalTariffs
                .Where(t => t.Tariff!.IsFree)
                .Select(t => (Guid?)t.Id)
                .SingleOrDefaultAsync(cancellationToken),
            _ => Task.FromResult<Guid?>(null),
        };

    private async Task<ActiveSubscription> GetActiveLaraueBoardsPersonalSubscriptionAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var subscriptionId = await GetOrCreateActivePersonalSubscriptionIdAsync(
            ServiceId.LaraueBoards, userId, cancellationToken);

        var row = await context.Subscriptions
            .Where(s => s.Id == subscriptionId)
            .Join(context.LaraueBoardsPersonalTariffs,
                s => s.TariffId,
                t => t.Tariff!.Id,
                (s, t) => new
                {
                    s.Tariff!.Title,
                    s.Tariff.IncludedTokensCount,
                    s.Tariff.IncludedTokensCountMvpOverride,
                    t.LimitIssuesPerMonth,
                    t.LimitIssuesPerMonthMvpOverride,
                    t.LimitFreeTeamOrganizationsCount,
                    t.LimitFreeTeamOrganizationsCountMvpOverride,
                })
            .SingleAsync(cancellationToken);

        return new LaraueBoardsPersonalActiveSubscription
        {
            Code = row.Title,
            LimitIssuesPerMonth = row.LimitIssuesPerMonthMvpOverride ?? row.LimitIssuesPerMonth,
            LimitFreeTeamOrganizationsCount =
                row.LimitFreeTeamOrganizationsCountMvpOverride ?? row.LimitFreeTeamOrganizationsCount,
            IncludedTokensCount = row.IncludedTokensCountMvpOverride ?? row.IncludedTokensCount,
        };
    }

    private async Task<ActiveSubscription> GetActiveLaraueBoardsTeamSubscriptionAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var subscriptionId = await GetOrCreateActiveOrganizationSubscriptionIdAsync(
            ServiceId.LaraueBoards, organizationId, cancellationToken);

        var row = await context.Subscriptions
            .Where(s => s.Id == subscriptionId)
            .Join(context.LaraueBoardsTeamTariffs,
                s => s.TariffId,
                t => t.Tariff!.Id,
                (s, t) => new
                {
                    s.Tariff!.Title,
                    s.Tariff.IncludedTokensCount,
                    s.Tariff.IncludedTokensCountMvpOverride,
                    t.LimitIssuesPerMonth,
                    t.LimitIssuesPerMonthMvpOverride,
                })
            .SingleAsync(cancellationToken);

        return new LaraueBoardsTeamActiveSubscription
        {
            Code = row.Title,
            LimitIssuesPerMonth = row.LimitIssuesPerMonthMvpOverride ?? row.LimitIssuesPerMonth,
            IncludedTokensCount = row.IncludedTokensCountMvpOverride ?? row.IncludedTokensCount,
        };
    }

    private async Task<ActiveSubscription> GetActiveMarkdownTranslatorPersonalSubscriptionAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var subscriptionId = await GetOrCreateActivePersonalSubscriptionIdAsync(
            ServiceId.MarkdownTranslator, userId, cancellationToken);

        var row = await context.Subscriptions
            .Where(s => s.Id == subscriptionId)
            .Join(context.MarkdownTranslatorPersonalTariffs,
                s => s.TariffId,
                t => t.Tariff!.Id,
                (s, t) => new { s.Tariff!.Title, t.IncludedDailyFreeTokensCount })
            .SingleAsync(cancellationToken);

        return new MarkdownTranslatorActiveSubscription
        {
            Code = row.Title,
            IncludedDailyFreeTokensCount = row.IncludedDailyFreeTokensCount,
        };
    }

    /// <summary>
    /// A subscription counts as active when it hasn't been cancelled AND its current paid period
    /// hasn't lapsed - <see cref="SubscriptionStatus"/> alone isn't enough, since a renewal job
    /// could fail to flip a lapsed row to <see cref="SubscriptionStatus.Cancelled"/> in time. A
    /// null <see cref="SubscriptionEntity.CurrentPeriodFinishesAt"/> means it never lapses (Free).
    /// </summary>
    private IQueryable<SubscriptionEntity> GetActiveSubscriptionsQuery(ServiceId serviceId, Guid paidEntityId)
    {
        var now = dateTimeProvider.UtcNow;

        return context.Subscriptions
            .Where(s => s.ServiceId == serviceId
                && s.PaidEntityId == paidEntityId
                && s.Status == SubscriptionStatus.Active
                && (s.CurrentPeriodFinishesAt == null || s.CurrentPeriodFinishesAt > now));
    }
}

public abstract record ActiveSubscription
{
    public required string Code { get; set; }
}

public sealed record LaraueBoardsPersonalActiveSubscription : ActiveSubscription
{
    public int? LimitIssuesPerMonth { get; set; }
    public int? LimitFreeTeamOrganizationsCount { get; set; }
    public required long IncludedTokensCount { get; set; }
}

public sealed record LaraueBoardsTeamActiveSubscription : ActiveSubscription
{
    public int? LimitIssuesPerMonth { get; set; }
    public required long IncludedTokensCount { get; set; }
}

public sealed record MarkdownTranslatorActiveSubscription : ActiveSubscription
{
    public required long IncludedDailyFreeTokensCount { get; set; }
}
