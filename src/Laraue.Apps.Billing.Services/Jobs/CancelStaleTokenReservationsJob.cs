using Laraue.Apps.Billing.DataAccess;
using Laraue.Apps.Billing.DataAccess.Entities;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Core.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Laraue.Apps.Billing.Services.Jobs;

/// <summary>
/// Reconciles <see cref="TokenTransaction"/> rows stuck in <see cref="TokenSpentStatus.Started"/> -
/// a reservation whose caller crashed or otherwise never called
/// <see cref="ITokenService.CommitTokensSpentAsync"/>/<see cref="ITokenService.CancelTokensReservationAsync"/>
/// would otherwise hold its full reservation ceiling against the paid entity's balance forever,
/// since nothing else in this system ever revisits a <c>Started</c> transaction. Runs in
/// <c>WorkerHost</c>, not <c>InternalApiHost</c>/<c>WebApiHost</c> - registered via
/// <c>Laraue.Core.Extensions.Hosting.EfCore</c>'s <c>AddBackgroundJob</c>.
/// </summary>
public class CancelStaleTokenReservationsJob(
    DatabaseContext context,
    ITokenService tokenService,
    IDateTimeProvider dateTimeProvider,
    ILogger<CancelStaleTokenReservationsJob> logger) : BaseJob
{
    /// <summary>
    /// A normal reserve-then-commit round trip (an AI call plus its response) completes in
    /// seconds, so anything still <c>Started</c> after this long is treated as abandoned rather
    /// than merely slow.
    /// </summary>
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromMinutes(30);

    private static readonly TimeSpan RunInterval = TimeSpan.FromMinutes(5);

    public override async Task<TimeSpan> ExecuteAsync(JobState<EmptyJobData> jobState, CancellationToken stoppingToken)
    {
        var staleBefore = dateTimeProvider.UtcNow - StaleThreshold;

        var staleTransactionIds = await context.TokenTransactions
            .Where(t => t.Status == TokenSpentStatus.Started && t.CreatedAt < staleBefore)
            .Select(t => t.Id)
            .ToListAsync(stoppingToken);

        foreach (var id in staleTransactionIds)
        {
            try
            {
                await tokenService.CancelTokensReservationAsync(id, "Reservation timed out", stoppingToken);
            }
            catch (Exception ex)
            {
                // One bad row shouldn't stop the rest of the sweep - log and move on, it'll be
                // picked up again next run if it's genuinely still Started.
                logger.LogError(ex, "Failed to cancel stale token reservation {TokenTransactionId}", id);
            }
        }

        if (staleTransactionIds.Count > 0)
        {
            logger.LogWarning("Canceled {Count} stale token reservations", staleTransactionIds.Count);
        }

        return RunInterval;
    }
}
