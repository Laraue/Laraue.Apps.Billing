using Laraue.Apps.Billing.Services.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Laraue.Apps.Billing.WebApiServices;

public sealed record HandlePaymentNotificationRequest
{
    /// <summary>
    /// Code of the provider that sent the notification, taken from the URL.
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
    Task<string> HandleNotification(HandlePaymentNotificationRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Where to send the customer when the provider returns them after a payment. Returning to the
    /// success address is not a proof of payment, only a notification is.
    /// </summary>
    string GetSuccessUrl(string provider);

    string GetFailUrl(string provider);
}

public class PaymentsService(
    ICorePaymentService corePaymentService,
    IOptions<PaymentRedirectsOptions> redirectsOptions,
    ILogger<PaymentsService> logger) : IPaymentsService
{
    public async Task<string> HandleNotification(
        HandlePaymentNotificationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await corePaymentService.HandleNotificationAsync(
            request.Provider,
            new PaymentNotificationRequest
            {
                Parameters = request.Parameters,
                Headers = request.Headers,
                Body = request.Body,
            },
            cancellationToken);

        logger.LogInformation(
            "Answering the {Provider} notification with '{Acknowledgement}'",
            request.Provider,
            result.Acknowledgement);

        return result.Acknowledgement;
    }

    public string GetSuccessUrl(string provider)
    {
        var url = redirectsOptions.Value.SuccessUrl;

        logger.LogInformation("Customer returned from {Provider} after paying, redirecting to {Url}", provider, url);

        return url;
    }

    public string GetFailUrl(string provider)
    {
        var url = redirectsOptions.Value.FailUrl;

        logger.LogInformation("Customer returned from {Provider} without paying, redirecting to {Url}", provider, url);

        return url;
    }
}
