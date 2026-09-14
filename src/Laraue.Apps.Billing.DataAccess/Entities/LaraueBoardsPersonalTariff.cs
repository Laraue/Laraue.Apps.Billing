namespace Laraue.Apps.Billing.DataAccess.Entities;

public class LaraueBoardsPersonalTariff
{
    public required Guid Id { get; set; }
    public Tariff? Tariff { get; set; }
    public required int? LimitIssuesPerMonth { get; set; }

    /// <summary>Temporary MVP-stage override of <see cref="LimitIssuesPerMonth"/> - see the
    /// comment on <see cref="Tariff.IncludedTokensCountMvpOverride"/>.</summary>
    public int? LimitIssuesPerMonthMvpOverride { get; set; }

    public required int? LimitFreeTeamOrganizationsCount { get; set; }

    /// <summary>Temporary MVP-stage override of <see cref="LimitFreeTeamOrganizationsCount"/> - see
    /// the comment on <see cref="Tariff.IncludedTokensCountMvpOverride"/>.</summary>
    public int? LimitFreeTeamOrganizationsCountMvpOverride { get; set; }
}