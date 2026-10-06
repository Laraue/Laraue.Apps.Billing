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
