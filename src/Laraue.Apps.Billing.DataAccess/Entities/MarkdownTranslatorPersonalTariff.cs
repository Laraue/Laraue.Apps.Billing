namespace Laraue.Apps.Billing.DataAccess.Entities;

public class MarkdownTranslatorPersonalTariff
{
    public required Guid Id { get; set; }
    public Tariff? Tariff { get; set; }
    public required long IncludedDailyFreeTokensCount { get; set; }
}