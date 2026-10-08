using Laraue.Apps.Billing.WorkerServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Laraue.Apps.Billing.IntegrationTests;

public class PaymentExpirationOptionsTests
{
    [Fact]
    public void Value_ShouldBeRead_WhenSettingIsConfigured()
    {
        var options = Resolve(new Dictionary<string, string?> { ["Payments:PendingExpiration"] = "02:30:00" });

        Assert.Equal(TimeSpan.FromHours(2.5), options.PendingExpiration);
    }

    [Fact]
    public void Value_ShouldThrow_WhenSettingIsMissing()
    {
        Assert.Throws<OptionsValidationException>(() => Resolve(new Dictionary<string, string?>()));
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("-01:00:00")]
    public void Value_ShouldThrow_WhenSettingIsNotPositive(string value)
    {
        Assert.Throws<OptionsValidationException>(
            () => Resolve(new Dictionary<string, string?> { ["Payments:PendingExpiration"] = value }));
    }

    private static PaymentExpirationOptions Resolve(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        using var provider = new ServiceCollection()
            .AddWorkerServices(configuration)
            .BuildServiceProvider();

        return provider.GetRequiredService<IOptions<PaymentExpirationOptions>>().Value;
    }
}
