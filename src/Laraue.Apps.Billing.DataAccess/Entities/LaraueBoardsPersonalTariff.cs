namespace Laraue.Apps.Billing.DataAccess.Entities;

public class LaraueBoardsPersonalTariff
{
    public required Guid Id { get; set; }
    public Tariff? Tariff { get; set; }
    public required int? LimitIssuesPerMonth { get; set; }
    public required int? LimitFreeTeamOrganizationsCount { get; set; }
}