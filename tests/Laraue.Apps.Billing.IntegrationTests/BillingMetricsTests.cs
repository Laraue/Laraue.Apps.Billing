using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using Laraue.Apps.Billing.DataAccess.Data;
using Laraue.Apps.Billing.DataAccess.Entities;
using Laraue.Apps.Billing.IntegrationTests.Infrastructure;
using Laraue.Apps.Billing.Services;
using Laraue.Apps.Billing.Services.Metrics;
using Laraue.Apps.Billing.Services.Payments;
using Laraue.Apps.Billing.WorkerServices;
using Laraue.Core.DateTime.Services.Impl;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Laraue.Apps.Billing.IntegrationTests;

/// <summary>
/// Scrapes the web host's Prometheus endpoint after real operations. Meters are process-wide, so another
/// test can add to the same series - assert that a series exists, not its exact count.
/// </summary>
public class BillingMetricsTests : BillingIntegrationTest
{
    private static readonly Guid PersonalPlusTariffId = new("e8e4b409-366d-4803-b116-76c1a4a6c8f1");

    private readonly WebApiTestHost _host;
    private readonly ITokenService _tokenService;

    public BillingMetricsTests(WebApiTestHost host) : base(host)
    {
        _host = host;
        var dateTimeProvider = new DateTimeProvider();
        _tokenService = new TokenService(Context, new SubscriptionService(Context, dateTimeProvider), dateTimeProvider, Metrics);
    }

    [Fact]
    public async Task Metrics_ShouldExposeTokenReservationsAndSpending_WhenTokensAreReservedCommittedAndCancelled()
    {
        var userId = Guid.NewGuid();

        var confirmed = await InTransaction(() => _tokenService.TryReservePersonalTokensAsync(
            ServiceId.LaraueBoards, userId, inputTokensCount: 10, maxOutputTokensCount: 10, CancellationToken.None));
        await InTransaction(() => _tokenService.CommitTokensSpentAsync(
            confirmed.TokenTransactionId!.Value, actualOutputTokensCount: 5, CancellationToken.None));

        var cancelled = await InTransaction(() => _tokenService.TryReservePersonalTokensAsync(
            ServiceId.LaraueBoards, userId, inputTokensCount: 10, maxOutputTokensCount: 10, CancellationToken.None));
        await InTransaction(() => _tokenService.CancelTokensReservationAsync(
            cancelled.TokenTransactionId!.Value, "failed", CancellationToken.None));

        await InTransaction(() => _tokenService.TryReservePersonalTokensAsync(
            ServiceId.LaraueBoards, userId, inputTokensCount: int.MaxValue, maxOutputTokensCount: 0, CancellationToken.None));

        var metrics = await ScrapeAsync();

        Assert.Contains(SeriesLines(metrics, "billing_tokens_spent_total"), line =>
            line.Contains("service=\"LaraueBoards\""));
        foreach (var result in new[] { "started", "confirmed", "cancelled", "insufficient_balance" })
        {
            Assert.Contains(SeriesLines(metrics, "billing_token_reservations_total"), line =>
                line.Contains("service=\"LaraueBoards\"") && line.Contains($"result=\"{result}\""));
        }
    }

    [Fact]
    public async Task Metrics_ShouldExposeCreatedPayments_WhenCheckoutIsCreated()
    {
        await CreatePaymentAsync();

        var metrics = await ScrapeAsync();

        Assert.Contains(SeriesLines(metrics, "billing_payments_created_total"), line =>
            line.Contains("provider=\"robokassa\"") && line.Contains("kind=\"Subscription\"") && line.Contains("currency=\"RUB\""));
    }

    [Fact]
    public async Task Metrics_ShouldExposePaidPaymentAndAcceptedNotification_WhenNotificationIsValid()
    {
        var payment = await CreatePaymentAsync();

        var response = await _host.CreateClient().GetAsync(BuildNotifyUrl(payment, "334.000000", "6001"));
        response.EnsureSuccessStatusCode();

        var metrics = await ScrapeAsync();

        Assert.Contains(SeriesLines(metrics, "billing_payments_completed_total"), line =>
            line.Contains("provider=\"robokassa\"") && line.Contains("status=\"Paid\""));
        Assert.Contains(SeriesLines(metrics, "billing_payments_amount_total"), line =>
            line.Contains("provider=\"robokassa\"") && line.Contains("currency=\"RUB\""));
        Assert.Contains(SeriesLines(metrics, "billing_payment_notifications_total"), line =>
            line.Contains("provider=\"robokassa\"") && line.Contains("result=\"accepted\""));
        Assert.NotEmpty(SeriesLines(metrics, "billing_payment_notification_duration_seconds_count"));
    }

