using Laraue.Apps.Billing.DataAccess;
using Laraue.Apps.Billing.Services;
using Laraue.Apps.Billing.Services.Metrics;
using Laraue.Apps.Billing.WebApiHost;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Billing.IntegrationTests.Infrastructure;

[Collection("IntegrationTest")]
public abstract class BillingIntegrationTest : IClassFixture<WebApiTestHost>, IDisposable
{
    private readonly IServiceScope _scope;

    protected DatabaseContext Context { get; }

    /// <summary>The host's metrics, so what a test records shows on that host's <c>/_metrics</c>.</summary>
    protected BillingMetrics Metrics { get; }

    protected BillingIntegrationTest(WebApiTestHost host)
    {
        _scope = host.Services.CreateScope();
        Context = _scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        Metrics = host.Services.GetRequiredService<BillingMetrics>();
        Context.CleanDatabase();
    }

    /// <summary>
    /// Core services need a transaction started by their caller; this does what a host does.
    /// </summary>
    protected Task<T> InTransaction<T>(Func<Task<T>> action) =>
        Context.Database.InTransactionAsync(action, CancellationToken.None);

    protected Task InTransaction(Func<Task> action) =>
        Context.Database.InTransactionAsync(action, CancellationToken.None);

    public void Dispose() => _scope.Dispose();
}
