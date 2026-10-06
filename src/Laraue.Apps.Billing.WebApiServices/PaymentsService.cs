using Laraue.Apps.Billing.DataAccess;
using Laraue.Apps.Billing.Services.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Laraue.Apps.Billing.WebApiServices;

/// <summary>
/// What a payment provider's request to one of our callback addresses carries, whichever address it is
/// (notification, success or fail).
/// </summary>
public sealed record PaymentCallback
{
    /// <summary>
    /// Code of the provider that called, taken from the URL.
    /// </summary>
    public required string Provider { get; init; }

    /// <summary>
    /// Query string and form values merged into one dictionary.
    /// </summary>
    public required IReadOnlyDictionary<string, string> Parameters { get; init; }

    public required IReadOnlyDictionary<string, string> Headers { get; init; }

    public string? Body { get; init; }
}

public interface IPaymentsService
{
    /// <summary>
    /// Handles a provider's notification and returns the body the provider expects as the answer.
    /// </summary>
    Task<string> HandleNotification(PaymentCallback callback, CancellationToken cancellationToken);

    /// <summary>
    /// Where to send the customer when the provider returns them after a payment. Returning to the
    /// success address is not a proof of payment, only a notification is.
    /// </summary>
    Task<string> GetSuccessUrl(PaymentCallback callback, CancellationToken cancellationToken);

    Task<string> GetFailUrl(PaymentCallback callback, CancellationToken cancellationToken);
}

public class PaymentsService(
    DatabaseContext context,
    ICorePaymentService corePaymentService,
    IOptions<PaymentRedirectsOptions> redirectsOptions,
    ILogger<PaymentsService> logger) : IPaymentsService
{
    public async Task<string> HandleNotification(PaymentCallback callback, CancellationToken cancellationToken)
    {
        // The core service needs a transaction and leaves its lifecycle to the host.
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var result = await corePaymentService.HandleNotificationAsync(
            callback.Provider,
            new PaymentNotificationRequest
            {
                Parameters = callback.Parameters,
                Headers = callback.Headers,
                Body = callback.Body,
            },
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Answering the {Provider} notification with '{Acknowledgement}'",
            callback.Provider,
            result.Acknowledgement);

        return result.Acknowledgement;
    }

    public async Task<string> GetSuccessUrl(PaymentCallback callback, CancellationToken cancellationToken)
    {
        var serviceId = await corePaymentService.FindReturnedPaymentServiceAsync(
            callback.Provider,
            callback.Parameters,
            cancellationToken);
        var url = redirectsOptions.Value.GetSuccessUrl(serviceId);

        logger.LogInformation(
            "Customer returned from {Provider} after paying for service {ServiceId}, redirecting to {Url}",
            callback.Provider,
            serviceId,
            url);

        return url;
    }

    public async Task<string> GetFailUrl(PaymentCallback callback, CancellationToken cancellationToken)
    {
        var serviceId = await corePaymentService.FindReturnedPaymentServiceAsync(
            callback.Provider,
            callback.Parameters,
            cancellationToken);
        var url = redirectsOptions.Value.GetFailUrl(serviceId);

        logger.LogInformation(
            "Customer returned from {Provider} without paying for service {ServiceId}, redirecting to {Url}",
            callback.Provider,
            serviceId,
            url);

        return url;
    }
}
