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
                .ValidateDataAnnotations();

            services.TryAddEnumerable(ServiceDescriptor.Singleton<IPaymentProvider, RobokassaPaymentProvider>());

            return services;
        }
    }
}
