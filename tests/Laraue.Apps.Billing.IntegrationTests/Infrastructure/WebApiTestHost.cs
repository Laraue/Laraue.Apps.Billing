using Laraue.Apps.Billing.WebApiHost;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Laraue.Apps.Billing.IntegrationTests.Infrastructure;

public class WebApiTestHost : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            config.AddJsonFile("appsettings.json", optional: true);
        });

        return base.CreateHost(builder);
    }

    public Proxy<TController> Controller<TController>() where TController : ControllerBase
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        return new Proxy<TController>(client, Services);
    }
}
