using Laraue.Apps.Billing.Payments.Robokassa;
using Laraue.Apps.Billing.Services;
using Laraue.Apps.Billing.Services.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Billing.WebApiServices;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddWebApiServices(IConfiguration configuration)
        {
            services.AddScoped<ICoreTariffService, CoreTariffService>();
            services.AddScoped<ITariffService, TariffService>();

            services
                .AddOptions<PaymentRedirectsOptions>()
                .Bind(configuration.GetSection(PaymentRedirectsOptions.SectionName))
                .ValidateDataAnnotations();

            services.AddPaymentServices(configuration);
            services.AddScoped<IPaymentsService, PaymentsService>();

            // The payment provider in use is a choice of this line and Payments:DefaultProvider.
            services.AddRobokassaPaymentProvider(configuration);

            return services;
        }
    }
}
