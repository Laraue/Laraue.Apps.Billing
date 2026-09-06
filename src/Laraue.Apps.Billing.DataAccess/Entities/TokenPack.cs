using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Billing.DataAccess.Entities;

public class TokenPack
{
    public Guid Id { get; set; }

    [MaxLength(16)]
    public required string Code { get; set; }

    [MaxLength(32)]
    public required string Title { get; set; }
    public long TokensCount { get; set; }
    public int Price { get; set; }
    public bool IsActive { get; set; }

    public int ExpirationDuration { get; set; }
    public BillingPeriod ExpirationPeriod { get; set; }
}