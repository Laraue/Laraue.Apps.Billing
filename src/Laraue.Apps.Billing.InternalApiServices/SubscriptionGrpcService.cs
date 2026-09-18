using Grpc.Core;
using Laraue.Apps.Billing.Services;
// The generated proto service is also called `SubscriptionService`, colliding with the business
// logic class of the same name in Laraue.Apps.Billing.Services - alias it to keep both usable here.
using ContractsSubscriptionService = Laraue.Apps.Billing.Internal.Contracts.SubscriptionService;

namespace Laraue.Apps.Billing.InternalApiServices;

/// <summary>
/// gRPC-facing adapter over <see cref="ISubscriptionService"/>: translates between the wire
/// contract (<c>Laraue.Apps.Billing.Internal.Contracts</c>, string ids, a polymorphic
/// <c>oneof</c> response) and the DB-backed business logic (<c>Guid</c> ids, an
/// <see cref="ActiveSubscription"/> record hierarchy). No business logic of its own.
/// <see cref="Laraue.Core.Exceptions.Web.BadRequestException"/> (an unknown <c>ServiceId</c> or
/// currency) isn't caught here - <c>Laraue.Grpc.Server</c>'s <c>ExceptionTranslationInterceptor</c>
/// (registered via <c>AddLaraueGrpcExceptionHandling()</c> in <c>Program.cs</c>) translates it into
/// <see cref="StatusCode.InvalidArgument"/> globally. There's no "no active subscription" case to
/// report either - <see cref="ISubscriptionService"/> auto-provisions Free onto a paid entity with
/// none yet, so both rpcs below always return a real subscription.
/// </summary>
public sealed class SubscriptionGrpcService(ISubscriptionService subscriptionService)
    : ContractsSubscriptionService.SubscriptionServiceBase
{
    public override async Task<Internal.Contracts.ActiveSubscriptionResponse> GetActivePersonalSubscription(
        Internal.Contracts.GetActivePersonalSubscriptionRequest request,
        ServerCallContext context)
    {
        var subscription = await subscriptionService.GetActivePersonalSubscriptionAsync(
            GrpcParsing.ToDomainServiceId(request.ServiceId),
            GrpcParsing.ParseGuid(request.UserId, nameof(request.UserId)),
            context.CancellationToken);

        return ToResponse(subscription);
    }

    public override async Task<Internal.Contracts.ActiveSubscriptionResponse> GetActiveOrganizationSubscription(
        Internal.Contracts.GetActiveOrganizationSubscriptionRequest request,
        ServerCallContext context)
    {
        var subscription = await subscriptionService.GetActiveOrganizationSubscriptionAsync(
            GrpcParsing.ToDomainServiceId(request.ServiceId),
            GrpcParsing.ParseGuid(request.OrganizationId, nameof(request.OrganizationId)),
            context.CancellationToken);

        return ToResponse(subscription);
    }

    private static Internal.Contracts.ActiveSubscriptionResponse ToResponse(ActiveSubscription subscription)
    {
        var response = new Internal.Contracts.ActiveSubscriptionResponse { Code = subscription.Code };

        switch (subscription)
        {
            case LaraueBoardsPersonalActiveSubscription s:
                var personal = new Internal.Contracts.LaraueBoardsPersonalSubscription
                {
                    IncludedTokensCount = s.IncludedTokensCount,
                };
                if (s.LimitIssuesPerMonth is { } personalLimit) personal.LimitIssuesPerMonth = personalLimit;
                if (s.LimitFreeTeamOrganizationsCount is { } limitOrgs) personal.LimitFreeTeamOrganizationsCount = limitOrgs;
                response.LaraueBoardsPersonal = personal;
                break;

            case LaraueBoardsTeamActiveSubscription s:
                var team = new Internal.Contracts.LaraueBoardsTeamSubscription
                {
                    IncludedTokensCount = s.IncludedTokensCount,
                };
                if (s.LimitIssuesPerMonth is { } teamLimit) team.LimitIssuesPerMonth = teamLimit;
                response.LaraueBoardsTeam = team;
                break;

            case MarkdownTranslatorActiveSubscription s:
                response.MarkdownTranslator = new Internal.Contracts.MarkdownTranslatorSubscription
                {
                    IncludedDailyFreeTokensCount = s.IncludedDailyFreeTokensCount,
                };
                break;

            default:
                throw new InvalidOperationException($"Unmapped subscription type '{subscription.GetType()}'.");
        }

        return response;
    }
}
