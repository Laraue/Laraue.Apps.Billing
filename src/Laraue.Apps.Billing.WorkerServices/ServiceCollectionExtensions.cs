using Microsoft.Extensions.DependencyInjection;
using Laraue.Apps.Billing.Services.Metrics;

namespace Laraue.Apps.Billing.WorkerServices;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers what only <c>WorkerHost</c> runs: the database-backed gauges of <see cref="BillingStateMetrics"/>,
        /// published by one host so scaled web/gRPC replicas do not each report the same series.
        /// </summary>
        public IServiceCollection AddWorkerServices()
        {
            services.AddBillingMetrics();
            services.AddSingleton<BillingStateMetrics>();
            services.AddHostedService(sp => sp.GetRequiredService<BillingStateMetrics>());

            return services;
        }
    }
}
