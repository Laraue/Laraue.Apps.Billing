using Laraue.Apps.Billing.Payments.Robokassa;
using Laraue.Apps.Billing.Services;
using Laraue.Apps.Billing.Services.Payments;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Core.DateTime.Services.Impl;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Laraue.Apps.Billing.WebApiServices;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddWebApiServices(IConfiguration configuration)
        {
            services.TryAddSingleton<IDateTimeProvider, DateTimeProvider>();

            services.AddScoped<ICoreTariffService, CoreTariffService>();
            services.AddScoped<ITariffService, TariffService>();

            services
                .AddOptions<PaymentsOptions>()
                .Bind(configuration.GetSection(PaymentsOptions.SectionName))
                .ValidateDataAnnotations();

            services
                .AddOptions<PaymentRedirectsOptions>()
                .Bind(configuration.GetSection(PaymentRedirectsOptions.SectionName))
                .ValidateDataAnnotations();

            services.AddSingleton<IPaymentProviderRegistry, PaymentProviderRegistry>();
            services.AddScoped<IPaymentFulfillment, PaymentFulfillmentService>();
            services.AddScoped<ICorePaymentService, CorePaymentService>();
            services.AddScoped<IPaymentsService, PaymentsService>();

            // The payment provider in use is a choice of this line and Payments:DefaultProvider.
            services.AddRobokassaPaymentProvider(configuration);

            return services;
        }
    }
}
