using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Billing.DataAccess.Entities;

public class Tariff
{
    public required Guid Id { get; set; }

    [MaxLength(32)]
    public required string Title { get; set; }
    public required int Price { get; set; }
    public required BillingPeriod BillingPeriod { get; set; }

    /// <summary>
    /// Tokens the tariff includes per month. For a <see cref="IsFree"/> tariff with
    /// <see cref="BillingPeriod.Forever"/> nothing is ever renewed, so this is the free monthly
    /// allowance instead: <see cref="BalanceSubscriptionToken.FreeTokensCount"/> is reset to it once
    /// per UTC month (it doesn't roll over) - see <c>SubscriptionService</c>. Zero means the tariff
    /// has no monthly allowance (e.g. Markdown Translator's Free tariff, which has a daily one).
    /// </summary>
    public required long IncludedTokensCount { get; set; }

    public required bool IsActive { get; set; }

    /// <summary>
    /// Marks the one tariff per service+type that auto-provisioning should create for a paid
    /// entity with no subscription yet - explicit rather than inferring "free" from
    /// <see cref="Price"/> == 0, since a future zero-price promo tariff wouldn't be this one.
    /// </summary>
    public required bool IsFree { get; set; }

    public required TariffType Type { get; set; }
}

public enum BillingPeriod
{
    Month,
    Forever,
}

public enum TariffType
{
    Personal,
    Team,
}