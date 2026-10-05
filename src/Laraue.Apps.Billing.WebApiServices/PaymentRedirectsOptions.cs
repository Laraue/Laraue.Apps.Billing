using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Billing.WebApiServices;

/// <summary>
/// Where the customer lands after the payment provider sends them back to us.
/// </summary>
public class PaymentRedirectsOptions
{
    public const string SectionName = "Payments:Redirects";

    [Required]
    [Url]
    public string SuccessUrl { get; set; } = null!;

    [Required]
    [Url]
    public string FailUrl { get; set; } = null!;
}
