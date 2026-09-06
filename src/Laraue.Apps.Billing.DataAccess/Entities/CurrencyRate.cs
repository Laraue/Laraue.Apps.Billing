using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Billing.DataAccess.Entities;

public class CurrencyRate
{
    public required Guid Id { get; set; }

    [MaxLength(3)]
    public required string Code { get; set; }

    [MaxLength(3)]
    public required string Symbol { get; set; }
    public required decimal RateToUsd { get; set; }

    public required decimal RoundingStep { get; set; }
    public required RoundingMode RoundingMode { get; set; }
}

public enum RoundingMode
{
    Nearest,
    Up,
    Down,
}