using Grpc.Core;
using Laraue.Apps.Billing.Services;
// The generated proto service is also called `TokenService`, colliding with the business logic
// class of the same name in Laraue.Apps.Billing.Services - alias it to keep both usable here.
using ContractsTokenService = Laraue.Apps.Billing.Internal.Contracts.TokenService;

namespace Laraue.Apps.Billing.InternalApiServices;

/// <summary>
/// gRPC-facing adapter over <see cref="ITokenService"/>: translates between the wire contract
/// (<c>Laraue.Apps.Billing.Internal.Contracts</c>, string ids) and the DB-backed business logic
/// (<c>Guid</c> ids, a <see cref="ReservationResult"/> record). No business logic of its own.
/// <see cref="Laraue.Core.Exceptions.Web.NotFoundException"/>/<see cref="Laraue.Core.Exceptions.Web.BadRequestException"/>
/// thrown by <see cref="ITokenService"/> (an unknown or already-finalized token_transaction_id)
/// aren't caught here - <c>Laraue.Grpc.Server</c>'s <c>ExceptionTranslationInterceptor</c>
/// (registered via <c>AddLaraueGrpcExceptionHandling()</c> in <c>Program.cs</c>) translates them
/// into <see cref="StatusCode.NotFound"/>/<see cref="StatusCode.InvalidArgument"/> globally.
/// </summary>
public sealed class TokenGrpcService(ITokenService tokenService)
    : ContractsTokenService.TokenServiceBase
{
    public override async Task<Internal.Contracts.ReserveTokensResponse> ReserveTokens(
        Internal.Contracts.ReserveTokensRequest request,
        ServerCallContext context)
    {
        var result = await tokenService.TryReserveTokensAsync(
            GrpcParsing.ReadDomainServiceId(context),
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
        await tokenService.CommitTokensSpentAsync(
            GrpcParsing.ParseGuid(request.TokenTransactionId, nameof(request.TokenTransactionId)),
            request.ActualOutputTokensCount,
            context.CancellationToken);

        return new Internal.Contracts.CommitTokensSpentResponse();
    }

    public override async Task<Internal.Contracts.CancelTokensReservationResponse> CancelTokensReservation(
        Internal.Contracts.CancelTokensReservationRequest request,
        ServerCallContext context)
    {
        await tokenService.CancelTokensReservationAsync(
            GrpcParsing.ParseGuid(request.TokenTransactionId, nameof(request.TokenTransactionId)),
            request.Error,
            context.CancellationToken);

        return new Internal.Contracts.CancelTokensReservationResponse();
    }
}
