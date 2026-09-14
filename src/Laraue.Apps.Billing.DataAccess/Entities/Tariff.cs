using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Billing.DataAccess.Entities;

public class Tariff
{
    public required Guid Id { get; set; }

    [MaxLength(32)]
    public required string Title { get; set; }
    public required int Price { get; set; }
    public required BillingPeriod BillingPeriod { get; set; }
    public required long IncludedTokensCount { get; set; }

    /// <summary>
    /// Temporary override of <see cref="IncludedTokensCount"/>, actually enforced in place of it
    /// while set. Lets a tariff's real, publicly-advertised grant stay accurate on the pricing page
    /// even while its effective grant is raised for an MVP stage where it isn't purchasable yet -
    /// see the comment on the Free tariffs' seed data. Ending that stage is nulling this out, not
    /// re-typing the real value back in.
    /// </summary>
    public long? IncludedTokensCountMvpOverride { get; set; }

    public required bool IsActive { get; set; }

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