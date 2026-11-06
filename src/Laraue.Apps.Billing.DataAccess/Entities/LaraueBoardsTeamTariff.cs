namespace Laraue.Apps.Billing.DataAccess.Entities;

public class LaraueBoardsTeamTariff
{
    public required Guid Id { get; set; }
    public Tariff? Tariff { get; set; }
    public required int? LimitIssuesPerMonth { get; set; }

    /// <summary>
    /// File storage the tariff includes, in megabytes: the organization's quota, before a
    /// <see cref="StorageAddon"/>.
    /// </summary>
    public required int IncludedStorageMegabytes { get; set; }
}