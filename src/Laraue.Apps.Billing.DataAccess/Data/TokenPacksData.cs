using Laraue.Apps.Billing.DataAccess.Entities;

namespace Laraue.Apps.Billing.DataAccess.Data;

public static class TokenPacksData
{
    private static readonly TimeSpan HalfYearExpirationDuration = TimeSpan.FromDays(182.5);

    private static readonly TokenPack Small = new()
    {
        Id = new Guid("76ABD692-C450-4CD6-80E4-B9C012D91610"),
        IsActive = true,
        Code = "small",
        Price = 3_00,
        TokensCount = 100_000,
        ExpirationDuration = HalfYearExpirationDuration,
    };

    private static readonly TokenPack Medium = new()
    {
        Id = new Guid("BB76C6C6-41B9-4546-972A-A9730456FDF0"),
        IsActive = true,
        Code = "medium",
        Price = 12_00,
        TokensCount = 600_000,
        ExpirationDuration = HalfYearExpirationDuration,
    };

    private static readonly TokenPack Large = new()
    {
        Id = new Guid("2A840FE4-175A-4E69-834E-5F5F6C5E2150"),
        IsActive = true,
        Code = "large",
        Price = 40_00,
        TokensCount = 3_000_000,
        ExpirationDuration = HalfYearExpirationDuration,
    };

    public static readonly TokenPack[] Packs =
    [
        Small,
        Medium,
        Large,
    ];
}