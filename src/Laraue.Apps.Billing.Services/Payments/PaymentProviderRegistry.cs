using Laraue.Apps.Billing.Services.Resources;
using Laraue.Core.Exceptions.Web;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Laraue.Apps.Billing.Services.Payments;

public interface IPaymentProviderRegistry
{
    /// <summary>
    /// The provider new checkouts use unless a caller asks for another one.
    /// </summary>
    IPaymentProvider Default { get; }

    IPaymentProvider Get(string code);

    /// <summary>
    /// The currencies any registered provider can charge in. Tariffs are offered in these only, so a
    /// price is never shown that cannot be paid.
    /// </summary>
    IReadOnlySet<string> SupportedCurrencies { get; }

    /// <summary>
    /// The currency tariffs are priced in: the requested one when some provider can charge it, the
    /// default provider's own currency when none is requested.
    /// </summary>
    string ResolveCurrency(string? requestedCurrencyCode);

    /// <summary>
    /// The provider a checkout in the currency goes through: the default one when it charges in the
    /// currency, otherwise the first registered one that does.
    /// </summary>
    IPaymentProvider GetForCurrency(string currencyCode);
}

public class PaymentProviderRegistry(
    IEnumerable<IPaymentProvider> providers,
    IOptions<PaymentsOptions> options,
    ILogger<PaymentProviderRegistry> logger) : IPaymentProviderRegistry
{
    public IPaymentProvider Default => Get(options.Value.DefaultProvider);

    public IReadOnlySet<string> SupportedCurrencies =>
        providers.SelectMany(p => p.SupportedCurrencies).ToHashSet(StringComparer.Ordinal);

    public string ResolveCurrency(string? requestedCurrencyCode)
    {
        if (string.IsNullOrWhiteSpace(requestedCurrencyCode))
        {
            // A provider charging in several currencies gets a stable choice; the callers that care
            // name the currency.
            return Default.SupportedCurrencies.Order(StringComparer.Ordinal).First();
        }

        var code = requestedCurrencyCode.ToUpperInvariant();
        if (!SupportedCurrencies.Contains(code))
        {
            logger.LogWarning(
                "Currency {CurrencyCode} is not offered: the registered providers charge in {SupportedCurrencies}",
                code,
                string.Join(", ", SupportedCurrencies));

            throw new BadRequestException(
                nameof(requestedCurrencyCode),
                string.Format(Errors.PaymentCurrencyNotAvailable, code));
        }

        return code;
    }

    public IPaymentProvider GetForCurrency(string currencyCode)
    {
        var code = currencyCode.ToUpperInvariant();
        var provider = Default.SupportedCurrencies.Contains(code)
            ? Default
            : providers.FirstOrDefault(p => p.SupportedCurrencies.Contains(code));

        if (provider is null)
        {
            logger.LogWarning("No registered payment provider charges in currency {CurrencyCode}", code);

            throw new BadRequestException(
                nameof(currencyCode),
                string.Format(Errors.PaymentCurrencyNotAvailable, code));
        }

        return provider;
    }

    public IPaymentProvider Get(string code)
    {
        var provider = providers.FirstOrDefault(p => string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase));
        if (provider is null)
        {
            logger.LogWarning(
                "Payment provider '{Code}' is not registered, registered: {RegisteredProviders}",
                code,
                string.Join(", ", providers.Select(p => p.Code)));

            throw new BadRequestException(
                nameof(code),
                string.Format(Errors.PaymentProviderNotFound, code));
        }

        return provider;
    }
}
