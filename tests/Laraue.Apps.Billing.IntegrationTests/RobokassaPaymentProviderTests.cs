using Laraue.Apps.Billing.Payments.Robokassa;
using Laraue.Apps.Billing.Services.Payments;
using Laraue.Core.Exceptions.Web;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Laraue.Apps.Billing.IntegrationTests;

public class RobokassaPaymentProviderTests
{
    private static readonly Guid PaymentId = Guid.Parse("3f2b6c1e-8a44-4d0a-9f7e-1c2d3e4f5a6b");

    private static RobokassaPaymentProvider CreateProvider(
        bool isTest = false,
        RobokassaHashAlgorithm hashAlgorithm = RobokassaHashAlgorithm.Md5)
    {
        var options = new RobokassaOptions
        {
            MerchantLogin = "shop",
            PaymentUrl = "https://auth.robokassa.ru/Merchant/Index.aspx",
            Culture = "ru",
            Password1 = "pass1",
            Password2 = "pass2",
            TestPassword1 = "testpass1",
            TestPassword2 = "testpass2",
            IsTest = isTest,
            HashAlgorithm = hashAlgorithm,
        };

        return new RobokassaPaymentProvider(
            Options.Create(options),
            NullLogger<RobokassaPaymentProvider>.Instance);
    }

    private static PaymentNotificationRequest CreateNotification(
        string signature,
        string outSum = "1250.000000",
        params (string Name, string Value)[] extra)
    {
        var parameters = new Dictionary<string, string>
        {
            ["OutSum"] = outSum,
            ["InvId"] = "77",
            ["SignatureValue"] = signature,
            ["Shp_paymentId"] = PaymentId.ToString(),
        };

        foreach (var (name, value) in extra)
        {
            parameters[name] = value;
        }

        return new PaymentNotificationRequest { Parameters = parameters };
    }

    private static PaymentCheckoutRequest CreateCheckoutRequest(string description = "Pro plan") => new()
    {
        PaymentId = PaymentId,
        AmountMinorUnits = 125_000,
        CurrencyCode = "RUB",
        Description = description,
    };

    [Fact]
    public async Task CreateCheckoutAsync_ShouldSignWithoutInvId_Always()
    {
        var checkout = await CreateProvider().CreateCheckoutAsync(CreateCheckoutRequest(), CancellationToken.None);

        var query = ParseQuery(checkout.Url);
        Assert.StartsWith("https://auth.robokassa.ru/Merchant/Index.aspx?", checkout.Url);
        Assert.Equal("shop", query["MerchantLogin"]);
        Assert.Equal("1250.00", query["OutSum"]);
        Assert.Equal(PaymentId.ToString(), query["Shp_paymentId"]);
        Assert.False(query.ContainsKey("InvId"));
        Assert.False(query.ContainsKey("IsTest"));
        // md5("shop:1250.00::pass1:Shp_paymentId=<id>")
        Assert.Equal("95d4147b123c604ab37b7de29e1a44e5", query["SignatureValue"]);
        Assert.Null(checkout.ProviderPaymentId);
    }

    [Fact]
    public async Task CreateCheckoutAsync_ShouldUseTestPasswordsAndFlag_WhenTestModeIsOn()
    {
        var checkout = await CreateProvider(isTest: true)
            .CreateCheckoutAsync(CreateCheckoutRequest(), CancellationToken.None);

        var query = ParseQuery(checkout.Url);
        Assert.Equal("1", query["IsTest"]);
        // md5("shop:1250.00::testpass1:Shp_paymentId=<id>")
        Assert.Equal("22f3fd47e316cf719f8481e02a325be2", query["SignatureValue"]);
    }

    [Fact]
    public async Task CreateCheckoutAsync_ShouldCutDescription_WhenItIsLongerThanTheLimit()
    {
        var checkout = await CreateProvider()
            .CreateCheckoutAsync(CreateCheckoutRequest(new string('a', 150)), CancellationToken.None);

        Assert.Equal(100, ParseQuery(checkout.Url)["Description"].Length);
    }

