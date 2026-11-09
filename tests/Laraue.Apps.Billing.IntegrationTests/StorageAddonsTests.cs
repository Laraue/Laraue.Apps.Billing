using Laraue.Apps.Billing.DataAccess.Data;
using Laraue.Apps.Billing.DataAccess.Entities;
using Laraue.Apps.Billing.IntegrationTests.Infrastructure;
using Laraue.Apps.Billing.Services;
using Laraue.Apps.Billing.Services.Payments;
using Laraue.Core.Exceptions.Web;
using Microsoft.EntityFrameworkCore;
using Laraue.Core.DateTime.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Laraue.Apps.Billing.IntegrationTests;

public class StorageAddonsTests : BillingIntegrationTest
{
    private const long Megabyte = 1024L * 1024L;
    private const long Gigabyte = 1024L * Megabyte;

    private static readonly Guid PersonalPlusTariffId = new("e8e4b409-366d-4803-b116-76c1a4a6c8f1");
    private static readonly Guid TeamTariffId = new("89111f8d-292b-4f04-8766-b521e19e6964");
    private static readonly Guid SmallTokenPackId = new("76abd692-c450-4cd6-80e4-b9c012d91610");
    private static readonly Guid Addon10GbId = StorageAddonsData.Addons[0].Id;
    private static readonly Guid Addon50GbId = StorageAddonsData.Addons[1].Id;

    private readonly IServiceScope _scope;
    private readonly ICorePaymentService _paymentService;
    private readonly IPaymentFulfillment _fulfillment;
    private readonly ISubscriptionService _subscriptionService;

    public StorageAddonsTests(WebApiTestHost host) : base(host)
    {
        _scope = host.Services.CreateScope();
        _paymentService = _scope.ServiceProvider.GetRequiredService<ICorePaymentService>();
        // On the test's own context, so they run in the transaction InTransaction starts.
        var dateTimeProvider = _scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        _fulfillment = new PaymentFulfillmentService(
            Context,
            dateTimeProvider,
            NullLogger<PaymentFulfillmentService>.Instance);
        _subscriptionService = new SubscriptionService(Context, dateTimeProvider, new StorageQuotaService(Context));
    }

    [Fact]
    public async Task CreateAsync_ShouldChargeTariffAndAddonInOnePayment_WhenAddonIsBought()
    {
        var checkout = await _paymentService.CreateAsync(
            CreateRequest(PersonalPlusTariffId, Guid.NewGuid()) with { StorageAddonId = Addon10GbId },
            CancellationToken.None);

        // 4 + 1 USD at the seeded RUB rate (0.012), rounded up to a whole ruble.
        var payment = await Context.Payments.AsNoTracking().SingleAsync(x => x.Id == checkout.PaymentId);
        Assert.Equal(41_700, payment.AmountMinorUnits);
        Assert.Equal(PersonalPlusTariffId, payment.TariffId);
        Assert.Equal(Addon10GbId, payment.StorageAddonId);
    }

