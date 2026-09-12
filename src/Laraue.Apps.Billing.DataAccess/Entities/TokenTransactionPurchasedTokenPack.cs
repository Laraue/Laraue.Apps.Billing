namespace Laraue.Apps.Billing.DataAccess.Entities;

public class TokenTransactionPurchasedTokenPack
{
    public long Id { get; set; }

    public Guid PurchasedTokenPackId { get; set; }
    public PurchasedTokenPack? PurchasedTokenPack { get; set; }

    public long ChargedAmount { get; set; }
    public long BalanceAfter { get; set; }

    /// <summary>
    /// FK back to the owning <see cref="TokenTransaction"/>. Exposed explicitly (rather than left
    /// as an EF shadow property) so it can be queried directly instead of only reachable via
    /// <see cref="TokenTransaction.PurchasedTokensSpent"/>.
    /// </summary>
    public Guid? TokenTransactionId { get; set; }
}