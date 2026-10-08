using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Laraue.Apps.Billing.Services.Metrics;

namespace Laraue.Apps.Billing.WorkerServices;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers what only <c>WorkerHost</c> runs: the database-backed gauges of <see cref="BillingStateMetrics"/>,
        /// published by one host so scaled web/gRPC replicas do not each report the same series, and the options of
        /// <see cref="ExpireStalePaymentsJob"/>, which the host registers as a background job.
        /// </summary>
        public IServiceCollection AddWorkerServices(IConfiguration configuration)
        {
            services.AddBillingMetrics();

            services
                .AddOptions<PaymentExpirationOptions>()
                .Bind(configuration.GetSection(PaymentExpirationOptions.SectionName));

            services.AddSingleton<BillingStateMetrics>();
            services.AddHostedService(sp => sp.GetRequiredService<BillingStateMetrics>());

            return services;
        }
    }
}
