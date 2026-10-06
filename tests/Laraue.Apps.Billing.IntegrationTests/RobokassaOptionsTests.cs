using System.ComponentModel.DataAnnotations;
using Laraue.Apps.Billing.Payments.Robokassa;

namespace Laraue.Apps.Billing.IntegrationTests;

public class RobokassaOptionsTests
{
    private static List<ValidationResult> Validate(params string[] currencies)
    {
        var options = new RobokassaOptions
        {
            MerchantLogin = "shop",
            Password1 = "pass1",
            Password2 = "pass2",
            PaymentUrl = "https://pay.example",
            Culture = "ru",
            Currencies = currencies,
        };

        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        return results;
    }

    [Fact]
    public void Validate_ShouldAccept_WhenOnlyRubIsConfigured()
    {
        Assert.Empty(Validate("RUB"));
        Assert.Empty(Validate("rub"));
    }

    [Theory]
    [InlineData("USD")]
    [InlineData("RUB", "EUR")]
    public void Validate_ShouldRefuseACurrencyTheCheckoutCannotExpress(params string[] currencies)
    {
        Assert.Contains(Validate(currencies), x => x.MemberNames.Contains(nameof(RobokassaOptions.Currencies)));
    }

    [Fact]
    public void Validate_ShouldRefuseAnEmptyCurrencyList()
    {
        Assert.NotEmpty(Validate());
    }
}
