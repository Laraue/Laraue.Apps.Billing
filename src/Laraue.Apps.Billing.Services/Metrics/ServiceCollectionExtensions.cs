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

        /// <summary>
        /// Registers the database-backed gauges. For a single host only (WorkerHost), see <see cref="BillingStateMetrics"/>.
        /// </summary>
        public IServiceCollection AddBillingStateMetrics()
        {
            services.AddBillingMetrics();
            services.AddSingleton<BillingStateMetrics>();
            services.AddHostedService(sp => sp.GetRequiredService<BillingStateMetrics>());

            return services;
        }
    }
}
