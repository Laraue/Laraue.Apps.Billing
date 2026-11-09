using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Billing.DataAccess.Entities;

/// <summary>
/// Extra file storage bought on top of a paid tariff of a service, e.g. +10 GB for Laraue Boards.
/// Not sold on its own: it's added to a tariff's checkout and paid in the same payment, monthly like
/// the tariff, and the subscription keeps it until it's renewed with another one or without one.
/// </summary>
public class StorageAddon
{
    public required Guid Id { get; set; }

    public ServiceId ServiceId { get; set; }
    public Service? Service { get; set; }

    [MaxLength(16)]
    public required string Code { get; set; }

    [MaxLength(32)]
    public required string Title { get; set; }

    /// <summary>
    /// The storage added to the tariff's own, in megabytes.
    /// </summary>
    public required int StorageMegabytes { get; set; }

    /// <summary>
    /// The price per month in USD cents, added to the tariff's.
    /// </summary>
    public required int Price { get; set; }

    public required bool IsActive { get; set; }
}
