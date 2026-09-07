using System.Globalization;
using Laraue.Apps.Billing.DataAccess.Entities;

namespace Laraue.Apps.Billing.Services;

/// <summary>
/// Converts a USD-cent price into a target currency. Shared by every host that needs to display a
/// price - takes only the currency properties it needs rather than a whole <see cref="CurrencyRate"/>,
/// so it's usable from a raw <see cref="CoreTariff"/> or any other price source.
/// </summary>
public static class PriceCalculator
{
    public static decimal ConvertPrice(int priceInUsdCents, decimal rateToUsd, decimal roundingStep, RoundingMode roundingMode)
    {
        var amount = priceInUsdCents / 100m / rateToUsd;

        return RoundPrice(amount, roundingStep, roundingMode);
    }

    public static string FormatPrice(int priceInUsdCents, decimal rateToUsd, decimal roundingStep, RoundingMode roundingMode, string symbol)
    {
        var amount = ConvertPrice(priceInUsdCents, rateToUsd, roundingStep, roundingMode);
        var decimals = GetDecimalPlaces(roundingStep);
        var format = decimals > 0 ? "0." + new string('#', decimals) : "0";

        return $"{amount.ToString(format, CultureInfo.InvariantCulture)}{symbol}";
    }

    private static decimal RoundPrice(decimal amount, decimal roundingStep, RoundingMode roundingMode)
    {
        var steps = amount / roundingStep;

        var roundedSteps = roundingMode switch
        {
            RoundingMode.Up => Math.Ceiling(steps),
            RoundingMode.Down => Math.Floor(steps),
            _ => Math.Round(steps, MidpointRounding.AwayFromZero),
        };

        return roundedSteps * roundingStep;
    }

    private static int GetDecimalPlaces(decimal step)
    {
        step = Math.Abs(step);

        var decimals = 0;
        while (step != Math.Floor(step) && decimals < 10)
        {
            step *= 10;
            decimals++;
        }

        return decimals;
    }
}
