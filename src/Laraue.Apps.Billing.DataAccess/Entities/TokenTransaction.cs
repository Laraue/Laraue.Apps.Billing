using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Billing.DataAccess.Entities;

public class TokenTransaction
{
    public Guid Id { get; set; }

    public Guid PaidEntityId { get; set; }
    public Guid OwnerId { get; set; }

    /// <summary>
    /// The service the row belongs to: where the tokens were spent, or the service whose tariff or payment
    /// granted them. The metrics label of a spend.
    /// </summary>
    public ServiceId ServiceId { get; set; }

    public TokenSpentStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public long Delta { get; set; }

    public TokenTransactionReason Reason { get; set; }

    /// <summary>
    /// Total tokens held by the reservation (input + max output) at the time it was created via
    /// <c>TryReserveTokens</c>. Fixed for the lifetime of the transaction; used on commit to work
    /// out how much of the reservation to refund.
    /// </summary>
    public long ReservedAmount { get; set; }

    /// <summary>
    /// Input tokens counted toward the reservation - known and fixed at reserve time, unlike
    /// output tokens which are only known when the operation finishes.
    /// </summary>
    public int InputTokensCount { get; set; }

    [MaxLength(32)]
    public string? Error { get; set; }

    public Guid? SubscriptionTokensSpentId { get; set; }
    public TokenTransactionSubscriptionTokenPack? SubscriptionTokensSpent { get; set; }
    public IList<TokenTransactionPurchasedTokenPack>? PurchasedTokensSpent { get; set; }
}

public enum TokenSpentStatus
{
    Started,
    Canceled,
    Confirmed,
}

public enum TokenTransactionReason
{
    /// <summary>
    /// Tokens included in a tariff have been added: a paid tariff was bought, or a free tariff's
    /// monthly allowance was refreshed.
    /// </summary>
    TariffGrant,
    
    /// <summary>
    /// Free daily tokens added.
    /// </summary>
    DailyGrant,
    
    /// <summary>
    /// Tokens have been bought manually.
    /// </summary>
    Purchase,
    
    /// <summary>
    /// Tokens have been expired.
    /// </summary>
    Expiry,

    /// <summary>
    /// Tokens have been reserved/spent by a consuming service's operation.
    /// </summary>
    Spend,
}