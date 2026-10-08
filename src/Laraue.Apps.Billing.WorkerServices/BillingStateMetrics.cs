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

    /// <summary>The windows of <c>billing.payments.recent</c>: how far back a payment's creation counts.</summary>
    private static readonly (string Label, TimeSpan Window)[] RecentPaymentWindows =
    [
        ("1d", TimeSpan.FromDays(1)),
        ("7d", TimeSpan.FromDays(7)),
        ("30d", TimeSpan.FromDays(30)),
    ];

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
                new KeyValuePair<string, object?>("tariff", x.Tariff),
                new KeyValuePair<string, object?>("type", x.Type.ToString()),
                new KeyValuePair<string, object?>("plan", x.IsFree ? "free" : "paid"))),
            description: "Active subscriptions by service, tariff title, tariff type (Personal or Team: a service can have both with the same title) and plan (free or paid).");

        meter.CreateObservableGauge(
            "billing.payments.pending",
            () => _snapshot.PendingPayments,
            description: "Payments waiting for the provider's confirmation.");

        meter.CreateObservableGauge(
            "billing.payments.recent",
            () => _snapshot.RecentPayments.Select(x => new Measurement<long>(
                x.Count,
                new KeyValuePair<string, object?>("window", x.Window),
                new KeyValuePair<string, object?>("status", x.Status.ToString()))),
            description: "Payments created within the last 1, 7 or 30 days, by their current status (every status is always present). Read from the database, so unlike the counters it does not depend on process restarts or on a series' first event.");

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
            .GroupBy(x => new { x.ServiceId, x.Tariff!.Title, x.Tariff.Type, x.Tariff.IsFree })
            .Select(x => new ActiveSubscriptions(x.Key.ServiceId, x.Key.Title, x.Key.Type, x.Key.IsFree, x.Count()))
            .ToListAsync(cancellationToken);

        var pending = await context.Payments
            .Where(x => x.Status == PaymentStatus.Pending)
            .GroupBy(_ => 1)
            .Select(x => new { Count = x.Count(), Oldest = x.Min(p => p.CreatedAt) })
            .SingleOrDefaultAsync(cancellationToken);

        var now = _dateTimeProvider.UtcNow;
        var recentPayments = new List<RecentPayments>();
        foreach (var (label, window) in RecentPaymentWindows)
        {
            var since = now - window;
            var byStatus = await context.Payments
                .Where(x => x.CreatedAt >= since)
                .GroupBy(x => x.Status)
                .Select(x => new { Status = x.Key, Count = x.LongCount() })
                .ToListAsync(cancellationToken);

            recentPayments.AddRange(Enum.GetValues<PaymentStatus>().Select(status => new RecentPayments(
                label,
                status,
                byStatus.SingleOrDefault(x => x.Status == status)?.Count ?? 0)));
        }

        _snapshot = new Snapshot(
            subscriptions,
            recentPayments,
            pending?.Count ?? 0,
            pending is null ? 0 : Math.Max(0, (_dateTimeProvider.UtcNow - pending.Oldest).TotalSeconds));
    }

    private sealed record ActiveSubscriptions(ServiceId Service, string Tariff, TariffType Type, bool IsFree, long Count);

    private sealed record RecentPayments(string Window, PaymentStatus Status, long Count);

    private sealed record Snapshot(
        IReadOnlyList<ActiveSubscriptions> ActiveSubscriptions,
        IReadOnlyList<RecentPayments> RecentPayments,
        long PendingPayments,
        double OldestPendingPaymentAgeSeconds)
    {
        public static readonly Snapshot Empty = new([], [], 0, 0);
    }
}
