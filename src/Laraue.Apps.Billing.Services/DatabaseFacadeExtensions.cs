using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Laraue.Apps.Billing.Services;

public static class DatabaseFacadeExtensions
{
    public static void EnsureTransactionStarted(this DatabaseFacade facade)
    {
        if (facade.CurrentTransaction == null)
            throw new InvalidOperationException("Database transaction is required.");
    }

    /// <summary>
    /// Runs the action in a new transaction and commits it when the action completes; an exception
    /// rolls it back. For hosts calling core services, which only require a started transaction and
    /// never open one themselves.
    /// </summary>
    public static async Task<T> InTransactionAsync<T>(
        this DatabaseFacade facade,
        Func<Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await facade.BeginTransactionAsync(cancellationToken);

        var result = await action();
        await transaction.CommitAsync(cancellationToken);

        return result;
    }

    public static async Task InTransactionAsync(
        this DatabaseFacade facade,
        Func<Task> action,
        CancellationToken cancellationToken = default)
    {
        await facade.InTransactionAsync(
            async () =>
            {
                await action();
                return true;
            },
            cancellationToken);
    }

    public static Task PgAdvisoryXactLock(this DatabaseFacade facade, string lockKey, CancellationToken cancellationToken = default)
    {
        facade.EnsureTransactionStarted();

        return facade.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(hashtext({0})::bigint)",
            [lockKey],
            cancellationToken);
    }
}
