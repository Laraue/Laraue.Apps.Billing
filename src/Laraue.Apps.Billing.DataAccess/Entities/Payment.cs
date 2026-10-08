using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Laraue.Apps.Billing.DataAccess.Entities;

/// <summary>
/// A customer's attempt to pay for a tariff or a token pack. Knows nothing about how a specific
/// payment provider works: the provider's own references live only in <see cref="ProviderPaymentId"/>
/// and <see cref="ProviderData"/>, so replacing a provider never needs a schema change.
/// </summary>
public class Payment
{
    /// <summary>
    /// Our own identifier - the only one the core logic relies on.
    /// </summary>
    public Guid Id { get; set; }

    public ServiceId ServiceId { get; set; }

    public PaymentKind Kind { get; set; }

    /// <summary>
    /// Who is covered by the purchase, e.g. a user or an organization id.
    /// </summary>
    public Guid PaidEntityId { get; set; }

    /// <summary>
    /// Who is paying.
    /// </summary>
    public Guid OwnerId { get; set; }

    /// <summary>
    /// Set when <see cref="Kind"/> is <see cref="PaymentKind.Subscription"/>.
    /// </summary>
    public Guid? TariffId { get; set; }

    /// <summary>
    /// Set when <see cref="Kind"/> is <see cref="PaymentKind.TokenPack"/>.
    /// </summary>
    public Guid? TokenPackId { get; set; }

    /// <summary>
    /// The amount in the currency's minor units (e.g. kopecks, cents), so no floating point is
    /// involved. Fixed when the payment is created.
    /// </summary>
    public long AmountMinorUnits { get; set; }

    /// <summary>
    /// ISO 4217 code, matches <see cref="CurrencyRate.Code"/>.
    /// </summary>
    [MaxLength(3)]
    public required string CurrencyCode { get; set; }

    public PaymentStatus Status { get; set; }

    /// <summary>
    /// Code of the provider that processes this payment, e.g. <c>robokassa</c>.
    /// </summary>
    [MaxLength(32)]
    public required string Provider { get; set; }

    /// <summary>
    /// The provider's own reference to this payment, once known. Unique per
    /// <see cref="Provider"/>; opaque to everything except that provider.
    /// </summary>
    [MaxLength(128)]
    public string? ProviderPaymentId { get; set; }

    /// <summary>
    /// Provider-specific data as a JSON document. Only the provider's own code reads or writes it.
    /// </summary>
    [Column(TypeName = "jsonb")]
    public string? ProviderData { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the provider confirmed the payment, null while it is not paid.
    /// </summary>
    public DateTime? PaidAt { get; set; }
}

public enum PaymentKind
{
    Subscription,
    TokenPack,
}

public enum PaymentStatus
{
    /// <summary>
    /// Created, waiting for the provider's confirmation.
    /// </summary>
    Pending,

    /// <summary>
    /// Confirmed by the provider and fulfilled.
    /// </summary>
    Paid,

    /// <summary>
    /// The provider reported a failure.
    /// </summary>
    Failed,

    /// <summary>
    /// The customer abandoned or cancelled the payment.
    /// </summary>
    Canceled,

    /// <summary>
    /// Still waiting for the provider long after the checkout was created, so most likely abandoned. Set by
    /// the worker, not by the provider. Not final: a notification that the customer did pay still fulfils it.
    /// </summary>
    Expired,
}
