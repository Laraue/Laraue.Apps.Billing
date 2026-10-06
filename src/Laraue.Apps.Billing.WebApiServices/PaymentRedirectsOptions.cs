using System.ComponentModel.DataAnnotations;
using Laraue.Apps.Billing.DataAccess.Entities;

namespace Laraue.Apps.Billing.WebApiServices;

/// <summary>
/// Where the customer lands after the payment provider sends them back to the one address we give
/// the provider. Each service has its own pages, so the target is chosen by the service the payment
/// belongs to; the addresses are ours, never taken from a caller.
/// </summary>
public class PaymentRedirectsOptions
{
    public const string SectionName = "Payments:Redirects";

    /// <summary>
    /// Used when the payment cannot be identified or its service has no addresses of its own.
    /// </summary>
    [Required]
    [Url]
    public required string SuccessUrl { get; set; }

    [Required]
    [Url]
    public required string FailUrl { get; set; }

    /// <summary>
    /// The addresses of the services, by <see cref="ServiceId"/> name.
    /// </summary>
    public Dictionary<ServiceId, ServiceRedirectsOptions> Services { get; set; } = [];

    public string GetSuccessUrl(ServiceId? serviceId) =>
        serviceId is { } id && Services.TryGetValue(id, out var service) ? service.SuccessUrl : SuccessUrl;

    public string GetFailUrl(ServiceId? serviceId) =>
        serviceId is { } id && Services.TryGetValue(id, out var service) ? service.FailUrl : FailUrl;
}

public class ServiceRedirectsOptions
{
    [Required]
    [Url]
    public required string SuccessUrl { get; set; }

    [Required]
    [Url]
    public required string FailUrl { get; set; }
}
