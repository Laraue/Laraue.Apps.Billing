using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Billing.DataAccess.Entities;

public class Tariff
{
    public required TariffId Id { get; set; }
    
    [MaxLength(16)]
    public required string Code { get; set; }
    public required int Price { get; set; }
    public required BillingPeriod BillingPeriod { get; set; }
    public required long IncludedTokensCount { get; set; }
    public required bool IsActive { get; set; }
    
    public required TariffType Type { get; set; }
}

public enum BillingPeriod
{
    Month,
}

public enum TariffType
{
    Personal,
    Team,
}

public enum TariffId
{
    LaraueBoardsPersonalFree,
    LaraueBoardsPersonalPlus,
    LaraueBoardsTeamFree,
    LaraueBoardsTeam,
    LaraueBoardsTeamBusiness,
    MarkdownTranslatorFree,
    MarkdownTranslatorPlus,
    MarkdownTranslatorPro,
}