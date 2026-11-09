namespace Laraue.Apps.Billing.DataAccess.Entities;

public class LaraueBoardsPersonalTariff
{
    public required Guid Id { get; set; }
    public Tariff? Tariff { get; set; }
    public required int? LimitIssuesPerMonth { get; set; }
    public required int? LimitFreeTeamOrganizationsCount { get; set; }

    /// <summary>
    /// File storage the tariff includes, in megabytes: the quota of the user's personal organization,
    /// before a <see cref="StorageAddon"/>.
    /// </summary>
    public required int IncludedStorageMegabytes { get; set; }
}