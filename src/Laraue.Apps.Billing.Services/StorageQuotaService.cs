using Laraue.Apps.Billing.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Billing.Services;

/// <summary>
/// The file storage a subscription gives: its tariff's included storage plus its add-on's. Tariffs and
/// add-ons keep sizes in megabytes; callers get bytes.
/// </summary>
public interface IStorageQuotaService
{
    /// <summary>
    /// The quota in bytes of a tariff's storage and an add-on's, both in megabytes (null for no add-on).
    /// </summary>
    long GetQuotaBytes(int tariffMegabytes, int? addonMegabytes);

    /// <summary>
    /// The storage of a Laraue Boards tariff (personal or team) with an optional add-on, in bytes. Null
    /// when the tariff isn't a Laraue Boards one, so it has no storage.
    /// </summary>
    Task<long?> GetLaraueBoardsQuotaBytesAsync(
        Guid tariffId,
        Guid? storageAddonId,
        CancellationToken cancellationToken);
}

public class StorageQuotaService(DatabaseContext context) : IStorageQuotaService
{
    private const long BytesPerMegabyte = 1024L * 1024L;

    public long GetQuotaBytes(int tariffMegabytes, int? addonMegabytes) =>
        (tariffMegabytes + (long)(addonMegabytes ?? 0)) * BytesPerMegabyte;

    public async Task<long?> GetLaraueBoardsQuotaBytesAsync(
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
            ? (int?)null
            : await context.StorageAddons
                .Where(x => x.Id == storageAddonId)
                .Select(x => x.StorageMegabytes)
                .SingleAsync(cancellationToken);

        return GetQuotaBytes(tariffMegabytes.Value, addonMegabytes);
    }
}
