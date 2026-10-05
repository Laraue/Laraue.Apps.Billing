namespace Laraue.Apps.Billing.Services.Payments;

/// <summary>
/// A payment provider (Robokassa, a card acquirer, ...). The rest of Billing talks to providers only
/// through this interface and the neutral types next to it, so replacing a provider means adding
/// another implementation - nothing in <see cref="ICorePaymentService"/>, the entities or the
/// schema knows which provider is behind a payment.
/// </summary>
public interface IPaymentProvider
{
    /// <summary>
    /// Stable lowercase code stored in <c>Payment.Provider</c> and used in notification URLs,
    /// e.g. <c>robokassa</c>. Never change it for a provider that already has payments.
    /// </summary>
    string Code { get; }

    /// <summary>
    /// ISO 4217 codes of the currencies the provider can charge in.
    /// </summary>
    IReadOnlySet<string> SupportedCurrencies { get; }

    /// <summary>
    /// Builds the page the customer is sent to in order to pay.
    /// </summary>
    Task<PaymentCheckoutResult> CreateCheckoutAsync(
        PaymentCheckoutRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Verifies the authenticity of a notification (signature, secret, ...) and turns it into the
    /// provider-neutral <see cref="PaymentNotification"/>. Throws a
    /// <see cref="Laraue.Core.Exceptions.Web.BadRequestException"/> for a notification that cannot
    /// be trusted - the core never sees an unverified one.
    /// </summary>
    PaymentNotification ParseNotification(PaymentNotificationRequest request);

    /// <summary>
    /// The body the provider expects as the answer to a handled notification, e.g. <c>OK123</c>.
    /// </summary>
    string CreateNotificationAck(PaymentNotification notification);
}

public sealed record PaymentCheckoutRequest
{
    /// <summary>
    /// Our <c>Payment.Id</c>. The provider must be able to return it, or its own reference, in the
    /// notification.
    /// </summary>
    public required Guid PaymentId { get; init; }

    public required long AmountMinorUnits { get; init; }

    public required string CurrencyCode { get; init; }

    /// <summary>
    /// What the customer is paying for, to show on the provider's page.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Where to send the customer back after paying, when the provider supports it. The provider
    /// falls back to its own configured address when this is null.
    /// </summary>
    public string? ReturnUrl { get; init; }
}

public sealed record PaymentCheckoutResult
{
    /// <summary>
    /// The address to redirect the customer to.
    /// </summary>
    public required string Url { get; init; }

    /// <summary>
    /// The provider's own reference to the payment, when it is known at this point.
    /// </summary>
    public string? ProviderPaymentId { get; init; }

    /// <summary>
    /// Provider-specific data to keep with the payment, as a JSON document.
    /// </summary>
    public string? ProviderData { get; init; }
}

/// <summary>
/// An incoming notification as the host received it, without any provider-specific parsing.
/// </summary>
public sealed record PaymentNotificationRequest
{
    /// <summary>
    /// Query string and form values merged into one case-sensitive dictionary.
    /// </summary>
    public required IReadOnlyDictionary<string, string> Parameters { get; init; }

    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// The raw request body, for providers that sign or send JSON.
    /// </summary>
    public string? Body { get; init; }
}

public sealed record PaymentNotification
{
    /// <summary>
    /// Our <c>Payment.Id</c>, when the provider returned it.
    /// </summary>
    public Guid? PaymentId { get; init; }

    /// <summary>
    /// The provider's own reference, when the provider only returns that one.
    /// </summary>
    public string? ProviderPaymentId { get; init; }

    public required PaymentNotificationOutcome Outcome { get; init; }

    /// <summary>
    /// The amount the provider says was paid, compared with the payment's own amount before anything
    /// is granted. Null when the provider does not report it.
    /// </summary>
    public long? AmountMinorUnits { get; init; }

    public string? CurrencyCode { get; init; }

    /// <summary>
    /// Provider-specific data to merge into <c>Payment.ProviderData</c>, as a JSON document.
    /// </summary>
    public string? ProviderData { get; init; }
}

public enum PaymentNotificationOutcome
{
    Paid,
    Failed,
    Canceled,
}
