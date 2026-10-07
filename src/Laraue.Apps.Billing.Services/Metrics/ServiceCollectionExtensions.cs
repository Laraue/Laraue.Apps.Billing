using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Laraue.Apps.Billing.Services.Metrics;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers <see cref="BillingMetrics"/>, the event counters. A host also adds
        /// <see cref="BillingMetrics.MeterName"/> to its OpenTelemetry meters.
        /// </summary>
        public IServiceCollection AddBillingMetrics()
        {
            services.AddMetrics();
            services.TryAddSingleton<BillingMetrics>();

            return services;
        }
    }
}
