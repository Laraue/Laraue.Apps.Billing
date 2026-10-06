using Laraue.Apps.Billing.DataAccess.Entities;
using Laraue.Apps.Billing.IntegrationTests.Infrastructure;
using Laraue.Apps.Billing.Services.Payments;
using Laraue.Apps.Billing.WebApiServices;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Billing.IntegrationTests;

/// <summary>
/// Fails for a <see cref="ServiceId"/> that was added without everything a product needs to be sold:
/// tariffs that can be mapped and served, and a payment check that knows its tariff tables. The list
/// of services is the enum, so a new value is covered without touching this test. See "Adding a
/// product" in AGENTS.md.
/// </summary>
public class ServiceOnboardingTests : BillingIntegrationTest
{
    private readonly IServiceScope _scope;
    private readonly ITariffService _tariffService;
    private readonly ICorePaymentService _paymentService;

    public ServiceOnboardingTests(WebApiTestHost host) : base(host)
    {
        _scope = host.Services.CreateScope();
        _tariffService = _scope.ServiceProvider.GetRequiredService<ITariffService>();
        _paymentService = _scope.ServiceProvider.GetRequiredService<ICorePaymentService>();
    }

    public static TheoryData<ServiceId> Services()
    {
        var data = new TheoryData<ServiceId>();
        foreach (var serviceId in Enum.GetValues<ServiceId>())
        {
            data.Add(serviceId);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Services))]
    public async Task Service_ShouldServeTariffsAndAcceptPaymentForThem_Always(ServiceId serviceId)
    {
        var tariffs = await _tariffService.GetServiceTariffs(
            new GetServiceTariffsRequest { ServiceId = serviceId },
            CancellationToken.None);

        var paid = new List<(bool IsOrganization, Guid Id, string CurrencyCode)>();
        paid.AddRange(tariffs.PersonalSubscriptions
            .Where(x => x.Price > 0)
            .Select(x => (false, x.Id, x.CurrencyCode)));
        paid.AddRange(tariffs.TeamSubscriptions
            .Where(x => x.Price > 0)
            .Select(x => (true, x.Id, x.CurrencyCode)));

        Assert.True(
            paid.Count > 0,
            $"{serviceId} has no paid tariffs: seed them and map them in TariffService.");

        foreach (var (isOrganization, itemId, currencyCode) in paid)
        {
            var paidEntityId = Guid.NewGuid();

            var checkout = await _paymentService.CreateAsync(
                new CreatePaymentRequest
                {
                    ServiceId = serviceId,
                    Kind = PaymentKind.Subscription,
                    ItemId = itemId,
                    PaidEntityId = paidEntityId,
                    IsOrganization = isOrganization,
                    OwnerId = paidEntityId,
                    CurrencyCode = currencyCode,
                },
                CancellationToken.None);

            Assert.NotEqual(Guid.Empty, checkout.PaymentId);
        }
    }
}
