using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Billing.Services.Payments;

public class PaymentsOptions
{
    public const string SectionName = "Payments";

    /// <summary>
    /// Code of the provider that new checkouts use when the caller does not name one.
    /// </summary>
    [Required]
    public string DefaultProvider { get; set; } = null!;
}
