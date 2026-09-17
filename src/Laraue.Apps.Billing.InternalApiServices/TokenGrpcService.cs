using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Laraue.Apps.Billing.DataAccess.Entities;
using Laraue.Apps.Billing.Services;
using Laraue.Core.DataAccess.Contracts;
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
    public override async Task<Internal.Contracts.ReserveTokensResponse> ReservePersonalTokens(
        Internal.Contracts.ReservePersonalTokensRequest request,
        ServerCallContext context)
    {
        var result = await tokenService.TryReservePersonalTokensAsync(
            GrpcParsing.ReadDomainServiceId(context),
            GrpcParsing.ParseGuid(request.UserId, nameof(request.UserId)),
            request.InputTokensCount,
            request.MaxOutputTokensCount,
            context.CancellationToken);

        return ToResponse(result);
    }

    public override async Task<Internal.Contracts.ReserveTokensResponse> ReserveOrganizationTokens(
        Internal.Contracts.ReserveOrganizationTokensRequest request,
        ServerCallContext context)
    {
        var result = await tokenService.TryReserveOrganizationTokensAsync(
            GrpcParsing.ReadDomainServiceId(context),
            GrpcParsing.ParseGuid(request.OrganizationId, nameof(request.OrganizationId)),
            GrpcParsing.ParseGuid(request.UserId, nameof(request.UserId)),
            request.InputTokensCount,
            request.MaxOutputTokensCount,
            context.CancellationToken);

        return ToResponse(result);
    }

    private static Internal.Contracts.ReserveTokensResponse ToResponse(ReservationResult result)
    {
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

    public override async Task<Internal.Contracts.TokenBalanceResponse> GetPersonalTokenBalance(
        Internal.Contracts.GetPersonalTokenBalanceRequest request,
        ServerCallContext context)
    {
        var balance = await tokenService.GetPersonalTokenBalanceAsync(
            GrpcParsing.ReadDomainServiceId(context),
            GrpcParsing.ParseGuid(request.UserId, nameof(request.UserId)),
            context.CancellationToken);

        return ToResponse(balance);
    }

    public override async Task<Internal.Contracts.TokenBalanceResponse> GetOrganizationTokenBalance(
        Internal.Contracts.GetOrganizationTokenBalanceRequest request,
        ServerCallContext context)
    {
        var balance = await tokenService.GetOrganizationTokenBalanceAsync(
            GrpcParsing.ReadDomainServiceId(context),
            GrpcParsing.ParseGuid(request.OrganizationId, nameof(request.OrganizationId)),
            context.CancellationToken);

        return ToResponse(balance);
    }

    private static Internal.Contracts.TokenBalanceResponse ToResponse(TokenBalance balance) => new()
    {
        FreeTokensCount = balance.FreeTokensCount,
        SubscriptionTokensCount = balance.SubscriptionTokensCount,
        PurchasedTokensCount = balance.PurchasedTokensCount,
    };

    public override async Task<Internal.Contracts.GetTokenTransactionsResponse> GetTokenTransactions(
        Internal.Contracts.GetTokenTransactionsRequest request,
        ServerCallContext context)
    {
        var ownerId = string.IsNullOrEmpty(request.OwnerId)
            ? (Guid?)null
            : GrpcParsing.ParseGuid(request.OwnerId, nameof(request.OwnerId));

        var page = await tokenService.GetTokenTransactionsAsync(
            GrpcParsing.ParseGuid(request.PaidEntityId, nameof(request.PaidEntityId)),
            ownerId,
            new PaginationData { Page = request.Page, PerPage = request.PerPage },
            context.CancellationToken);

        var response = new Internal.Contracts.GetTokenTransactionsResponse { HasNextPage = page.HasNextPage };
        response.Items.AddRange(page.Data.Select(ToItem));

        return response;
    }

    private static Internal.Contracts.TokenTransactionItem ToItem(TokenTransactionItem item)
    {
        var contractItem = new Internal.Contracts.TokenTransactionItem
        {
            Id = item.Id.ToString(),
            Status = ToContractStatus(item.Status),
            Reason = ToContractReason(item.Reason),
            CreatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(item.CreatedAt, DateTimeKind.Utc)),
            Delta = item.Delta,
            Error = item.Error ?? string.Empty,
            OwnerId = item.OwnerId.ToString(),
        };

        if (item.FinishedAt is { } finishedAt)
        {
            contractItem.FinishedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(finishedAt, DateTimeKind.Utc));
        }

        return contractItem;
    }

    private static Internal.Contracts.TokenTransactionStatus ToContractStatus(TokenSpentStatus status) => status switch
    {
        TokenSpentStatus.Started => Internal.Contracts.TokenTransactionStatus.Started,
        TokenSpentStatus.Canceled => Internal.Contracts.TokenTransactionStatus.Canceled,
        TokenSpentStatus.Confirmed => Internal.Contracts.TokenTransactionStatus.Confirmed,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    private static Internal.Contracts.TokenTransactionReason ToContractReason(TokenTransactionReason reason) => reason switch
    {
        TokenTransactionReason.TariffGrant => Internal.Contracts.TokenTransactionReason.TariffGrant,
        TokenTransactionReason.DailyGrant => Internal.Contracts.TokenTransactionReason.DailyGrant,
        TokenTransactionReason.Purchase => Internal.Contracts.TokenTransactionReason.Purchase,
        TokenTransactionReason.Expiry => Internal.Contracts.TokenTransactionReason.Expiry,
        TokenTransactionReason.Spend => Internal.Contracts.TokenTransactionReason.Spend,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };
}
