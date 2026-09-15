using Laraue.Apps.Billing.DataAccess;
using Laraue.Apps.Billing.WebApiHost;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Billing.IntegrationTests.Infrastructure;

[Collection("IntegrationTest")]
public abstract class BillingIntegrationTest : IClassFixture<WebApiTestHost>, IDisposable
{
    private readonly IServiceScope _scope;

    protected DatabaseContext Context { get; }

    protected BillingIntegrationTest(WebApiTestHost host)
    {
        _scope = host.Services.CreateScope();
        Context = _scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        Context.CleanDatabase();
    }

    public void Dispose() => _scope.Dispose();
}
