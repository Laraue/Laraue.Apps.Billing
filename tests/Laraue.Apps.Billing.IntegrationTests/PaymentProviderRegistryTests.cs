using Laraue.Apps.Billing.Services.Payments;
using Laraue.Core.Exceptions.Web;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Laraue.Apps.Billing.IntegrationTests;

public class PaymentProviderRegistryTests
{
    private static IPaymentProvider Provider(string code, params string[] currencies)
    {
        var provider = new Mock<IPaymentProvider>();
        provider.SetupGet(x => x.Code).Returns(code);
        provider.SetupGet(x => x.SupportedCurrencies).Returns(currencies.ToHashSet());

        return provider.Object;
    }

    private static PaymentProviderRegistry CreateRegistry(string defaultProvider, params IPaymentProvider[] providers)
    {
        return new PaymentProviderRegistry(
            providers,
            Options.Create(new PaymentsOptions { DefaultProvider = defaultProvider }),
            NullLogger<PaymentProviderRegistry>.Instance);
    }

    [Fact]
    public void SupportedCurrencies_ShouldCombineTheCurrenciesOfEveryProvider()
    {
        var registry = CreateRegistry("robokassa", Provider("robokassa", "RUB"), Provider("stripe", "USD", "EUR"));

        Assert.Equal(["EUR", "RUB", "USD"], registry.SupportedCurrencies.Order());
    }

    [Fact]
    public void ResolveCurrency_ShouldReturnTheDefaultProvidersCurrency_WhenNoneIsRequested()
    {
        var registry = CreateRegistry("robokassa", Provider("robokassa", "RUB"), Provider("stripe", "USD"));

        Assert.Equal("RUB", registry.ResolveCurrency(null));
    }

    [Fact]
    public void ResolveCurrency_ShouldAcceptACurrencyOfANonDefaultProvider()
    {
        var registry = CreateRegistry("robokassa", Provider("robokassa", "RUB"), Provider("stripe", "USD"));

        Assert.Equal("USD", registry.ResolveCurrency("usd"));
    }

    [Fact]
    public void ResolveCurrency_ShouldRefuseACurrencyNoProviderCharges()
    {
        var registry = CreateRegistry("robokassa", Provider("robokassa", "RUB"));

        Assert.Throws<BadRequestException>(() => registry.ResolveCurrency("USD"));
    }

    [Fact]
    public void GetForCurrency_ShouldPreferTheDefaultProvider_WhenItChargesInTheCurrency()
    {
        var registry = CreateRegistry("stripe", Provider("robokassa", "RUB", "USD"), Provider("stripe", "USD"));

        Assert.Equal("stripe", registry.GetForCurrency("USD").Code);
    }

    [Fact]
    public void GetForCurrency_ShouldFallBackToAnotherProvider_WhenTheDefaultDoesNotChargeInTheCurrency()
    {
        var registry = CreateRegistry("robokassa", Provider("robokassa", "RUB"), Provider("stripe", "USD"));

        Assert.Equal("stripe", registry.GetForCurrency("USD").Code);
    }

    [Fact]
    public void GetForCurrency_ShouldRefuseACurrencyNoProviderCharges()
    {
        var registry = CreateRegistry("robokassa", Provider("robokassa", "RUB"));

        Assert.Throws<BadRequestException>(() => registry.GetForCurrency("USD"));
    }
}
