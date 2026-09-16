using Laraue.Apps.Billing.DataAccess;
using Laraue.Apps.Billing.InternalApiServices;
using Laraue.Apps.Billing.Services.Jobs;
using Laraue.Core.Extensions.Hosting;
using Laraue.Core.Extensions.Hosting.EfCore;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Billing.WorkerHost;

/// <summary>
/// The one host for Billing's non-request-driven background work - today just
/// <see cref="CancelStaleTokenReservationsJob"/>, but meant to be where anything else in this
/// category lands too (a future RabbitMQ consumer, say), rather than being scoped to
/// <c>Laraue.Core.Extensions.Hosting</c> jobs specifically. Kept as its own deployable, same as
/// <c>InternalApiHost</c>/<c>WebApiHost</c>, so a job never runs concurrently with itself across
/// horizontally-scaled instances - <see cref="IJobConcurrencyChecker"/>'s locking is in-memory/
/// per-process only, not distributed.
/// </summary>
public sealed class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        var connection = builder.Configuration.GetConnectionString("Postgre");
        builder.Services.AddDbContext<DatabaseContext>(opt => opt
            .UseNpgsql(connection)
            .UseSnakeCaseNamingConvention());

        // AddDbContext only registers the concrete DatabaseContext - DbJobRunnerRepository (from
        // Laraue.Core.Extensions.Hosting.EfCore) depends on IJobsDbContext, so it needs its own
        // registration resolving to the same scoped instance.
        builder.Services.AddScoped<IJobsDbContext>(sp => sp.GetRequiredService<DatabaseContext>());

        // Reused as-is rather than duplicated - it's plain DI composition (ISubscriptionService,
        // ITokenService, IDateTimeProvider), nothing gRPC/API-specific despite the project name.
        builder.Services.AddInternalApiServices();

        builder.Services.AddBackgroundJob<CancelStaleTokenReservationsJob, EmptyJobData>(
            "CancelStaleTokenReservationsJob");

        builder.Services.AddHealthChecks();

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            await using var db = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            await db.Database.MigrateAsync();
        }

        app.MapHealthChecks("/_health");

        await app.RunAsync();
    }
}