    [Fact]
    public async Task CreateAsync_ShouldThrow_WhenAddonIsBoughtWithTokenPack()
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _paymentService.CreateAsync(
            CreateRequest(SmallTokenPackId, Guid.NewGuid(), PaymentKind.TokenPack) with
            {
                StorageAddonId = Addon10GbId,
            },
            CancellationToken.None));

        Assert.Empty(await Context.Payments.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_ShouldThrow_WhenAddonDoesNotExist()
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _paymentService.CreateAsync(
            CreateRequest(PersonalPlusTariffId, Guid.NewGuid()) with { StorageAddonId = Guid.NewGuid() },
            CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_ShouldRefuseWithUsageAndQuota_WhenUsageIsAboveTheNewStorage()
    {
        var exception = await Assert.ThrowsAsync<StorageOverLimitException>(() => _paymentService.CreateAsync(
            CreateRequest(PersonalPlusTariffId, Guid.NewGuid()) with { StorageUsageBytes = 2 * Gigabyte },
            CancellationToken.None));

        Assert.Equal(2 * Gigabyte, exception.UsageBytes);
        Assert.Equal(1 * Gigabyte, exception.QuotaBytes);
        Assert.Empty(await Context.Payments.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_ShouldCreatePayment_WhenUsageFitsTheTariffAndAddon()
    {
        var checkout = await _paymentService.CreateAsync(
            CreateRequest(TeamTariffId, Guid.NewGuid(), isOrganization: true) with
            {
                StorageAddonId = Addon10GbId,
                StorageUsageBytes = 11 * Gigabyte,
            },
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, checkout.PaymentId);
    }

    [Fact]
    public async Task FulfillAsync_ShouldGiveTheSubscriptionTheAddon_WhenTariffIsBoughtWithIt()
    {
        var userId = Guid.NewGuid();

        await PayAsync(userId, Addon10GbId);

        var subscription = await InTransaction(() => _subscriptionService.GetActivePersonalSubscriptionAsync(
            ServiceId.LaraueBoards, userId, CancellationToken.None));

        var boards = Assert.IsType<LaraueBoardsPersonalActiveSubscription>(subscription);
        Assert.Equal(Addon10GbId, boards.StorageAddonId);
        Assert.Equal(11 * Gigabyte, boards.StorageQuotaBytes);
    }

    [Fact]
    public async Task FulfillAsync_ShouldReplaceTheAddonAtOnce_WhenSameTariffIsRenewedWithAnotherOne()
    {
        var userId = Guid.NewGuid();
        await PayAsync(userId, Addon10GbId);

        await PayAsync(userId, Addon50GbId);

        var subscription = await Context.Subscriptions.AsNoTracking()
            .SingleAsync(x => x.PaidEntityId == userId);
        Assert.Equal(Addon50GbId, subscription.StorageAddonId);
    }

    [Fact]
    public async Task FulfillAsync_ShouldRemoveTheAddon_WhenSameTariffIsRenewedWithoutOne()
    {
        var userId = Guid.NewGuid();
        await PayAsync(userId, Addon10GbId);

        await PayAsync(userId, storageAddonId: null);

        var subscription = await Context.Subscriptions.AsNoTracking()
            .SingleAsync(x => x.PaidEntityId == userId);
        Assert.Null(subscription.StorageAddonId);
    }

    [Fact]
    public async Task GetActivePersonalSubscriptionAsync_ShouldReturnFreeStorage_WhenPlanIsFree()
    {
        var subscription = await InTransaction(() => _subscriptionService.GetActivePersonalSubscriptionAsync(
            ServiceId.LaraueBoards, Guid.NewGuid(), CancellationToken.None));

        var boards = Assert.IsType<LaraueBoardsPersonalActiveSubscription>(subscription);
        Assert.Equal(100 * Megabyte, boards.StorageQuotaBytes);
        Assert.Null(boards.StorageAddonId);
    }

    [Fact]
    public async Task GetActiveOrganizationSubscriptionAsync_ShouldReturnFreeStorage_WhenPlanIsFree()
    {
        var subscription = await InTransaction(() => _subscriptionService.GetActiveOrganizationSubscriptionAsync(
            ServiceId.LaraueBoards, Guid.NewGuid(), CancellationToken.None));

        var boards = Assert.IsType<LaraueBoardsTeamActiveSubscription>(subscription);
        Assert.Equal(100 * Megabyte, boards.StorageQuotaBytes);
    }

    /// <summary>
    /// Fulfils a paid Plus payment of <paramref name="userId"/>, the way a provider's notification does.
    /// </summary>
    private async Task PayAsync(Guid userId, Guid? storageAddonId)
    {
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            ServiceId = ServiceId.LaraueBoards,
            Kind = PaymentKind.Subscription,
            PaidEntityId = userId,
            OwnerId = userId,
            TariffId = PersonalPlusTariffId,
            StorageAddonId = storageAddonId,
            AmountMinorUnits = 41_700,
            CurrencyCode = "RUB",
            Status = PaymentStatus.Pending,
            Provider = "robokassa",
            CreatedAt = DateTime.UtcNow,
        };

        Context.Payments.Add(payment);
        await Context.SaveChangesAsync();

        await InTransaction(async () =>
        {
            await _fulfillment.FulfillAsync(payment, CancellationToken.None);
            await Context.SaveChangesAsync();
        });

        Context.ChangeTracker.Clear();
    }

    private static CreatePaymentRequest CreateRequest(
        Guid itemId,
        Guid paidEntityId,
        PaymentKind kind = PaymentKind.Subscription,
        bool isOrganization = false) => new()
    {
        ServiceId = ServiceId.LaraueBoards,
        Kind = kind,
        ItemId = itemId,
        PaidEntityId = paidEntityId,
        IsOrganization = isOrganization,
        OwnerId = paidEntityId,
        CurrencyCode = "RUB",
    };
}
