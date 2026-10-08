using Laraue.Apps.Billing.DataAccess;
using Laraue.Apps.Billing.DataAccess.Entities;
using Laraue.Apps.Billing.Services;
using Laraue.Apps.Billing.Services.Metrics;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Core.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Laraue.Apps.Billing.WorkerServices;

/// <summary>
/// Marks payments still <see cref="PaymentStatus.Pending"/> long after their checkout was created as
/// <see cref="PaymentStatus.Expired"/>. A customer who opens the checkout and leaves is never reported by a provider
/// (Robokassa only notifies a payment that was made), so without this such a payment stays Pending forever, the
/// "pending payments" gauge never drops and a payment that is really stuck cannot be told apart from an abandoned one.
/// </summary>
/// <remarks>
/// Expiring is not final. A notification that the customer did pay still fulfils an expired payment
/// (<c>CorePaymentService.HandleNotificationAsync</c> fulfils anything that is not paid yet), so a customer who pays
/// the old link later is not lost. Done here rather than in <c>ICorePaymentService</c> because the worker has no
/// payment provider configuration, which that service needs. It takes the same per-payment lock as the notification
/// handler, so an expiry and a notification for one payment never interleave.
/// </remarks>
public class ExpireStalePaymentsJob(
    DatabaseContext context,
    IOptions<PaymentExpirationOptions> options,
    IDateTimeProvider dateTimeProvider,
    BillingMetrics metrics,
    ILogger<ExpireStalePaymentsJob> logger) : BaseJob
{
    private static readonly TimeSpan RunInterval = TimeSpan.FromMinutes(5);

    private const int BatchSize = 200;

    public override async Task<TimeSpan> ExecuteAsync(JobState<EmptyJobData> jobState, CancellationToken stoppingToken)
    {
        await ExpireStalePaymentsAsync(stoppingToken);

        return RunInterval;
    }

    /// <returns>How many payments were expired.</returns>
    public async Task<int> ExpireStalePaymentsAsync(CancellationToken cancellationToken)
    {
        var expiration = options.Value.PendingExpiration!.Value;
        var createdBefore = dateTimeProvider.UtcNow - expiration;

        var staleIds = await context.Payments
            .Where(x => x.Status == PaymentStatus.Pending && x.CreatedAt < createdBefore)
            .OrderBy(x => x.CreatedAt)
            .Select(x => x.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        var expired = 0;
        foreach (var id in staleIds)
        {
            try
            {
                if (await context.Database.InTransactionAsync(
                        () => ExpireAsync(id, createdBefore, expiration, cancellationToken),
                        cancellationToken))
                {
                    expired++;
                }
            }
            catch (Exception ex)
            {
                // One bad row must not stop the sweep; it is tried again on the next run.
                logger.LogError(ex, "Failed to expire payment {PaymentId}", id);
            }
            finally
            {
                context.ChangeTracker.Clear();
            }
        }

        if (expired > 0)
        {
            logger.LogInformation(
                "Expired {Count} payments that waited longer than {PendingExpiration} for the provider",
                expired,
                expiration);
        }

        return expired;
    }

    private async Task<bool> ExpireAsync(
        Guid paymentId,
        DateTime createdBefore,
        TimeSpan expiration,
        CancellationToken cancellationToken)
    {
        await context.Database.PgAdvisoryPaymentXactLock(paymentId, cancellationToken);

        // Read again under the lock: a notification may have paid the payment since the sweep listed it.
        var payment = await context.Payments.SingleAsync(x => x.Id == paymentId, cancellationToken);
        if (payment.Status != PaymentStatus.Pending || payment.CreatedAt >= createdBefore)
        {
            return false;
        }

        payment.Status = PaymentStatus.Expired;
        await context.SaveChangesAsync(cancellationToken);

        metrics.RecordPaymentCompleted(payment.Provider, payment.Kind, PaymentStatus.Expired);

        logger.LogInformation(
            "Payment {PaymentId} of {Provider} expired: still pending {Age} after it was created, expiration {PendingExpiration}",
            payment.Id,
            payment.Provider,
            dateTimeProvider.UtcNow - payment.CreatedAt,
            expiration);

        return true;
    }
}
