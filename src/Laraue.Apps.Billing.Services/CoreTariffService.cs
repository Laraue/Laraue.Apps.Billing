using Laraue.Apps.Billing.DataAccess;
using Laraue.Apps.Billing.DataAccess.Entities;
using Laraue.Apps.Billing.Services.Resources;
using Laraue.Core.Exceptions.Web;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Billing.Services;

/// <summary>
/// Tariff logic common to every host (currency lookup, price conversion/formatting, raw tariff
/// data) - no host-facing response shape. Hosts project <see cref="CoreTariff"/> rows (already
/// priced in the requested currency) into their own DTOs in their own <c>Host{Services}</c> layer
/// instead of this project knowing about any of them.
/// </summary>
public interface ICoreTariffService
{
    Task<CurrencyRate> GetCurrencyRateAsync(string currencyCode, CancellationToken cancellationToken);

    Task<IReadOnlyList<CoreTariff>> GetPersonalTariffsAsync(
        ServiceId serviceId,
        CurrencyRate currencyRate,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CoreTariff>> GetTeamTariffsAsync(
        ServiceId serviceId,
        CurrencyRate currencyRate,
        CancellationToken cancellationToken);
}

public class CoreTariffService(DatabaseContext context) : ICoreTariffService
{
    public async Task<CurrencyRate> GetCurrencyRateAsync(string currencyCode, CancellationToken cancellationToken)
    {
        var code = currencyCode.ToUpper();

        var currencyRate = await context.CurrencyRates
            .SingleOrDefaultAsync(x => x.Code == code, cancellationToken);

        if (currencyRate is null)
        {
            throw new BadRequestException(
                nameof(currencyCode),
                string.Format(Errors.CurrencyRateNotFound, code));
        }

        return currencyRate;
    }

    public Task<IReadOnlyList<CoreTariff>> GetPersonalTariffsAsync(
        ServiceId serviceId,
        CurrencyRate currencyRate,
        CancellationToken cancellationToken)
    {
        return serviceId switch
        {
            ServiceId.LaraueBoards => GetLaraueBoardsPersonalTariffsAsync(currencyRate, cancellationToken),
            ServiceId.MarkdownTranslator => GetMarkdownTranslatorPersonalTariffsAsync(currencyRate, cancellationToken),
            _ => throw new BadRequestException(
                nameof(serviceId),
                string.Format(Errors.UnknownService, serviceId)),
        };
    }

    public Task<IReadOnlyList<CoreTariff>> GetTeamTariffsAsync(
        ServiceId serviceId,
        CurrencyRate currencyRate,
        CancellationToken cancellationToken)
    {
        return serviceId switch
        {
            ServiceId.LaraueBoards => GetLaraueBoardsTeamTariffsAsync(currencyRate, cancellationToken),
            ServiceId.MarkdownTranslator => Task.FromResult<IReadOnlyList<CoreTariff>>([]),
            _ => throw new BadRequestException(
                nameof(serviceId),
                string.Format(Errors.UnknownService, serviceId)),
        };
    }

