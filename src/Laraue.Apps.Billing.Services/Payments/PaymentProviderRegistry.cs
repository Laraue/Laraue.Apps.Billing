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
    /// The currency tariffs are priced in: the requested one when the default provider can charge it,
    /// the provider's own currency when none is requested. Currencies the provider cannot charge are
    /// never offered, so a price is never shown that cannot be paid.
    /// </summary>
    string ResolveCurrency(string? requestedCurrencyCode);
}

public class PaymentProviderRegistry(
    IEnumerable<IPaymentProvider> providers,
    IOptions<PaymentsOptions> options,
    ILogger<PaymentProviderRegistry> logger) : IPaymentProviderRegistry
{
    public IPaymentProvider Default => Get(options.Value.DefaultProvider);

    public string ResolveCurrency(string? requestedCurrencyCode)
    {
        var provider = Default;
        if (string.IsNullOrWhiteSpace(requestedCurrencyCode))
        {
            // A provider charging in several currencies gets a stable choice; the callers that care
            // name the currency.
            return provider.SupportedCurrencies.Order(StringComparer.Ordinal).First();
        }

        var code = requestedCurrencyCode.ToUpperInvariant();
        if (!provider.SupportedCurrencies.Contains(code))
        {
            logger.LogWarning(
                "Currency {CurrencyCode} is not offered: provider {Provider} charges in {SupportedCurrencies}",
                code,
                provider.Code,
                string.Join(", ", provider.SupportedCurrencies));

            throw new BadRequestException(
                nameof(requestedCurrencyCode),
                string.Format(Errors.PaymentProviderCurrencyNotSupported, provider.Code, code));
        }

        return code;
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