    [Fact]
    public async Task Metrics_ShouldExposeRejectedNotification_WhenSignatureIsInvalid()
    {
        var payment = await CreatePaymentAsync();

        await _host.CreateClient().GetAsync(BuildNotifyUrl(payment, "334.000000", "6002", signature: "bad"));

        var metrics = await ScrapeAsync();

        Assert.Contains(SeriesLines(metrics, "billing_payment_notifications_total"), line =>
            line.Contains("provider=\"robokassa\"") && line.Contains("result=\"invalid_signature\""));
    }

    [Fact]
    public async Task StateMetrics_ShouldReportPendingPaymentsAndActiveSubscriptions_WhenRefreshed()
    {
        await CreatePaymentAsync();
        await InTransaction(() => _tokenService.TryReservePersonalTokensAsync(
            ServiceId.LaraueBoards, Guid.NewGuid(), inputTokensCount: 1, maxOutputTokensCount: 1, CancellationToken.None));

        using var meters = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var stateMetrics = new BillingStateMetrics(
            meters.GetRequiredService<IMeterFactory>(),
            _host.Services.GetRequiredService<IServiceScopeFactory>(),
            new DateTimeProvider(),
            NullLogger<BillingStateMetrics>.Instance);
        await stateMetrics.RefreshAsync(CancellationToken.None);

        var measurements = new Dictionary<string, double>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == BillingMetrics.MeterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) => measurements[instrument.Name] = value);
        listener.SetMeasurementEventCallback<double>((instrument, value, _, _) => measurements[instrument.Name] = value);
        listener.Start();
        listener.RecordObservableInstruments();

        Assert.True(measurements["billing.payments.pending"] >= 1);
        Assert.True(measurements["billing.payments.pending.oldest_age"] >= 0);
        Assert.True(measurements["billing.subscriptions.active"] >= 1);
    }

    [Fact]
    public async Task StateMetrics_ShouldKeepPersonalAndTeamTariffsApart_WhenBothAreTitledFree()
    {
        // Laraue Boards has a Personal and a Team tariff, both titled "Free".
        foreach (var tariff in new[]
                 {
                     LaraueBoardsTariffsData.PersonalTariffs.Select(x => x.Tariff).Single(x => x.IsFree),
                     LaraueBoardsTariffsData.TeamTariffs.Select(x => x.Tariff).Single(x => x.IsFree),
                 })
        {
            var paidEntityId = Guid.NewGuid();
            Context.Subscriptions.Add(new Subscription
            {
                Id = Guid.NewGuid(),
                ServiceId = ServiceId.LaraueBoards,
                TariffId = tariff.Id,
                OwnerId = paidEntityId,
                PaidEntityId = paidEntityId,
                Status = SubscriptionStatus.Active,
                CurrentPeriodStartedAt = DateTime.UtcNow,
            });
        }

        await Context.SaveChangesAsync();

        using var meters = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var stateMetrics = new BillingStateMetrics(
            meters.GetRequiredService<IMeterFactory>(),
            _host.Services.GetRequiredService<IServiceScopeFactory>(),
            new DateTimeProvider(),
            NullLogger<BillingStateMetrics>.Instance);
        await stateMetrics.RefreshAsync(CancellationToken.None);

        var series = new List<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == BillingMetrics.MeterName && instrument.Name == "billing.subscriptions.active")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            series.Add(string.Join(",", tags.ToArray().OrderBy(x => x.Key).Select(x => $"{x.Key}={x.Value}")) + $":{value}"));
        listener.Start();
        listener.RecordObservableInstruments();

        Assert.Contains("service=LaraueBoards,tariff=Free,type=Personal:1", series);
        Assert.Contains("service=LaraueBoards,tariff=Free,type=Team:1", series);
    }

    [Fact]
    public async Task StateMetrics_ShouldCountRecentPaymentsByWindowAndStatus_WhenRefreshed()
    {
        await SeedPaymentAsync(PaymentStatus.Paid, TimeSpan.FromHours(2));
        await SeedPaymentAsync(PaymentStatus.Expired, TimeSpan.FromDays(3));
        await SeedPaymentAsync(PaymentStatus.Failed, TimeSpan.FromDays(10));
        await SeedPaymentAsync(PaymentStatus.Paid, TimeSpan.FromDays(40));
        await SeedPaymentAsync(PaymentStatus.Pending, TimeSpan.FromMinutes(10));

        using var meters = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var stateMetrics = new BillingStateMetrics(
            meters.GetRequiredService<IMeterFactory>(),
            _host.Services.GetRequiredService<IServiceScopeFactory>(),
            new DateTimeProvider(),
            NullLogger<BillingStateMetrics>.Instance);
        await stateMetrics.RefreshAsync(CancellationToken.None);

        var series = new Dictionary<string, long>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == BillingMetrics.MeterName && instrument.Name == "billing.payments.recent")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var window = tags.ToArray().Single(x => x.Key == "window").Value;
            var status = tags.ToArray().Single(x => x.Key == "status").Value;
            series[$"{window}/{status}"] = value;
        });
        listener.Start();
        listener.RecordObservableInstruments();

        // Every status is present in every window, even when nothing is in it.
        Assert.Equal(3 * Enum.GetValues<PaymentStatus>().Length, series.Count);

        Assert.Equal(1, series["1d/Paid"]);
        Assert.Equal(1, series["7d/Paid"]);
        Assert.Equal(1, series["30d/Paid"]);
        Assert.Equal(0, series["1d/Expired"]);
        Assert.Equal(1, series["7d/Expired"]);
        Assert.Equal(0, series["7d/Failed"]);
        Assert.Equal(1, series["30d/Failed"]);
        Assert.Equal(1, series["1d/Pending"]);
        Assert.Equal(0, series["1d/Canceled"]);
    }

    private async Task SeedPaymentAsync(PaymentStatus status, TimeSpan age)
    {
        var userId = Guid.NewGuid();
        Context.Payments.Add(new Payment
        {
            Id = Guid.NewGuid(),
            ServiceId = ServiceId.LaraueBoards,
            Kind = PaymentKind.Subscription,
            PaidEntityId = userId,
            OwnerId = userId,
            TariffId = PersonalPlusTariffId,
            AmountMinorUnits = 33_400,
            CurrencyCode = "RUB",
            Status = status,
            Provider = "robokassa",
            CreatedAt = DateTime.UtcNow - age,
        });

        await Context.SaveChangesAsync();
    }

    private async Task<Payment> CreatePaymentAsync()
    {
        var userId = Guid.NewGuid();
        var checkout = await _host.Services.CreateScope().ServiceProvider.GetRequiredService<ICorePaymentService>()
            .CreateAsync(
                new CreatePaymentRequest
                {
                    ServiceId = ServiceId.LaraueBoards,
                    Kind = PaymentKind.Subscription,
                    ItemId = PersonalPlusTariffId,
                    PaidEntityId = userId,
                    IsOrganization = false,
                    OwnerId = userId,
                    CurrencyCode = "RUB",
                },
                CancellationToken.None);

        return Context.Payments.Single(x => x.Id == checkout.PaymentId);
    }

    private async Task<string> ScrapeAsync() => await _host.CreateClient().GetStringAsync("/_metrics");

    private static IEnumerable<string> SeriesLines(string metrics, string series)
        => metrics.Split('\n').Where(line => line.StartsWith(series + "{"));

    private static string BuildNotifyUrl(Payment payment, string outSum, string invId, string? signature = null)
    {
        signature ??= Convert.ToHexStringLower(
            MD5.HashData(Encoding.UTF8.GetBytes($"{outSum}:{invId}:pass2:Shp_paymentId={payment.Id}")));

        return $"/api/payments/robokassa/notify?OutSum={outSum}&InvId={invId}"
            + $"&Shp_paymentId={payment.Id}&SignatureValue={signature}";
    }
}
