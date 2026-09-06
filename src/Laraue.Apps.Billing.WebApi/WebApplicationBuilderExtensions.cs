using System.Text.Json.Serialization;
using Laraue.Apps.Billing.DataAccess;
using Laraue.Apps.Billing.WebApiServices;
using Laraue.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Billing.WebApi;

public static class WebApplicationBuilderExtensions
{
    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder AddDatabaseServices(string connectionStringName)
        {
            var connection = builder.Configuration.GetConnectionString(connectionStringName);

            builder.Services.AddDbContext<DatabaseContext>(opt =>
            {
                opt
                    .UseNpgsql(connection)
                    .UseSnakeCaseNamingConvention();
            });

            return builder;
        }

        public WebApplicationBuilder AddApplicationServices()
        {
            builder.Services.AddScoped<ExceptionHandleMiddleware>();
            builder.Services.AddScoped<ITariffService, TariffService>();

            builder.Services
                .AddControllers()
                .AddJsonOptions(options =>
                    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
            builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
                options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddOpenApi();

            return builder;
        }
    }
}
