namespace Laraue.Apps.Billing.DataAccess.Entities;

/// <summary>
/// Materialized token balances for service subscriptions.
/// </summary>
public class BalanceSubscriptionToken
{
    public Guid SubscriptionId { get; set; }

    public long FreeTokensCount { get; set; }
    public long SubscriptionTokensCount { get; set; }

    /// <summary>
    /// UTC date the daily free allowance (<c>IncludedDailyFreeTokensCount</c>, currently
    /// MarkdownTranslator-only) was last topped up - <see langword="null"/> if it never has been.
    /// Used to reset (not accumulate) <see cref="FreeTokensCount"/> once per day. A date, not an
    /// instant - only "which day" ever matters here, never a time within it.
    /// </summary>
    public DateOnly? LastDailyGrantAt { get; set; }
}