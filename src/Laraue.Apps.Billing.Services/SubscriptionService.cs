using Laraue.Apps.Billing.DataAccess;
using Laraue.Apps.Billing.DataAccess.Entities;
using Laraue.Apps.Billing.Services.Resources;
using Laraue.Core.Exceptions.Web;
using Microsoft.EntityFrameworkCore;
// TariffService.cs declares its own `Subscription` DTO record in this same namespace, which
// shadows the DataAccess entity of the same name - alias the entity to keep it unambiguous.
using SubscriptionEntity = Laraue.Apps.Billing.DataAccess.Entities.Subscription;

namespace Laraue.Apps.Billing.Services;

public interface ISubscriptionService
{
    /// <summary>
    /// Returns the caller's active personal subscription on <paramref name="serviceId"/>, or
    /// <see langword="null"/> if they don't have one.
    /// </summary>
    Task<ActiveSubscription?> GetActivePersonalSubscriptionAsync(
        ServiceId serviceId,
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the organization's active team subscription on <paramref name="serviceId"/>, or
    /// <see langword="null"/> if it doesn't have one.
    /// </summary>
    Task<ActiveSubscription?> GetActiveOrganizationSubscriptionAsync(
        ServiceId serviceId,
        Guid organizationId,
        CancellationToken cancellationToken);
}

public class SubscriptionService(DatabaseContext context) : ISubscriptionService
{
    public Task<ActiveSubscription?> GetActivePersonalSubscriptionAsync(
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

    public Task<ActiveSubscription?> GetActiveOrganizationSubscriptionAsync(
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

    private async Task<ActiveSubscription?> GetActiveLaraueBoardsPersonalSubscriptionAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var row = await GetActiveSubscriptionsQuery(ServiceId.LaraueBoards, userId)
            .Join(context.LaraueBoardsPersonalTariffs,
                s => s.TariffId,
                t => t.Tariff!.Id,
                (s, t) => new { s.Tariff!.Title, t.LimitIssuesPerMonth, t.LimitFreeTeamOrganizationsCount })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new LaraueBoardsPersonalActiveSubscription
            {
                Code = row.Title,
                LimitIssuesPerMonth = row.LimitIssuesPerMonth,
                LimitFreeTeamOrganizationsCount = row.LimitFreeTeamOrganizationsCount,
            };
    }

    private async Task<ActiveSubscription?> GetActiveLaraueBoardsTeamSubscriptionAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var row = await GetActiveSubscriptionsQuery(ServiceId.LaraueBoards, organizationId)
            .Join(context.LaraueBoardsTeamTariffs,
                s => s.TariffId,
                t => t.Tariff!.Id,
                (s, t) => new { s.Tariff!.Title, t.LimitIssuesPerMonth })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new LaraueBoardsTeamActiveSubscription
            {
                Code = row.Title,
                LimitIssuesPerMonth = row.LimitIssuesPerMonth,
            };
    }

    private async Task<ActiveSubscription?> GetActiveMarkdownTranslatorPersonalSubscriptionAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var row = await GetActiveSubscriptionsQuery(ServiceId.MarkdownTranslator, userId)
            .Join(context.MarkdownTranslatorPersonalTariffs,
                s => s.TariffId,
                t => t.Tariff!.Id,
                (s, t) => new { s.Tariff!.Title, t.IncludedDailyFreeTokensCount })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new MarkdownTranslatorActiveSubscription
            {
                Code = row.Title,
                IncludedDailyFreeTokensCount = row.IncludedDailyFreeTokensCount,
            };
    }

    /// <summary>
    /// A subscription counts as active when it hasn't been cancelled AND its current paid period
    /// hasn't lapsed - <see cref="SubscriptionStatus"/> alone isn't enough, since a renewal job
    /// could fail to flip a lapsed row to <see cref="SubscriptionStatus.Cancelled"/> in time.
    /// </summary>
    private IQueryable<SubscriptionEntity> GetActiveSubscriptionsQuery(ServiceId serviceId, Guid paidEntityId)
    {
        var now = DateTime.UtcNow;

        return context.Subscriptions
            .Where(s => s.ServiceId == serviceId
                && s.PaidEntityId == paidEntityId
                && s.Status == SubscriptionStatus.Active
                && s.CurrentPeriodFinishesAt > now);
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
}

public sealed record LaraueBoardsTeamActiveSubscription : ActiveSubscription
{
    public int? LimitIssuesPerMonth { get; set; }
}

public sealed record MarkdownTranslatorActiveSubscription : ActiveSubscription
{
    public required long IncludedDailyFreeTokensCount { get; set; }
}
