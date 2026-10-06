using Grpc.Core;
using Laraue.Apps.Billing.DataAccess.Entities;
using Laraue.Apps.Billing.Services.Payments;
// The generated proto service is also called `PaymentService` - alias it to keep names unambiguous.
using ContractsPaymentService = Laraue.Apps.Billing.Internal.Contracts.PaymentService;

namespace Laraue.Apps.Billing.InternalApiServices;

/// <summary>
/// gRPC-facing adapter over <see cref="ICorePaymentService"/>: translates the wire contract (string
/// ids, proto enums) to the domain types. No business logic and no knowledge of any payment
/// provider. Exceptions of the core service (<see cref="Laraue.Core.Exceptions.Web.BadRequestException"/>)
/// are translated into status codes by <c>Laraue.Grpc.Server</c>'s exception interceptor.
/// </summary>
public sealed class PaymentGrpcService(ICorePaymentService corePaymentService)
    : ContractsPaymentService.PaymentServiceBase
{
    public override Task<Internal.Contracts.CreateCheckoutResponse> CreatePersonalCheckout(
        Internal.Contracts.CreatePersonalCheckoutRequest request,
        ServerCallContext context)
    {
        var userId = GrpcParsing.ParseGuid(request.UserId, nameof(request.UserId));

        return CreateCheckoutAsync(
            context,
            paidEntityId: userId,
            isOrganization: false,
            ownerId: userId,
            request.Kind,
            request.ItemId,
            request.CurrencyCode,
            request.ReturnUrl);
    }

    public override Task<Internal.Contracts.CreateCheckoutResponse> CreateOrganizationCheckout(
        Internal.Contracts.CreateOrganizationCheckoutRequest request,
        ServerCallContext context)
    {
        return CreateCheckoutAsync(
            context,
            paidEntityId: GrpcParsing.ParseGuid(request.OrganizationId, nameof(request.OrganizationId)),
            isOrganization: true,
            ownerId: GrpcParsing.ParseGuid(request.UserId, nameof(request.UserId)),
            request.Kind,
            request.ItemId,
            request.CurrencyCode,
            request.ReturnUrl);
    }

    private async Task<Internal.Contracts.CreateCheckoutResponse> CreateCheckoutAsync(
        ServerCallContext context,
        Guid paidEntityId,
        bool isOrganization,
        Guid ownerId,
        Internal.Contracts.PaymentItemKind kind,
        string itemId,
        string currencyCode,
        string returnUrl)
    {
        var checkout = await corePaymentService.CreateAsync(
            new CreatePaymentRequest
            {
                ServiceId = GrpcParsing.ReadDomainServiceId(context),
                Kind = ToDomainKind(kind),
                ItemId = GrpcParsing.ParseGuid(itemId, nameof(itemId)),
                PaidEntityId = paidEntityId,
                IsOrganization = isOrganization,
                OwnerId = ownerId,
                CurrencyCode = currencyCode,
                ReturnUrl = string.IsNullOrEmpty(returnUrl) ? null : returnUrl,
            },
            context.CancellationToken);

        return new Internal.Contracts.CreateCheckoutResponse
        {
            PaymentId = checkout.PaymentId.ToString(),
            Url = checkout.Url,
        };
    }

    private static PaymentKind ToDomainKind(Internal.Contracts.PaymentItemKind kind) => kind switch
    {
        Internal.Contracts.PaymentItemKind.Subscription => PaymentKind.Subscription,
        Internal.Contracts.PaymentItemKind.TokenPack => PaymentKind.TokenPack,
        _ => throw new RpcException(new Status(StatusCode.InvalidArgument, $"Unknown item kind '{kind}'.")),
    };
}
