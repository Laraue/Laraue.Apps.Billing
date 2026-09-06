using Laraue.Apps.Billing.DataAccess.Entities;

namespace Laraue.Apps.Billing.DataAccess.Data;

public static class MarkdownTranslatorTariffsData
{
    private const long IncludedDailyFreeTokensCount = 10_000;

    private static readonly Guid FreeId = new("33c1fcec-e6ed-47eb-a64c-27e3deb41038");
    private static readonly Guid PlusId = new("67307208-94fa-4da8-8ad4-d6902ba2a1a5");
    private static readonly Guid ProId = new("7aa60cba-ee52-4150-922d-2ea9b6c7aeb5");

    private static readonly TariffSeed<MarkdownTranslatorPersonalTariff> Free =
        new(
            new Tariff
            {
                BillingPeriod = BillingPeriod.Forever,
                Title = "Free",
                Id = FreeId,
                IncludedTokensCount = 0,
                IsActive = true,
                Price = 0,
                Type = TariffType.Personal,
            },
            new MarkdownTranslatorPersonalTariff
            {
                Id = FreeId,
                IncludedDailyFreeTokensCount = IncludedDailyFreeTokensCount,
            });

    private static readonly TariffSeed<MarkdownTranslatorPersonalTariff> Plus =
        new(
            new Tariff
            {
                BillingPeriod = BillingPeriod.Month,
                Title = "Plus",
                Id = PlusId,
                IncludedTokensCount = 300_000,
                IsActive = true,
                Price = 4_00,
                Type = TariffType.Personal,
            },
            new MarkdownTranslatorPersonalTariff
            {
                Id = PlusId,
                IncludedDailyFreeTokensCount = IncludedDailyFreeTokensCount,
            });

    private static readonly TariffSeed<MarkdownTranslatorPersonalTariff> Pro =
        new(
            new Tariff
            {
                BillingPeriod = BillingPeriod.Month,
                Title = "Pro",
                Id = ProId,
                IncludedTokensCount = 1_200_000,
                IsActive = true,
                Price = 10_00,
                Type = TariffType.Personal,
            },
            new MarkdownTranslatorPersonalTariff
            {
                Id = ProId,
                IncludedDailyFreeTokensCount = IncludedDailyFreeTokensCount,
            });

    public static readonly TariffSeed<MarkdownTranslatorPersonalTariff>[] PersonalTariffs =
    [
        Free,
        Plus,
        Pro,
    ];
}
