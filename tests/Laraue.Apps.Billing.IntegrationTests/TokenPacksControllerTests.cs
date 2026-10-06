using Laraue.Apps.Billing.DataAccess.Entities;
using Laraue.Apps.Billing.IntegrationTests.Infrastructure;
using Laraue.Apps.Billing.WebApiHost.Controllers;
using Laraue.Apps.Billing.WebApiServices;
using Laraue.Core.Exceptions.Web;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Billing.IntegrationTests;

public class TokenPacksControllerTests(WebApiTestHost host) : BillingIntegrationTest(host)
{
    private static readonly Guid SmallPackId = new("76abd692-c450-4cd6-80e4-b9c012d91610");

    [Theory]
    [InlineData("RUB")]
    [InlineData("rub")]
    [InlineData(null)]
    public async Task GetTokenPacks_ShouldReturnActivePacksCheapestFirst_InThePaymentCurrency(string? currencyCode)
    {
        var response = await Controller(currencyCode);

        Assert.Equal(["small", "medium", "large"], response!.TokenPacks.Select(x => x.Code));
        Assert.All(response.TokenPacks, x => Assert.Equal("RUB", x.CurrencyCode));
        Assert.Equal(response.TokenPacks.OrderBy(x => x.Price), response.TokenPacks);
    }

    [Fact]
    public async Task GetTokenPacks_ShouldPriceAPackLikeTheCheckoutDoes_Always()
    {
        var response = await Controller("RUB");

        // 3 USD at the seeded RUB rate, rounded up to a whole ruble: the amount a checkout charges.
        var small = Assert.Single(response!.TokenPacks, x => x.Id == SmallPackId);
        Assert.Equal(250, small.Price);
        Assert.Equal("250₽", small.FormattedPrice);
        Assert.Equal(100_000, small.TokensCount);
        Assert.Equal(6, small.ExpirationDuration);
        Assert.Equal(BillingPeriod.Month, small.ExpirationPeriod);
    }

    [Fact]
    public async Task GetTokenPacks_ShouldNotReturnAnInactivePack_Always()
    {
        await Context.TokenPacks.Where(x => x.Id == SmallPackId).ExecuteUpdateAsync(
            x => x.SetProperty(p => p.IsActive, false));

        try
        {
            var response = await Controller("RUB");

            Assert.DoesNotContain(response!.TokenPacks, x => x.Id == SmallPackId);
        }
        finally
        {
            await Context.TokenPacks.Where(x => x.Id == SmallPackId).ExecuteUpdateAsync(
                x => x.SetProperty(p => p.IsActive, true));
        }
    }

    [Theory]
    [InlineData("USD")]
    [InlineData("XXX")]
    public async Task GetTokenPacks_ShouldThrowBadRequestException_WhenProviderCannotChargeTheCurrency(string currencyCode)
    {
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => Controller(currencyCode));

        Assert.IsType<BadRequestException>(exception.InnerException);
    }

    private Task<GetTokenPacksResponse?> Controller(string? currencyCode) => host.Controller<TokenPacksController>()
        .Execute(c => c.GetTokenPacks(
            new GetTokenPacksRequest { CurrencyCode = currencyCode },
            CancellationToken.None));
}
