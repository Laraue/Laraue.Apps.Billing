using Laraue.Apps.Billing.Services.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Laraue.Apps.Billing.Payments.Robokassa;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers Robokassa as a payment provider and binds its <c>Payments:Robokassa</c> section.
        /// Select it for new checkouts with <c>Payments:DefaultProvider = robokassa</c>.
        /// </summary>
        public IServiceCollection AddRobokassaPaymentProvider(IConfiguration configuration)
        {
            services
                .AddOptions<RobokassaOptions>()
                .Bind(configuration.GetSection(RobokassaOptions.SectionName))
                .Validate(
                    o => !string.IsNullOrWhiteSpace(o.MerchantLogin)
                        && !string.IsNullOrWhiteSpace(o.ActivePassword1)
                        && !string.IsNullOrWhiteSpace(o.ActivePassword2),
                    "Payments:Robokassa needs MerchantLogin and the passwords of the active mode "
                    + "(Password1/Password2, or TestPassword1/TestPassword2 when IsTest is true).");

            services.TryAddEnumerable(ServiceDescriptor.Singleton<IPaymentProvider, RobokassaPaymentProvider>());

            return services;
        }
    }
}
