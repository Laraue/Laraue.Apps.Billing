using Laraue.Apps.Billing.DataAccess.Entities;

namespace Laraue.Apps.Billing.DataAccess.Data;


public static class LaraueBoardsTariffsData
{
    private static readonly Guid PersonalFreeId = new("bd5f3457-601d-4ef1-92b2-47353f6b5a8f");
    private static readonly Guid PersonalPlusId = new("e8e4b409-366d-4803-b116-76c1a4a6c8f1");
    private static readonly Guid TeamFreeId = new("d42ebf59-008f-4a1e-8a00-c00f27331e86");
    private static readonly Guid TeamId = new("89111f8d-292b-4f04-8766-b521e19e6964");
    private static readonly Guid TeamBusinessId = new("bb78563f-631c-4f96-afdd-c35ab02b077e");

    private static readonly TariffSeed<LaraueBoardsPersonalTariff> PersonalFree =
        new(
            new Tariff
            {
                BillingPeriod = BillingPeriod.Forever,
                Title = "Free",
                Id = PersonalFreeId,
                IncludedTokensCount = 0,
                IsActive = true,
                Price = 0,
                Type = TariffType.Personal,
            },
            new LaraueBoardsPersonalTariff
            {
                Id = PersonalFreeId,
                LimitFreeTeamOrganizationsCount = 1,
                LimitIssuesPerMonth = 500,
            });

    private static readonly TariffSeed<LaraueBoardsPersonalTariff> PersonalPlus =
        new(
            new Tariff
            {
                BillingPeriod = BillingPeriod.Month,
                Title = "Plus",
                Id = PersonalPlusId,
                IncludedTokensCount = 300_000,
                IsActive = true,
                Price = 4_00,
                Type = TariffType.Personal,
            },
            new LaraueBoardsPersonalTariff
            {
                Id = PersonalPlusId,
                LimitFreeTeamOrganizationsCount = null,
                LimitIssuesPerMonth = 50_000,
            });

    private static readonly TariffSeed<LaraueBoardsTeamTariff> TeamFree =
        new(
            new Tariff
            {
                BillingPeriod = BillingPeriod.Forever,
                Title = "Free",
                Id = TeamFreeId,
                IncludedTokensCount = 0,
                IsActive = true,
                Price = 0,
                Type = TariffType.Team,
            },
            new LaraueBoardsTeamTariff
            {
                Id = TeamFreeId,
                LimitIssuesPerMonth = 500,
            });

    private static readonly TariffSeed<LaraueBoardsTeamTariff> Team =
        new(
            new Tariff
            {
                BillingPeriod = BillingPeriod.Month,
                Title = "Team",
                Id = TeamId,
                IncludedTokensCount = 750_000,
                IsActive = true,
                Price = 6_00,
                Type = TariffType.Team,
            },
            new LaraueBoardsTeamTariff
            {
                Id = TeamId,
                LimitIssuesPerMonth = 50_000,
            });

    private static readonly TariffSeed<LaraueBoardsTeamTariff> TeamBusiness =
        new(
            new Tariff
            {
                BillingPeriod = BillingPeriod.Month,
                Title = "Business",
                Id = TeamBusinessId,
                IncludedTokensCount = 2_500_000,
                IsActive = true,
                Price = 14_00,
                Type = TariffType.Team,
            },
            new LaraueBoardsTeamTariff
            {
                Id = TeamBusinessId,
                LimitIssuesPerMonth = 200_000,
            });

    public static readonly TariffSeed<LaraueBoardsTeamTariff>[] TeamTariffs =
    [
        TeamFree,
        Team,
        TeamBusiness
    ];


    public static readonly TariffSeed<LaraueBoardsPersonalTariff>[] PersonalTariffs =
    [
        PersonalFree,
        PersonalPlus,
    ];
}
