using Laraue.Apps.Billing.DataAccess;
using Laraue.Apps.Billing.InternalApiServices;
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
        // no TLS. Kestrel cannot multiplex HTTP/1.1 and h2c on the same endpoint without TLS (there's
        // no ALPN to pick per-connection - a single endpoint set to Http1AndHttp2 silently falls back
        // to HTTP/1.1-only, which breaks every gRPC call with a client-side "HTTP_1_1_REQUIRED"
        // error - confirmed by actually running this host and calling it over a real socket;
        // WebApplicationFactory's in-memory TestServer bypasses Kestrel's real listen config
        // entirely, so this repo's own integration tests never exercised it). So gRPC and
        // health/metrics get their own ports instead: GrpcPort is HTTP/2-only, HealthPort is
        // HTTP/1.1-only.
        var grpcPort = builder.Configuration.GetValue<int?>("Kestrel:GrpcPort") ?? 5263;
        var healthPort = builder.Configuration.GetValue<int?>("Kestrel:HealthPort") ?? 5264;

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenAnyIP(grpcPort, listen => listen.Protocols = HttpProtocols.Http2);
            options.ListenAnyIP(healthPort, listen => listen.Protocols = HttpProtocols.Http1);
        });

        var connection = builder.Configuration.GetConnectionString(dbConnectionStringName);
        builder.Services.AddDbContext<DatabaseContext>(opt => opt
            .UseNpgsql(connection)
            .UseSnakeCaseNamingConvention());

        builder.Services.AddInternalApiServices();

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
