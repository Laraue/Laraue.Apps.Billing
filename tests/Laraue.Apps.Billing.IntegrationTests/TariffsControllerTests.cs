using Laraue.Apps.Billing.DataAccess.Entities;
using Laraue.Apps.Billing.IntegrationTests.Infrastructure;
using Laraue.Apps.Billing.WebApiHost.Controllers;
using Laraue.Apps.Billing.WebApiServices;
using Laraue.Core.Exceptions.Web;

namespace Laraue.Apps.Billing.IntegrationTests;

[Collection("IntegrationTest")]
public class TariffsControllerTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    [Theory]
    [InlineData("RUB")]
    [InlineData("rub")]
    [InlineData(null)]
    public async Task GetServiceTariffs_ShouldReturnLaraueBoardsTariffs_InThePaymentCurrency(string? requestedCurrencyCode)
    {
        const string currencyCode = "RUB";
        var response = await host.Controller<TariffsController>()
            .Execute(c => c.GetServiceTariffs(
                new GetServiceTariffsRequest
                {
                    ServiceId = ServiceId.LaraueBoards,
                    CurrencyCode = requestedCurrencyCode,
                },
                CancellationToken.None));

        Assert.NotEmpty(response!.PersonalSubscriptions);
        Assert.All(response.PersonalSubscriptions, x => Assert.Equal(currencyCode, x.CurrencyCode));
        Assert.All(response.TeamSubscriptions, x => Assert.Equal(currencyCode, x.CurrencyCode));
    }

    [Fact]
    public async Task GetServiceTariffs_ShouldReturnStorageAndAddonPrices_ForLaraueBoards()
    {
        var response = await host.Controller<TariffsController>()
            .Execute(c => c.GetServiceTariffs(
                new GetServiceTariffsRequest
                {
                    ServiceId = ServiceId.LaraueBoards,
                },
                CancellationToken.None));

        // +10 GB for 1 USD, +50 GB for 3 USD, at the seeded RUB rate rounded up to a whole ruble.
        Assert.Collection(
            response!.StorageAddons,
            x => Assert.Equal((10 * 1024, 84m), (x.StorageMegabytes, x.Price)),
            x => Assert.Equal((50 * 1024, 250m), (x.StorageMegabytes, x.Price)));

        var personal = response.PersonalSubscriptions.Cast<LaraueBoardsPersonalSubscription>().ToList();
        var free = personal.Single(x => x.Title == "Free");
        Assert.Equal(100, free.IncludedStorageMegabytes);
        Assert.Empty(free.StorageAddonPrices);

        // Plus (4 USD) with +10 GB (1 USD) is priced as one sum, like the checkout charges it.
        var plus = personal.Single(x => x.Title == "Plus");
        Assert.Equal(1024, plus.IncludedStorageMegabytes);
        Assert.Equal(417m, plus.StorageAddonPrices.Single(x => x.StorageAddonId == response.StorageAddons[0].Id).Price);

        Assert.All(
            response.TeamSubscriptions.Cast<LaraueBoardsTeamSubscription>(),
            x => Assert.Equal(x.Price == 0 ? 100 : 1024, x.IncludedStorageMegabytes));
    }

    [Fact]
    public async Task GetServiceTariffs_ShouldReturnNoStorageAddons_ForMarkdownTranslator()
    {
        var response = await host.Controller<TariffsController>()
            .Execute(c => c.GetServiceTariffs(
                new GetServiceTariffsRequest
                {
                    ServiceId = ServiceId.MarkdownTranslator,
                },
                CancellationToken.None));

        Assert.Empty(response!.StorageAddons);
    }

    [Fact]
    public async Task GetServiceTariffs_ShouldReturnMarkdownTranslatorTariffs_InThePaymentCurrency()
    {
        const string currencyCode = "RUB";
        var response = await host.Controller<TariffsController>()
            .Execute(c => c.GetServiceTariffs(
                new GetServiceTariffsRequest
                {
                    ServiceId = ServiceId.MarkdownTranslator,
                },
                CancellationToken.None));

        Assert.NotEmpty(response!.PersonalSubscriptions);
        Assert.All(response.PersonalSubscriptions, x => Assert.Equal(currencyCode, x.CurrencyCode));
        Assert.Empty(response.TeamSubscriptions);
    }

    [Fact]
    public async Task GetServiceTariffs_ShouldThrowBadRequestException_WhenServiceDoesNotExist()
    {
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => host.Controller<TariffsController>()
            .Execute(c => c.GetServiceTariffs(
                new GetServiceTariffsRequest
                {
                    ServiceId = (ServiceId)(-1),
                },
                CancellationToken.None)));

        Assert.IsType<BadRequestException>(exception.InnerException);
    }

    [Theory]
    [InlineData("USD")]
    [InlineData("XXX")]
    public async Task GetServiceTariffs_ShouldThrowBadRequestException_WhenProviderCannotChargeTheCurrency(string currencyCode)
    {
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => host.Controller<TariffsController>()
            .Execute(c => c.GetServiceTariffs(
                new GetServiceTariffsRequest
                {
                    ServiceId = ServiceId.LaraueBoards,
                    CurrencyCode = currencyCode,
                },
                CancellationToken.None)));

        Assert.IsType<BadRequestException>(exception.InnerException);
    }
}
