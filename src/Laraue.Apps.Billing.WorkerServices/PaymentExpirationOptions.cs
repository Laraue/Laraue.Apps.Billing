using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Billing.WorkerServices;

/// <summary>
/// Bound to the <c>Payments</c> section next to <c>Payments:DefaultProvider</c>, but a class of its own: the worker
/// has no payment provider configuration, and the provider options are validated on first use. No default value:
/// a missing setting must stop the worker on start, not silently expire every pending payment.
/// </summary>
public class PaymentExpirationOptions
{
    public const string SectionName = "Payments";

    /// <summary>
    /// How long a payment may wait for the provider's notification before it is marked
    /// <c>Expired</c>. A customer who pays later is still fulfilled.
    /// </summary>
    [Required]
    public TimeSpan? PendingExpiration { get; set; }
}
