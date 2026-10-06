using Laraue.Apps.Billing.DataAccess;
using Laraue.Apps.Billing.DataAccess.Entities;
using Laraue.Apps.Billing.Services;
using Laraue.Apps.Billing.WebApiServices.Resources;
using Laraue.Core.Exceptions.Web;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Billing.WebApiServices;

/// <summary>
/// What the reads of this host share: the currency rate lookup and the pricing of a USD-cent amount
/// through <see cref="PriceCalculator"/>, the one place the rounding policy of a currency is applied.
/// </summary>
internal static class CurrencyRateExtensions
{
    public static async Task<CurrencyRate> GetCurrencyRateAsync(
        this DatabaseContext context,
        string currencyCode,
        CancellationToken cancellationToken)
    {
        var code = currencyCode.ToUpperInvariant();

        var currencyRate = await context.CurrencyRates
            .SingleOrDefaultAsync(x => x.Code == code, cancellationToken);

        return currencyRate ?? throw new BadRequestException(
            nameof(currencyCode),
            string.Format(Errors.CurrencyRateNotFound, code));
    }

    /// <summary>
    /// The price, in the units of the currency, of an amount stored in USD cents.
    /// </summary>
    public static decimal Convert(this CurrencyRate rate, int priceInUsdCents) =>
        PriceCalculator.ConvertPrice(priceInUsdCents, rate.RateToUsd, rate.RoundingStep, rate.RoundingMode);

    public static string Format(this CurrencyRate rate, int priceInUsdCents) =>
        PriceCalculator.FormatPrice(priceInUsdCents, rate.RateToUsd, rate.RoundingStep, rate.RoundingMode, rate.Symbol);
}
