using Laraue.Apps.Billing.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Billing.WebApiServices;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddWebApiServices()
        {
            services.AddScoped<ICoreTariffService, CoreTariffService>();
            services.AddScoped<ITariffService, TariffService>();

            return services;
        }
    }
}
