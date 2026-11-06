using Laraue.Apps.Billing.DataAccess.Entities;

namespace Laraue.Apps.Billing.DataAccess.Data;

public static class StorageAddonsData
{
    private static readonly StorageAddon LaraueBoards10Gb = new()
    {
        Id = new Guid("5c0d1d0e-8a4f-4f2b-9a3e-1f6b7c2d4e10"),
        ServiceId = ServiceId.LaraueBoards,
        Code = "storage_10gb",
        Title = "+10 GB",
        StorageMegabytes = 10 * 1024,
        Price = 1_00,
        IsActive = true,
    };

    private static readonly StorageAddon LaraueBoards50Gb = new()
    {
        Id = new Guid("9e2a6b3f-4c1d-4e8a-b7f0-3d5c8a1e6b50"),
        ServiceId = ServiceId.LaraueBoards,
        Code = "storage_50gb",
        Title = "+50 GB",
        StorageMegabytes = 50 * 1024,
        Price = 3_00,
        IsActive = true,
    };

    public static readonly StorageAddon[] Addons =
    [
        LaraueBoards10Gb,
        LaraueBoards50Gb,
    ];
}
