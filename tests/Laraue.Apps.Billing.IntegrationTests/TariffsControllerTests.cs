using Laraue.Apps.Billing.DataAccess.Entities;
using Laraue.Apps.Billing.IntegrationTests.Infrastructure;
using Laraue.Apps.Billing.WebApi.Controllers;
using Laraue.Apps.Billing.WebApiServices;
using Laraue.Core.Exceptions.Web;

namespace Laraue.Apps.Billing.IntegrationTests;

public class TariffsControllerTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    [Theory]
    [InlineData("USD")]
    [InlineData("RUB")]
    public async Task GetServiceTariffs_ShouldReturnLaraueBoardsTariffs_WhenCurrencyIsSupported(string currencyCode)
    {
        var response = await host.Controller<TariffsController>()
            .Execute(c => c.GetServiceTariffs(
                new GetServiceTariffsRequest
                {
                    ServiceId = ServiceId.LaraueBoards,
                    CurrencyCode = currencyCode,
                },
                CancellationToken.None));

        Assert.NotEmpty(response!.PersonalSubscriptions);
        Assert.All(response.PersonalSubscriptions, x => Assert.Equal(currencyCode, x.CurrencyCode));
        Assert.All(response.TeamSubscriptions, x => Assert.Equal(currencyCode, x.CurrencyCode));
    }

    [Theory]
    [InlineData("USD")]
    [InlineData("RUB")]
    public async Task GetServiceTariffs_ShouldReturnMarkdownTranslatorTariffs_WhenCurrencyIsSupported(string currencyCode)
    {
        var response = await host.Controller<TariffsController>()
            .Execute(c => c.GetServiceTariffs(
                new GetServiceTariffsRequest
                {
                    ServiceId = ServiceId.MarkdownTranslator,
                    CurrencyCode = currencyCode,
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
                    CurrencyCode = "USD",
                },
                CancellationToken.None)));

        Assert.IsType<BadRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task GetServiceTariffs_ShouldThrowBadRequestException_WhenCurrencyDoesNotExist()
    {
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => host.Controller<TariffsController>()
            .Execute(c => c.GetServiceTariffs(
                new GetServiceTariffsRequest
                {
                    ServiceId = ServiceId.LaraueBoards,
                    CurrencyCode = "XXX",
                },
                CancellationToken.None)));

        Assert.IsType<BadRequestException>(exception.InnerException);
    }
}
