using Laraue.Apps.Billing.DataAccess.Entities;

namespace Laraue.Apps.Billing.DataAccess.Data;


public static class LaraueBoardsTariffsData
{
    private static readonly TariffSeed<LaraueBoardsPersonalTariff> PersonalFree =
        new(
            new Tariff
            {
                BillingPeriod = BillingPeriod.Month,
                Code = "personal_free",
                Id = TariffId.LaraueBoardsPersonalFree,
                IncludedTokensCount = 0,
                IsActive = true,
                Price = 0,
                Type = TariffType.Personal,
            },
            new LaraueBoardsPersonalTariff
            {
                Id = TariffId.LaraueBoardsPersonalFree,
                LimitFreeTeamOrganizationsCount = 1,
                LimitIssuesPerMonth = 500,
            });

    private static readonly TariffSeed<LaraueBoardsPersonalTariff> PersonalPlus =
        new(
            new Tariff
            {
                BillingPeriod = BillingPeriod.Month,
                Code = "personal_plus",
                Id = TariffId.LaraueBoardsPersonalPlus,
                IncludedTokensCount = 300_000,
                IsActive = true,
                Price = 4_00,
                Type = TariffType.Personal,
            },
            new LaraueBoardsPersonalTariff
            {
                Id = TariffId.LaraueBoardsPersonalPlus,
                LimitFreeTeamOrganizationsCount = null,
                LimitIssuesPerMonth = 50_000,
            });

    private static readonly TariffSeed<LaraueBoardsTeamTariff> TeamFree =
        new(
            new Tariff
            {
                BillingPeriod = BillingPeriod.Month,
                Code = "team_free",
                Id = TariffId.LaraueBoardsTeamFree,
                IncludedTokensCount = 0,
                IsActive = true,
                Price = 0,
                Type = TariffType.Team,
            },
            new LaraueBoardsTeamTariff
            {
                Id = TariffId.LaraueBoardsTeamFree,
                LimitIssuesPerMonth = 500,
            });

    private static readonly TariffSeed<LaraueBoardsTeamTariff> Team =
        new(
            new Tariff
            {
                BillingPeriod = BillingPeriod.Month,
                Code = "team",
                Id = TariffId.LaraueBoardsTeam,
                IncludedTokensCount = 750_000,
                IsActive = true,
                Price = 6_00,
                Type = TariffType.Team,
            },
            new LaraueBoardsTeamTariff
            {
                Id = TariffId.LaraueBoardsTeam,
                LimitIssuesPerMonth = 50_000,
            });

    private static readonly TariffSeed<LaraueBoardsTeamTariff> TeamBusiness =
        new(
            new Tariff
            {
                BillingPeriod = BillingPeriod.Month,
                Code = "team_business",
                Id = TariffId.LaraueBoardsTeamBusiness,
                IncludedTokensCount = 2_500_000,
                IsActive = true,
                Price = 14_00,
                Type = TariffType.Team,
            },
            new LaraueBoardsTeamTariff
            {
                Id = TariffId.LaraueBoardsTeamBusiness,
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
