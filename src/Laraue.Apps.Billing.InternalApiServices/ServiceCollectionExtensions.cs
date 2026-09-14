using Laraue.Apps.Billing.Services;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Core.DateTime.Services.Impl;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Billing.InternalApiServices;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddInternalApiServices()
        {
            services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
            services.AddScoped<ISubscriptionService, SubscriptionService>();
            services.AddScoped<ITokenService, TokenService>();

            return services;
        }
    }
}
