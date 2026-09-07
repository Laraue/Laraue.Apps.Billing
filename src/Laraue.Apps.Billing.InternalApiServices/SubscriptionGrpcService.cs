using Grpc.Core;
using Laraue.Apps.Billing.Services;
using Laraue.Core.Exceptions.Web;
// The generated proto service is also called `SubscriptionService`, colliding with the business
// logic class of the same name in Laraue.Apps.Billing.Services - alias it to keep both usable here.
using ContractsSubscriptionService = Laraue.Apps.Billing.Internal.Contracts.SubscriptionService;
using ContractsServiceId = Laraue.Apps.Billing.Internal.Contracts.ServiceId;
using DomainServiceId = Laraue.Apps.Billing.DataAccess.Entities.ServiceId;

namespace Laraue.Apps.Billing.InternalApiServices;

/// <summary>
/// gRPC-facing adapter over <see cref="ISubscriptionService"/>: translates between the wire
/// contract (<c>Laraue.Apps.Billing.Internal.Contracts</c>, string ids, a polymorphic
/// <c>oneof</c> response) and the DB-backed business logic (<c>Guid</c> ids, an
/// <see cref="ActiveSubscription"/> record hierarchy). No business logic of its own.
/// </summary>
public sealed class SubscriptionGrpcService(ISubscriptionService subscriptionService)
    : ContractsSubscriptionService.SubscriptionServiceBase
{
    public override async Task<Internal.Contracts.ActiveSubscriptionResponse> GetActivePersonalSubscription(
        Internal.Contracts.GetActivePersonalSubscriptionRequest request,
        ServerCallContext context)
    {
        var subscription = await GetSubscriptionOrThrowAsync(() => subscriptionService
            .GetActivePersonalSubscriptionAsync(
                ToDomainServiceId(request.ServiceId),
                ParseGuid(request.UserId, nameof(request.UserId)),
                context.CancellationToken));

        return ToResponse(subscription);
    }

    public override async Task<Internal.Contracts.ActiveSubscriptionResponse> GetActiveOrganizationSubscription(
        Internal.Contracts.GetActiveOrganizationSubscriptionRequest request,
        ServerCallContext context)
    {
        var subscription = await GetSubscriptionOrThrowAsync(() => subscriptionService
            .GetActiveOrganizationSubscriptionAsync(
                ToDomainServiceId(request.ServiceId),
                ParseGuid(request.OrganizationId, nameof(request.OrganizationId)),
                context.CancellationToken));

        return ToResponse(subscription);
    }

    /// <summary>No active subscription is reported as <see cref="StatusCode.NotFound"/>, not a
    /// response variant - see the "no subscription" note in subscription.proto.</summary>
    private static async Task<ActiveSubscription> GetSubscriptionOrThrowAsync(
        Func<Task<ActiveSubscription?>> getSubscription)
    {
        ActiveSubscription? subscription;

        try
        {
            subscription = await getSubscription();
        }
        catch (BadRequestException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }

        return subscription ?? throw new RpcException(new Status(StatusCode.NotFound, "No active subscription."));
    }

    private static Internal.Contracts.ActiveSubscriptionResponse ToResponse(ActiveSubscription subscription)
    {
        var response = new Internal.Contracts.ActiveSubscriptionResponse { Code = subscription.Code };

        switch (subscription)
        {
            case LaraueBoardsPersonalActiveSubscription s:
                var personal = new Internal.Contracts.LaraueBoardsPersonalSubscription();
                if (s.LimitIssuesPerMonth is { } personalLimit) personal.LimitIssuesPerMonth = personalLimit;
                if (s.LimitFreeTeamOrganizationsCount is { } limitOrgs) personal.LimitFreeTeamOrganizationsCount = limitOrgs;
                response.LaraueBoardsPersonal = personal;
                break;

            case LaraueBoardsTeamActiveSubscription s:
                var team = new Internal.Contracts.LaraueBoardsTeamSubscription();
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

    private static DomainServiceId ToDomainServiceId(ContractsServiceId serviceId) => serviceId switch
    {
        ContractsServiceId.LaraueBoards => DomainServiceId.LaraueBoards,
        ContractsServiceId.MarkdownTranslator => DomainServiceId.MarkdownTranslator,
        _ => throw new RpcException(new Status(StatusCode.InvalidArgument, $"Unknown service '{serviceId}'.")),
    };

    private static Guid ParseGuid(string value, string fieldName) =>
        Guid.TryParse(value, out var guid)
            ? guid
            : throw new RpcException(new Status(StatusCode.InvalidArgument, $"'{fieldName}' is not a valid GUID."));
}
