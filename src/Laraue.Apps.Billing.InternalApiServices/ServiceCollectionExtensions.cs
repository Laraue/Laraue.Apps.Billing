using Laraue.Apps.Billing.Payments.Robokassa;
using Laraue.Apps.Billing.Services;
using Laraue.Apps.Billing.Services.Payments;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Core.DateTime.Services.Impl;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Billing.InternalApiServices;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddInternalApiServices(IConfiguration configuration)
        {
            services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
            services.AddScoped<ISubscriptionService, SubscriptionService>();
            services.AddScoped<ITokenService, TokenService>();

            services.AddPaymentServices(configuration);

            // The payment provider in use is a choice of this line and Payments:DefaultProvider.
            services.AddRobokassaPaymentProvider(configuration);

            return services;
        }
    }
}
