using Laraue.Apps.Billing.DataAccess.Entities;

namespace Laraue.Apps.Billing.DataAccess.Data;

public static class MarkdownTranslatorTariffsData
{
    private const long IncludedDailyFreeTokensCount = 10_000;

    private static readonly TariffSeed<MarkdownTranslatorPersonalTariff> Free =
        new(
            new Tariff
            {
                BillingPeriod = BillingPeriod.Month,
                Code = "free",
                Id = TariffId.MarkdownTranslatorFree,
                IncludedTokensCount = 0,
                IsActive = true,
                Price = 0,
                Type = TariffType.Personal,
            },
            new MarkdownTranslatorPersonalTariff
            {
                Id = TariffId.MarkdownTranslatorFree,
                IncludedDailyFreeTokensCount = IncludedDailyFreeTokensCount,
            });

    private static readonly TariffSeed<MarkdownTranslatorPersonalTariff> Plus =
        new(
            new Tariff
            {
                BillingPeriod = BillingPeriod.Month,
                Code = "plus",
                Id = TariffId.MarkdownTranslatorPlus,
                IncludedTokensCount = 300_000,
                IsActive = true,
                Price = 4_00,
                Type = TariffType.Personal,
            },
            new MarkdownTranslatorPersonalTariff
            {
                Id = TariffId.MarkdownTranslatorPlus,
                IncludedDailyFreeTokensCount = IncludedDailyFreeTokensCount,
            });

    private static readonly TariffSeed<MarkdownTranslatorPersonalTariff> Pro =
        new(
            new Tariff
            {
                BillingPeriod = BillingPeriod.Month,
                Code = "pro",
                Id = TariffId.MarkdownTranslatorPro,
                IncludedTokensCount = 1_200_000,
                IsActive = true,
                Price = 10_00,
                Type = TariffType.Personal,
            },
            new MarkdownTranslatorPersonalTariff
            {
                Id = TariffId.MarkdownTranslatorPro,
                IncludedDailyFreeTokensCount = IncludedDailyFreeTokensCount,
            });

    public static readonly TariffSeed<MarkdownTranslatorPersonalTariff>[] PersonalTariffs =
    [
        Free,
        Plus,
        Pro,
    ];
}