    private async Task<IReadOnlyList<CoreTariff>> GetLaraueBoardsPersonalTariffsAsync(
        CurrencyRate currencyRate,
        CancellationToken cancellationToken)
    {
        var rows = await context.LaraueBoardsPersonalTariffs
            .Where(x => x.Tariff!.IsActive)
            .OrderBy(x => x.Tariff!.Price)
            .Select(x => new
            {
                x.Tariff!.Id,
                x.Tariff.Title,
                x.Tariff.Price,
                x.Tariff.BillingPeriod,
                x.Tariff.IncludedTokensCount,
                x.LimitIssuesPerMonth,
                x.LimitFreeTeamOrganizationsCount,
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(x => (CoreTariff)new CoreLaraueBoardsPersonalTariff
            {
                Id = x.Id,
                Title = x.Title,
                Price = PriceCalculator.ConvertPrice(x.Price, currencyRate.RateToUsd, currencyRate.RoundingStep, currencyRate.RoundingMode),
                CurrencyCode = currencyRate.Code,
                FormattedPrice = PriceCalculator.FormatPrice(x.Price, currencyRate.RateToUsd, currencyRate.RoundingStep, currencyRate.RoundingMode, currencyRate.Symbol),
                BillingDuration = x.BillingPeriod == BillingPeriod.Forever ? null : 1,
                BillingPeriod = x.BillingPeriod,
                IncludedTokensCount = x.IncludedTokensCount,
                LimitIssuesPerMonth = x.LimitIssuesPerMonth,
                LimitFreeTeamOrganizationsCount = x.LimitFreeTeamOrganizationsCount,
            })
            .ToList();
    }

    private async Task<IReadOnlyList<CoreTariff>> GetMarkdownTranslatorPersonalTariffsAsync(
        CurrencyRate currencyRate,
        CancellationToken cancellationToken)
    {
        var rows = await context.MarkdownTranslatorPersonalTariffs
            .Where(x => x.Tariff!.IsActive)
            .OrderBy(x => x.Tariff!.Price)
            .Select(x => new
            {
                x.Tariff!.Id,
                x.Tariff.Title,
                x.Tariff.Price,
                x.Tariff.BillingPeriod,
                x.Tariff.IncludedTokensCount,
                x.IncludedDailyFreeTokensCount,
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(x => (CoreTariff)new CoreMarkdownTranslatorPersonalTariff
            {
                Id = x.Id,
                Title = x.Title,
                Price = PriceCalculator.ConvertPrice(x.Price, currencyRate.RateToUsd, currencyRate.RoundingStep, currencyRate.RoundingMode),
                CurrencyCode = currencyRate.Code,
                FormattedPrice = PriceCalculator.FormatPrice(x.Price, currencyRate.RateToUsd, currencyRate.RoundingStep, currencyRate.RoundingMode, currencyRate.Symbol),
                BillingDuration = x.BillingPeriod == BillingPeriod.Forever ? null : 1,
                BillingPeriod = x.BillingPeriod,
                IncludedTokensCount = x.IncludedTokensCount,
                IncludedDailyFreeTokensCount = x.IncludedDailyFreeTokensCount,
            })
            .ToList();
    }

    private async Task<IReadOnlyList<CoreTariff>> GetLaraueBoardsTeamTariffsAsync(
        CurrencyRate currencyRate,
        CancellationToken cancellationToken)
    {
        var rows = await context.LaraueBoardsTeamTariffs
            .Where(x => x.Tariff!.IsActive)
            .OrderBy(x => x.Tariff!.Price)
            .Select(x => new
            {
                x.Tariff!.Id,
                x.Tariff.Title,
                x.Tariff.Price,
                x.Tariff.BillingPeriod,
                x.Tariff.IncludedTokensCount,
                x.LimitIssuesPerMonth,
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(x => (CoreTariff)new CoreLaraueBoardsTeamTariff
            {
                Id = x.Id,
                Title = x.Title,
                Price = PriceCalculator.ConvertPrice(x.Price, currencyRate.RateToUsd, currencyRate.RoundingStep, currencyRate.RoundingMode),
                CurrencyCode = currencyRate.Code,
                FormattedPrice = PriceCalculator.FormatPrice(x.Price, currencyRate.RateToUsd, currencyRate.RoundingStep, currencyRate.RoundingMode, currencyRate.Symbol),
                BillingDuration = x.BillingPeriod == BillingPeriod.Forever ? null : 1,
                BillingPeriod = x.BillingPeriod,
                IncludedTokensCount = x.IncludedTokensCount,
                LimitIssuesPerMonth = x.LimitIssuesPerMonth,
            })
            .ToList();
    }
}

/// <summary>
/// Tariff data already priced in the requested currency (<see cref="Price"/>/
/// <see cref="FormattedPrice"/> via <see cref="PriceCalculator"/>) - no host-specific response
/// shape beyond that. Already ordered by price ascending.
/// </summary>
public abstract record CoreTariff
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required decimal Price { get; init; }
    public required string CurrencyCode { get; init; }
    public required string FormattedPrice { get; init; }

    /// <summary>
    /// Null when <see cref="BillingPeriod"/> is <see cref="DataAccess.Entities.BillingPeriod.Forever"/>.
    /// </summary>
    public int? BillingDuration { get; init; }
    public required BillingPeriod BillingPeriod { get; init; }
    public required long IncludedTokensCount { get; init; }
}

public sealed record CoreLaraueBoardsPersonalTariff : CoreTariff
{
    public int? LimitIssuesPerMonth { get; init; }
    public int? LimitFreeTeamOrganizationsCount { get; init; }
}

public sealed record CoreMarkdownTranslatorPersonalTariff : CoreTariff
{
    public required long IncludedDailyFreeTokensCount { get; init; }
}

public sealed record CoreLaraueBoardsTeamTariff : CoreTariff
{
    public int? LimitIssuesPerMonth { get; init; }
}
