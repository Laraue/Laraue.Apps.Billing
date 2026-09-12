using Laraue.Apps.Billing.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Billing.InternalApiServices;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddInternalApiServices()
        {
            services.AddScoped<ISubscriptionService, SubscriptionService>();
            services.AddScoped<ITokenService, TokenService>();

            return services;
        }
    }
}
