using Laraue.Apps.Billing.Services.Metrics;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Core.DateTime.Services.Impl;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Laraue.Apps.Billing.Services.Payments;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the provider-agnostic payment services. A host also registers the provider it
        /// uses, e.g. <c>AddRobokassaPaymentProvider</c>, and sets <c>Payments:DefaultProvider</c>.
        /// </summary>
        public IServiceCollection AddPaymentServices(IConfiguration configuration)
        {
            services.TryAddSingleton<IDateTimeProvider, DateTimeProvider>();
            services.AddBillingMetrics();

            services
                .AddOptions<PaymentsOptions>()
                .Bind(configuration.GetSection(PaymentsOptions.SectionName))
                .ValidateDataAnnotations();

            services.TryAddSingleton<IPaymentProviderRegistry, PaymentProviderRegistry>();
            services.TryAddScoped<IPaymentFulfillment, PaymentFulfillmentService>();
            services.TryAddScoped<ICorePaymentService, CorePaymentService>();

            return services;
        }
    }
}
