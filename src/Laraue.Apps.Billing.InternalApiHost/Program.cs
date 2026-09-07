using Laraue.Apps.Billing.DataAccess;
using Laraue.Apps.Billing.InternalApiServices;
using Laraue.Apps.Billing.Services;
using Laraue.Grpc.OpenTelemetry;
using Laraue.Grpc.Server;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;

namespace Laraue.Apps.Billing.InternalApiHost;

public sealed class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        const string dbConnectionStringName = "Postgre";

        // Internal service-to-service traffic only (trusted network) - plain HTTP/2 (h2c) for gRPC,
        // no TLS. Http1AndHttp2 (not Http2 alone) so /_health and /_metrics still work - those are
        // scraped over plain HTTP/1.1, and Kestrel can serve both off the same cleartext port.
        builder.WebHost.ConfigureKestrel(options =>
            options.ConfigureEndpointDefaults(listen => listen.Protocols = HttpProtocols.Http1AndHttp2));

        var connection = builder.Configuration.GetConnectionString(dbConnectionStringName);
        builder.Services.AddDbContext<DatabaseContext>(opt => opt
            .UseNpgsql(connection)
            .UseSnakeCaseNamingConvention());

        builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();

        builder.Services
            .AddGrpc()
            .AddLaraueGrpcTelemetry();

        builder.Services.AddLaraueGrpcTelemetry(
            configureMetrics: metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation()
                .AddPrometheusExporter());

        builder.Services.AddHealthChecks();

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            await using var db = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            await db.Database.MigrateAsync();
        }

        app.MapGrpcService<SubscriptionGrpcService>();
        app.MapHealthChecks("/_health");
        app.MapPrometheusScrapingEndpoint("/_metrics");

        await app.RunAsync();
    }
}
