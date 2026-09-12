namespace Laraue.Apps.Billing.DataAccess.Entities;

public class TokenTransactionPurchasedTokenPack
{
    public long Id { get; set; }

    public Guid TokenTransactionId { get; set; }
    public TokenTransaction? TokenTransaction { get; set; }

    public Guid PurchasedTokenPackId { get; set; }
    public PurchasedTokenPack? PurchasedTokenPack { get; set; }

    public long ChargedAmount { get; set; }
    public long BalanceAfter { get; set; }
}