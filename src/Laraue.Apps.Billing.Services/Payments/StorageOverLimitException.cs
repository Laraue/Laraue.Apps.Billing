using Laraue.Core.Exceptions.Web;

namespace Laraue.Apps.Billing.Services.Payments;

/// <summary>
/// A checkout refused because the customer already uses more storage than the tariff and add-on they
/// chose would give. The gRPC adapter answers it with <c>storage_over_limit</c> rather than an error, so
/// the calling service can tell the customer how much to delete.
/// </summary>
public sealed class StorageOverLimitException(long usageBytes, long quotaBytes)
    : BadRequestException(
        "storage",
        string.Format(Resources.Errors.StorageOverLimit, usageBytes, quotaBytes))
{
    public long UsageBytes { get; } = usageBytes;

    public long QuotaBytes { get; } = quotaBytes;
}
