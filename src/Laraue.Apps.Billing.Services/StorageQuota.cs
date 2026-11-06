using Laraue.Apps.Billing.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Billing.Services;

/// <summary>
/// The file storage a subscription gives: its tariff's included storage plus its add-on's. Tariffs and
/// add-ons keep sizes in megabytes; callers get bytes.
/// </summary>
public static class StorageQuota
{
    private const long BytesPerMegabyte = 1024L * 1024L;

    public static long ToBytes(int megabytes) => megabytes * BytesPerMegabyte;

    /// <summary>
    /// The storage of a Laraue Boards tariff (personal or team) with an optional add-on, in bytes. Null
    /// when the tariff isn't a Laraue Boards one, so it has no storage.
    /// </summary>
    public static async Task<long?> GetLaraueBoardsQuotaBytesAsync(
        DatabaseContext context,
        Guid tariffId,
        Guid? storageAddonId,
        CancellationToken cancellationToken)
    {
        var tariffMegabytes = await context.LaraueBoardsPersonalTariffs
            .Where(x => x.Id == tariffId)
            .Select(x => (int?)x.IncludedStorageMegabytes)
            .SingleOrDefaultAsync(cancellationToken)
            ?? await context.LaraueBoardsTeamTariffs
                .Where(x => x.Id == tariffId)
                .Select(x => (int?)x.IncludedStorageMegabytes)
                .SingleOrDefaultAsync(cancellationToken);

        if (tariffMegabytes is null)
        {
            return null;
        }

        var addonMegabytes = storageAddonId is null
            ? 0
            : await context.StorageAddons
                .Where(x => x.Id == storageAddonId)
                .Select(x => x.StorageMegabytes)
                .SingleAsync(cancellationToken);

        return ToBytes(tariffMegabytes.Value) + ToBytes(addonMegabytes);
    }
}
