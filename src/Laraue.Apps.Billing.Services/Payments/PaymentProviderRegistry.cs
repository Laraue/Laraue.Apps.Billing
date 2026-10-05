using Laraue.Apps.Billing.Services.Resources;
using Laraue.Core.Exceptions.Web;
using Microsoft.Extensions.Options;

namespace Laraue.Apps.Billing.Services.Payments;

public interface IPaymentProviderRegistry
{
    /// <summary>
    /// The provider new checkouts use unless a caller asks for another one.
    /// </summary>
    IPaymentProvider Default { get; }

    IPaymentProvider Get(string code);
}

public class PaymentProviderRegistry(
    IEnumerable<IPaymentProvider> providers,
    IOptions<PaymentsOptions> options) : IPaymentProviderRegistry
{
    public IPaymentProvider Default => Get(options.Value.DefaultProvider);

    public IPaymentProvider Get(string code)
    {
        return providers.FirstOrDefault(p => string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase))
            ?? throw new BadRequestException(
                nameof(code),
                string.Format(Errors.PaymentProviderNotFound, code));
    }
}
