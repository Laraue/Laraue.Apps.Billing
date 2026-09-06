using Laraue.Apps.Billing.DataAccess.Entities;

namespace Laraue.Apps.Billing.DataAccess.Data;

public class CurrencyRatesData
{
    private static readonly CurrencyRate Usd = new()
    {
        Id = new Guid("E3F15A99-C08E-420C-9ACA-8BD35A13B4EC"),
        Code = "USD",
        RateToUsd = 1,
        RoundingStep = 0.01M,
        RoundingMode = RoundingMode.Nearest,
    };

    private static readonly CurrencyRate Rub = new()
    {
        Id = new Guid("D8B3DB18-09F8-4CD8-B4B8-C6D4402E2292"),
        Code = "RUB",
        RateToUsd = 0.012M,
        RoundingStep = 1M,
        RoundingMode = RoundingMode.Up,
    };

    public static readonly CurrencyRate[] CurrencyRates =
    [
        Usd,
        Rub,
    ];
}