using Laraue.Apps.Billing.DataAccess.Entities;
using Laraue.Apps.Billing.IntegrationTests.Infrastructure;
using Laraue.Apps.Billing.Services.Payments;
using Laraue.Core.Exceptions.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Billing.IntegrationTests;

public class CorePaymentServiceTests : BillingIntegrationTest
{
    private static readonly Guid PersonalPlusTariffId = new("e8e4b409-366d-4803-b116-76c1a4a6c8f1");
    private static readonly Guid PersonalFreeTariffId = new("bd5f3457-601d-4ef1-92b2-47353f6b5a8f");
    private static readonly Guid TeamTariffId = new("89111f8d-292b-4f04-8766-b521e19e6964");
    private static readonly Guid SmallTokenPackId = new("76abd692-c450-4cd6-80e4-b9c012d91610");

    private readonly IServiceScope _scope;
    private readonly ICorePaymentService _paymentService;

    public CorePaymentServiceTests(WebApiTestHost host) : base(host)
    {
        _scope = host.Services.CreateScope();
        _paymentService = _scope.ServiceProvider.GetRequiredService<ICorePaymentService>();
    }

    [Fact]
    public async Task CreateAsync_ShouldStorePendingPaymentAndReturnProviderUrl_WhenBuyingPaidTariff()
    {
        var userId = Guid.NewGuid();

        var checkout = await _paymentService.CreateAsync(
            CreateRequest(PaymentKind.Subscription, PersonalPlusTariffId, userId),
            CancellationToken.None);

        // 4 USD at the seeded RUB rate (0.012), rounded up to a whole ruble.
        var payment = await Context.Payments.AsNoTracking().SingleAsync(x => x.Id == checkout.PaymentId);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal("robokassa", payment.Provider);
        Assert.Equal(33_400, payment.AmountMinorUnits);
        Assert.Equal("RUB", payment.CurrencyCode);
        Assert.Equal(PersonalPlusTariffId, payment.TariffId);
        Assert.Null(payment.TokenPackId);
        Assert.Equal(userId, payment.PaidEntityId);
        Assert.Equal(userId, payment.OwnerId);

        Assert.StartsWith("https://auth.robokassa.ru/Merchant/Index.aspx?", checkout.Url);
        Assert.Contains("OutSum=334.00", checkout.Url);
        Assert.Contains($"Shp_paymentId={checkout.PaymentId}", checkout.Url);
    }

    [Fact]
    public async Task CreateAsync_ShouldStorePendingPayment_WhenBuyingTokenPack()
    {
        var checkout = await _paymentService.CreateAsync(
            CreateRequest(PaymentKind.TokenPack, SmallTokenPackId, Guid.NewGuid()),
            CancellationToken.None);

        // 3 USD -> 250 RUB.
        var payment = await Context.Payments.AsNoTracking().SingleAsync(x => x.Id == checkout.PaymentId);
        Assert.Equal(PaymentKind.TokenPack, payment.Kind);
        Assert.Equal(SmallTokenPackId, payment.TokenPackId);
        Assert.Null(payment.TariffId);
        Assert.Equal(25_000, payment.AmountMinorUnits);
    }

    [Fact]
    public async Task CreateAsync_ShouldThrow_WhenTariffIsFree()
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _paymentService.CreateAsync(
            CreateRequest(PaymentKind.Subscription, PersonalFreeTariffId, Guid.NewGuid()),
            CancellationToken.None));

        Assert.Empty(await Context.Payments.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_ShouldThrow_WhenTeamTariffIsBoughtForUser()
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _paymentService.CreateAsync(
            CreateRequest(PaymentKind.Subscription, TeamTariffId, Guid.NewGuid(), isOrganization: false),
            CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_ShouldStorePayment_WhenTeamTariffIsBoughtForOrganization()
    {
        var checkout = await _paymentService.CreateAsync(
            CreateRequest(PaymentKind.Subscription, TeamTariffId, Guid.NewGuid(), isOrganization: true),
            CancellationToken.None);

        var payment = await Context.Payments.AsNoTracking().SingleAsync(x => x.Id == checkout.PaymentId);
        Assert.Equal(TeamTariffId, payment.TariffId);
        // 6 USD -> 500 RUB.
        Assert.Equal(50_000, payment.AmountMinorUnits);
    }

    [Fact]
    public async Task CreateAsync_ShouldThrow_WhenItemDoesNotExist()
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _paymentService.CreateAsync(
            CreateRequest(PaymentKind.Subscription, Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_ShouldThrow_WhenProviderDoesNotSupportCurrency()
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _paymentService.CreateAsync(
            CreateRequest(PaymentKind.Subscription, PersonalPlusTariffId, Guid.NewGuid(), currencyCode: "USD"),
            CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_ShouldThrow_WhenProviderIsUnknown()
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _paymentService.CreateAsync(
            CreateRequest(PaymentKind.Subscription, PersonalPlusTariffId, Guid.NewGuid()) with
            {
                ProviderCode = "unknown",
            },
            CancellationToken.None));
    }

    private static CreatePaymentRequest CreateRequest(
        PaymentKind kind,
        Guid itemId,
        Guid paidEntityId,
        bool isOrganization = false,
        string currencyCode = "RUB") => new()
    {
        ServiceId = ServiceId.LaraueBoards,
        Kind = kind,
        ItemId = itemId,
        PaidEntityId = paidEntityId,
        IsOrganization = isOrganization,
        OwnerId = paidEntityId,
        CurrencyCode = currencyCode,
    };
}
