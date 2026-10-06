using System.Net;
using System.Security.Cryptography;
using System.Text;
using Laraue.Apps.Billing.DataAccess.Entities;
using Laraue.Apps.Billing.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Billing.IntegrationTests;

public class PaymentsControllerTests : BillingIntegrationTest
{
    private static readonly Guid PersonalPlusTariffId = new("e8e4b409-366d-4803-b116-76c1a4a6c8f1");

    private readonly HttpClient _client;

    public PaymentsControllerTests(WebApiTestHost host) : base(host)
    {
        _client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task Notify_ShouldPayAndFulfilPayment_WhenSignatureIsValid()
    {
        var payment = await CreatePendingPaymentAsync();

        var response = await _client.GetAsync(BuildNotifyUrl(payment, outSum: "334.000000", invId: "5001"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("OK5001", await response.Content.ReadAsStringAsync());

        var paid = await Context.Payments.AsNoTracking().SingleAsync(x => x.Id == payment.Id);
        Assert.Equal(PaymentStatus.Paid, paid.Status);
        Assert.Equal("5001", paid.ProviderPaymentId);
        Assert.NotNull(paid.PaidAt);

        var subscription = await Context.Subscriptions.AsNoTracking()
            .SingleAsync(x => x.PaidEntityId == payment.PaidEntityId);
        Assert.Equal(PersonalPlusTariffId, subscription.TariffId);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.NotNull(subscription.CurrentPeriodFinishesAt);

        var balance = await Context.BalanceSubscriptionTokens.AsNoTracking()
            .SingleAsync(x => x.SubscriptionId == subscription.Id);
        Assert.Equal(300_000, balance.SubscriptionTokensCount);

        var ledgerRow = await Context.TokenTransactions.AsNoTracking()
            .SingleAsync(x => x.PaidEntityId == payment.PaidEntityId);
        Assert.Equal(TokenTransactionReason.TariffGrant, ledgerRow.Reason);
        Assert.Equal(300_000, ledgerRow.Delta);
    }

    [Fact]
    public async Task Notify_ShouldPayAndFulfilPayment_WhenNotificationIsPostedAsAForm()
    {
        var payment = await CreatePendingPaymentAsync();
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["OutSum"] = "334.000000",
            ["InvId"] = "5002",
            ["Shp_paymentId"] = payment.Id.ToString(),
            ["SignatureValue"] = Md5($"334.000000:5002:pass2:Shp_paymentId={payment.Id}"),
        });

        var response = await _client.PostAsync("/api/payments/robokassa/notify", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("OK5002", await response.Content.ReadAsStringAsync());

        var paid = await Context.Payments.AsNoTracking().SingleAsync(x => x.Id == payment.Id);
        Assert.Equal(PaymentStatus.Paid, paid.Status);
        Assert.Equal("5002", paid.ProviderPaymentId);
    }

    [Fact]
    public async Task Notify_ShouldNotFulfilPayment_WhenPostedSignatureIsInvalid()
    {
        var payment = await CreatePendingPaymentAsync();
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["OutSum"] = "334.000000",
            ["InvId"] = "5002",
            ["Shp_paymentId"] = payment.Id.ToString(),
            ["SignatureValue"] = "deadbeef",
        });

        var response = await _client.PostAsync("/api/payments/robokassa/notify", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNothingGrantedAsync(payment);
    }

    [Fact]
    public async Task Notify_ShouldNotFulfilTwice_WhenNotificationIsRepeated()
    {
        var payment = await CreatePendingPaymentAsync();
        var url = BuildNotifyUrl(payment, outSum: "334.000000", invId: "5001");

        var first = await _client.GetAsync(url);
        var second = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("OK5001", await second.Content.ReadAsStringAsync());

        var subscription = await Context.Subscriptions.AsNoTracking()
            .SingleAsync(x => x.PaidEntityId == payment.PaidEntityId);
        var balance = await Context.BalanceSubscriptionTokens.AsNoTracking()
            .SingleAsync(x => x.SubscriptionId == subscription.Id);
        Assert.Equal(300_000, balance.SubscriptionTokensCount);
        Assert.Single(await Context.TokenTransactions.AsNoTracking()
            .Where(x => x.PaidEntityId == payment.PaidEntityId)
            .ToListAsync());
    }

    [Fact]
    public async Task Notify_ShouldReturnBadRequestAndKeepPaymentPending_WhenSignatureIsWrong()
    {
        var payment = await CreatePendingPaymentAsync();
        var url = BuildNotifyUrl(payment, outSum: "334.000000", invId: "5001", signature: "deadbeef");

        var response = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNothingGrantedAsync(payment);
    }

    [Fact]
    public async Task Notify_ShouldReturnBadRequestAndKeepPaymentPending_WhenPaidAmountDiffersFromPayment()
    {
        var payment = await CreatePendingPaymentAsync();

        // Correctly signed, but for less money than the payment is for.
        var response = await _client.GetAsync(BuildNotifyUrl(payment, outSum: "1.000000", invId: "5001"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNothingGrantedAsync(payment);
    }

    [Fact]
    public async Task Notify_ShouldReturnNotFound_WhenPaymentDoesNotExist()
    {
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            Provider = "robokassa",
            CurrencyCode = "RUB",
        };

        var response = await _client.GetAsync(BuildNotifyUrl(payment, outSum: "334.000000", invId: "5001"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Notify_ShouldReturnBadRequest_WhenProviderIsUnknown()
    {
        var response = await _client.GetAsync("/api/payments/unknown/notify?OutSum=1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("success", "http://localhost:3000/payment/success")]
    [InlineData("fail", "http://localhost:3000/payment/fail")]
    public async Task ReturnPages_ShouldRedirectToConfiguredUrl_Always(string page, string expectedUrl)
    {
        var response = await _client.GetAsync($"/api/payments/robokassa/{page}?InvId=1");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(expectedUrl, response.Headers.Location!.ToString());
    }

    [Theory]
    [InlineData(ServiceId.LaraueBoards, "success", "http://localhost:3000/payment/success")]
    [InlineData(ServiceId.LaraueBoards, "fail", "http://localhost:3000/payment/fail")]
    [InlineData(ServiceId.MarkdownTranslator, "success", "https://translator.example/payment/success")]
    [InlineData(ServiceId.MarkdownTranslator, "fail", "https://translator.example/payment/fail")]
    public async Task ReturnPages_ShouldRedirectToTheServiceOfThePayment_WhenPaymentIsKnown(
        ServiceId serviceId,
        string page,
        string expectedUrl)
    {
        var payment = await CreatePendingPaymentAsync();
        payment.ServiceId = serviceId;
        await Context.SaveChangesAsync();

        var response = await _client.GetAsync($"/api/payments/robokassa/{page}?InvId=1&Shp_paymentId={payment.Id}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(expectedUrl, response.Headers.Location!.ToString());
    }

    [Theory]
    [InlineData("success", "http://localhost:3000/payment/success")]
    [InlineData("fail", "http://localhost:3000/payment/fail")]
    public async Task ReturnPages_ShouldRedirectToTheDefaultUrl_WhenPaymentCannotBeIdentified(
        string page,
        string expectedUrl)
    {
        var unknown = await _client.GetAsync($"/api/payments/robokassa/{page}?InvId=1&Shp_paymentId={Guid.NewGuid()}");
        var invalid = await _client.GetAsync($"/api/payments/robokassa/{page}?InvId=1&Shp_paymentId=not-a-guid");

        Assert.Equal(expectedUrl, unknown.Headers.Location!.ToString());
        Assert.Equal(expectedUrl, invalid.Headers.Location!.ToString());
    }

    [Theory]
    [InlineData(ServiceId.LaraueBoards, "success", "http://localhost:3000/payment/success")]
    [InlineData(ServiceId.MarkdownTranslator, "success", "https://translator.example/payment/success")]
    [InlineData(ServiceId.LaraueBoards, "fail", "http://localhost:3000/payment/fail")]
    [InlineData(ServiceId.MarkdownTranslator, "fail", "https://translator.example/payment/fail")]
    public async Task ReturnPages_ShouldRedirectToTheServiceOfThePayment_WhenParametersArePostedAsAForm(
        ServiceId serviceId,
        string page,
        string expectedUrl)
    {
        var payment = await CreatePendingPaymentAsync();
        payment.ServiceId = serviceId;
        await Context.SaveChangesAsync();
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["InvId"] = "1",
            ["Shp_paymentId"] = payment.Id.ToString(),
        });

        var response = await _client.PostAsync($"/api/payments/robokassa/{page}", form);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(expectedUrl, response.Headers.Location!.ToString());
    }

    private async Task<Payment> CreatePendingPaymentAsync()
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
            Status = PaymentStatus.Pending,
            Provider = "robokassa",
            CreatedAt = DateTime.UtcNow,
        };

        Context.Payments.Add(payment);
        await Context.SaveChangesAsync();

        return payment;
    }

    private async Task AssertNothingGrantedAsync(Payment payment)
    {
        var current = await Context.Payments.AsNoTracking().SingleAsync(x => x.Id == payment.Id);
        Assert.Equal(PaymentStatus.Pending, current.Status);
        Assert.Null(current.PaidAt);
        Assert.Empty(await Context.Subscriptions.AsNoTracking()
            .Where(x => x.PaidEntityId == payment.PaidEntityId)
            .ToListAsync());
    }

    /// <summary>
    /// The way Robokassa calls the result address: <c>OutSum</c> with six decimals, its own
    /// <c>InvId</c>, our custom parameter and a signature made with Password #2.
    /// </summary>
    private static string BuildNotifyUrl(Payment payment, string outSum, string invId, string? signature = null)
    {
        signature ??= Md5($"{outSum}:{invId}:pass2:Shp_paymentId={payment.Id}");

        return $"/api/payments/robokassa/notify?OutSum={outSum}&InvId={invId}"
            + $"&Shp_paymentId={payment.Id}&SignatureValue={signature}";
    }

    private static string Md5(string value)
    {
        return Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(value)));
    }
}