    [Fact]
    public void ParseNotification_ShouldReturnPaidNotification_WhenSignatureIsValid()
    {
        // md5("1250.000000:77:pass2:Shp_paymentId=<id>")
        var notification = CreateProvider()
            .ParseNotification(CreateNotification("19c41a69268c2a5fe17640573a80a154"));

        Assert.Equal(PaymentId, notification.PaymentId);
        Assert.Equal("77", notification.ProviderPaymentId);
        Assert.Equal(PaymentNotificationOutcome.Paid, notification.Outcome);
        Assert.Equal(125_000, notification.AmountMinorUnits);
        Assert.Null(notification.CurrencyCode);
    }

    [Fact]
    public void ParseNotification_ShouldAcceptUpperCaseSignature_WhenSignatureIsValid()
    {
        var notification = CreateProvider()
            .ParseNotification(CreateNotification("19C41A69268C2A5FE17640573A80A154"));

        Assert.Equal(PaymentId, notification.PaymentId);
    }

    [Fact]
    public void ParseNotification_ShouldSupportSha256_WhenShopUsesIt()
    {
        var notification = CreateProvider(hashAlgorithm: RobokassaHashAlgorithm.Sha256).ParseNotification(
            CreateNotification("d0ca7ee3803668effa6b8fc8255be2c06c6cf5523f998045fd8cf3fd27ac15dd"));

        Assert.Equal(PaymentId, notification.PaymentId);
    }

    [Fact]
    public void ParseNotification_ShouldUseTestPassword_WhenTestModeIsOn()
    {
        var notification = CreateProvider(isTest: true)
            .ParseNotification(CreateNotification("c8668fc2ab85fde0e68ea66b24349779"));

        Assert.Equal(PaymentId, notification.PaymentId);
    }

    [Fact]
    public void ParseNotification_ShouldSignOtherCustomParametersSorted_WhenThereAreAny()
    {
        // md5("1250.000000:77:pass2:Shp_a=1:Shp_paymentId=<id>")
        var notification = CreateProvider().ParseNotification(
            CreateNotification("15ed7c6e3d7fb2a6e7c308733f5b79b3", extra: ("Shp_a", "1")));

        Assert.Equal(PaymentId, notification.PaymentId);
    }

    [Fact]
    public void ParseNotification_ShouldThrow_WhenSignatureIsWrong()
    {
        Assert.Throws<BadRequestException>(() => CreateProvider()
            .ParseNotification(CreateNotification("00000000000000000000000000000000")));
    }

    [Fact]
    public void ParseNotification_ShouldThrow_WhenAmountWasChanged()
    {
        Assert.Throws<BadRequestException>(() => CreateProvider()
            .ParseNotification(CreateNotification("19c41a69268c2a5fe17640573a80a154", outSum: "1.000000")));
    }

    [Fact]
    public void ParseNotification_ShouldThrow_WhenSignedWithTheOtherModesPassword()
    {
        // Signed with the test password while the provider is in live mode.
        Assert.Throws<BadRequestException>(() => CreateProvider()
            .ParseNotification(CreateNotification("c8668fc2ab85fde0e68ea66b24349779")));
    }

    [Fact]
    public void ParseNotification_ShouldThrow_WhenAParameterIsMissing()
    {
        var request = new PaymentNotificationRequest
        {
            Parameters = new Dictionary<string, string> { ["OutSum"] = "1250.000000" },
        };

        Assert.Throws<BadRequestException>(() => CreateProvider().ParseNotification(request));
    }

    [Fact]
    public void CreateNotificationAck_ShouldReturnOkWithInvId_Always()
    {
        var provider = CreateProvider();
        var notification = provider.ParseNotification(CreateNotification("19c41a69268c2a5fe17640573a80a154"));

        Assert.Equal("OK77", provider.CreateNotificationAck(notification));
    }

    private static Dictionary<string, string> ParseQuery(string url)
    {
        return url[(url.IndexOf('?') + 1)..]
            .Split('&')
            .Select(x => x.Split('=', 2))
            .ToDictionary(x => x[0], x => Uri.UnescapeDataString(x[1]));
    }
}
