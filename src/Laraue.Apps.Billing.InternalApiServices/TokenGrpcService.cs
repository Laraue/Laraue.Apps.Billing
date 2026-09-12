using Grpc.Core;
using Laraue.Apps.Billing.Services;
using Laraue.Core.Exceptions.Web;
// The generated proto service is also called `TokenService`, colliding with the business logic
// class of the same name in Laraue.Apps.Billing.Services - alias it to keep both usable here.
using ContractsTokenService = Laraue.Apps.Billing.Internal.Contracts.TokenService;

namespace Laraue.Apps.Billing.InternalApiServices;

/// <summary>
/// gRPC-facing adapter over <see cref="ITokenService"/>: translates between the wire contract
/// (<c>Laraue.Apps.Billing.Internal.Contracts</c>, string ids) and the DB-backed business logic
/// (<c>Guid</c> ids, a <see cref="ReservationResult"/> record). No business logic of its own.
/// </summary>
public sealed class TokenGrpcService(ITokenService tokenService)
    : ContractsTokenService.TokenServiceBase
{
    public override async Task<Internal.Contracts.ReserveTokensResponse> ReserveTokens(
        Internal.Contracts.ReserveTokensRequest request,
        ServerCallContext context)
    {
        var result = await tokenService.TryReserveTokensAsync(
            GrpcParsing.ToDomainServiceId(request.ServiceId),
            GrpcParsing.ParseGuid(request.PaidEntityId, nameof(request.PaidEntityId)),
            request.InputTokensCount,
            request.MaxOutputTokensCount,
            context.CancellationToken);

        if (result.Error is { } error)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, error));
        }

        return new Internal.Contracts.ReserveTokensResponse
        {
            TokenTransactionId = result.TokenTransactionId!.Value.ToString(),
        };
    }

    public override async Task<Internal.Contracts.CommitTokensSpentResponse> CommitTokensSpent(
        Internal.Contracts.CommitTokensSpentRequest request,
        ServerCallContext context)
    {
        await RunOrThrowAsync(() => tokenService.CommitTokensSpentAsync(
            GrpcParsing.ParseGuid(request.TokenTransactionId, nameof(request.TokenTransactionId)),
            request.ActualOutputTokensCount,
            context.CancellationToken));

        return new Internal.Contracts.CommitTokensSpentResponse();
    }

    public override async Task<Internal.Contracts.CancelTokensReservationResponse> CancelTokensReservation(
        Internal.Contracts.CancelTokensReservationRequest request,
        ServerCallContext context)
    {
        await RunOrThrowAsync(() => tokenService.CancelTokensReservationAsync(
            GrpcParsing.ParseGuid(request.TokenTransactionId, nameof(request.TokenTransactionId)),
            request.Error,
            context.CancellationToken));

        return new Internal.Contracts.CancelTokensReservationResponse();
    }

    /// <summary>Unknown/non-startable token_transaction_id is reported as
    /// <see cref="StatusCode.NotFound"/>/<see cref="StatusCode.FailedPrecondition"/> respectively -
    /// see the error-convention note in token.proto.</summary>
    private static async Task RunOrThrowAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (NotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
        catch (BadRequestException ex)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message));
        }
    }
}
