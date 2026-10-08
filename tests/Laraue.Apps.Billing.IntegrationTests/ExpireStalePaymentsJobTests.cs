using System.Net;
using System.Security.Cryptography;
using System.Text;
using Laraue.Apps.Billing.DataAccess.Entities;
using Laraue.Apps.Billing.IntegrationTests.Infrastructure;
using Laraue.Apps.Billing.WorkerServices;
using Laraue.Core.DateTime.Services.Impl;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Laraue.Apps.Billing.IntegrationTests;

public class ExpireStalePaymentsJobTests : BillingIntegrationTest
{
    private static readonly Guid PersonalPlusTariffId = new("e8e4b409-366d-4803-b116-76c1a4a6c8f1");

    private readonly WebApiTestHost _host;
    private readonly ExpireStalePaymentsJob _job;

    public ExpireStalePaymentsJobTests(WebApiTestHost host) : base(host)
    {
        _host = host;
        _job = new ExpireStalePaymentsJob(
            Context,
            Options.Create(new PaymentExpirationOptions { PendingExpiration = TimeSpan.FromHours(1) }),
            new DateTimeProvider(),
            Metrics,
            NullLogger<ExpireStalePaymentsJob>.Instance);
    }

    [Fact]
    public async Task ExpireStalePaymentsAsync_ShouldExpireOnlyOldPendingPayments_WhenSomeAreFreshOrFinished()
    {
        var old = await AddPaymentAsync(PaymentStatus.Pending, age: TimeSpan.FromHours(2));
        var fresh = await AddPaymentAsync(PaymentStatus.Pending, age: TimeSpan.FromMinutes(10));
        var paid = await AddPaymentAsync(PaymentStatus.Paid, age: TimeSpan.FromHours(5));
        var failed = await AddPaymentAsync(PaymentStatus.Failed, age: TimeSpan.FromHours(5));

        var expired = await _job.ExpireStalePaymentsAsync(CancellationToken.None);

        Assert.Equal(1, expired);
        Assert.Equal(PaymentStatus.Expired, await GetStatusAsync(old));
        Assert.Equal(PaymentStatus.Pending, await GetStatusAsync(fresh));
        Assert.Equal(PaymentStatus.Paid, await GetStatusAsync(paid));
        Assert.Equal(PaymentStatus.Failed, await GetStatusAsync(failed));
    }

    [Fact]
    public async Task ExpireStalePaymentsAsync_ShouldDoNothing_WhenCalledTwice()
    {
        await AddPaymentAsync(PaymentStatus.Pending, age: TimeSpan.FromHours(2));

        Assert.Equal(1, await _job.ExpireStalePaymentsAsync(CancellationToken.None));
        Assert.Equal(0, await _job.ExpireStalePaymentsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Notify_ShouldStillFulfilPayment_WhenPaymentWasExpiredBeforeCustomerPaid()
    {
        var payment = await AddPaymentAsync(PaymentStatus.Pending, age: TimeSpan.FromHours(2));
        await _job.ExpireStalePaymentsAsync(CancellationToken.None);
        Assert.Equal(PaymentStatus.Expired, await GetStatusAsync(payment));

        var response = await _host.CreateClient().GetAsync(BuildNotifyUrl(payment, outSum: "334.000000", invId: "7001"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(PaymentStatus.Paid, await GetStatusAsync(payment));
        Assert.True(await Context.Subscriptions.AsNoTracking()
            .AnyAsync(x => x.PaidEntityId == payment.PaidEntityId && x.Status == SubscriptionStatus.Active));
    }

    [Fact]
    public async Task Metrics_ShouldExposeExpiredPayments_WhenPaymentExpires()
    {
        await AddPaymentAsync(PaymentStatus.Pending, age: TimeSpan.FromHours(2));

        await _job.ExpireStalePaymentsAsync(CancellationToken.None);

        var metrics = await _host.CreateClient().GetStringAsync("/_metrics");
        Assert.Contains(
            metrics.Split('\n').Where(x => x.StartsWith("billing_payments_completed_total{")),
            line => line.Contains("provider=\"robokassa\"") && line.Contains("status=\"Expired\""));
    }

    private async Task<Payment> AddPaymentAsync(PaymentStatus status, TimeSpan age)
    {
        var userId = Guid.NewGuid();
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            ServiceId = ServiceId.LaraueBoards,
            Kind = PaymentKind.Subscription,
            PaidEntityId = userId,
            OwnerId = userId,
            TariffId = PersonalPlusTariffId,
            // 4 USD at the seeded RUB rate, rounded up to a whole ruble.
            AmountMinorUnits = 33_400,
            CurrencyCode = "RUB",
            Status = status,
            Provider = "robokassa",
            CreatedAt = DateTime.UtcNow - age,
        };

        Context.Payments.Add(payment);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        return payment;
    }

    private async Task<PaymentStatus> GetStatusAsync(Payment payment)
        => await Context.Payments.AsNoTracking().Where(x => x.Id == payment.Id).Select(x => x.Status).SingleAsync();

    private static string BuildNotifyUrl(Payment payment, string outSum, string invId)
    {
        var signature = Convert.ToHexStringLower(
            MD5.HashData(Encoding.UTF8.GetBytes($"{outSum}:{invId}:pass2:Shp_paymentId={payment.Id}")));

        return $"/api/payments/robokassa/notify?OutSum={outSum}&InvId={invId}"
            + $"&Shp_paymentId={payment.Id}&SignatureValue={signature}";
    }
}
