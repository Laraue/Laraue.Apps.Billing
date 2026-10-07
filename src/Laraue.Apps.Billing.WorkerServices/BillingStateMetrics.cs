using System.Diagnostics.Metrics;
using Laraue.Apps.Billing.DataAccess;
using Laraue.Apps.Billing.DataAccess.Entities;
using Laraue.Apps.Billing.Services.Metrics;
using Laraue.Core.DateTime.Services.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Laraue.Apps.Billing.WorkerServices;

/// <summary>
/// Gauges of Billing's state, read from the database: they answer "what is true now" and survive a restart,
/// which event counters cannot. Registered by one host only (WorkerHost), so scaled replicas of the web and gRPC
/// hosts do not each publish the same series. The numbers are refreshed in the background, a scrape only reads
/// the last snapshot.
/// </summary>
public sealed class BillingStateMetrics : BackgroundService
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<BillingStateMetrics> _logger;

    private volatile Snapshot _snapshot = Snapshot.Empty;

    public BillingStateMetrics(
        IMeterFactory meterFactory,
        IServiceScopeFactory scopeFactory,
        IDateTimeProvider dateTimeProvider,
        ILogger<BillingStateMetrics> logger)
    {
        _scopeFactory = scopeFactory;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;

        var meter = meterFactory.Create(BillingMetrics.MeterName);

        meter.CreateObservableGauge(
            "billing.subscriptions.active",
            () => _snapshot.ActiveSubscriptions.Select(x => new Measurement<long>(
                x.Count,
                new KeyValuePair<string, object?>("service", x.Service.ToString()),
                new KeyValuePair<string, object?>("tariff", x.Tariff))),
            description: "Active subscriptions by service and tariff.");

        meter.CreateObservableGauge(
            "billing.payments.pending",
            () => _snapshot.PendingPayments,
            description: "Payments waiting for the provider's confirmation.");

        meter.CreateObservableGauge(
            "billing.payments.pending.oldest_age",
            () => _snapshot.OldestPendingPaymentAgeSeconds,
            unit: "s",
            description: "Age of the oldest pending payment, zero when there is none. For a stuck-payment alert.");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RefreshInterval);

        do
        {
            try
            {
                await RefreshAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The gauges keep their last values; the next tick tries again.
                _logger.LogWarning(ex, "Refreshing the billing state metrics failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var subscriptions = await context.Subscriptions
            .Where(x => x.Status == SubscriptionStatus.Active)
            .GroupBy(x => new { x.ServiceId, x.Tariff!.Title })
            .Select(x => new ActiveSubscriptions(x.Key.ServiceId, x.Key.Title, x.Count()))
            .ToListAsync(cancellationToken);

        var pending = await context.Payments
            .Where(x => x.Status == PaymentStatus.Pending)
            .GroupBy(_ => 1)
            .Select(x => new { Count = x.Count(), Oldest = x.Min(p => p.CreatedAt) })
            .SingleOrDefaultAsync(cancellationToken);

        _snapshot = new Snapshot(
            subscriptions,
            pending?.Count ?? 0,
            pending is null ? 0 : Math.Max(0, (_dateTimeProvider.UtcNow - pending.Oldest).TotalSeconds));
    }

    private sealed record ActiveSubscriptions(ServiceId Service, string Tariff, long Count);

    private sealed record Snapshot(
        IReadOnlyList<ActiveSubscriptions> ActiveSubscriptions,
        long PendingPayments,
        double OldestPendingPaymentAgeSeconds)
    {
        public static readonly Snapshot Empty = new([], 0, 0);
    }
}
