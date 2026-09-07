using System.Globalization;
using System.Text.Json.Serialization;
using Laraue.Apps.Billing.DataAccess;
using Laraue.Apps.Billing.DataAccess.Entities;
using Laraue.Apps.Billing.Services.Resources;
using Laraue.Core.Exceptions.Web;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Billing.Services;

public interface ITariffService
{
    /// <summary>
    /// Public interface that return Laraue Tariffs for frontend.
    /// </summary>
    Task<GetServiceTariffsResponse> GetServiceTariffs(
        GetServiceTariffsRequest request,
        CancellationToken cancellationToken);
}

public class TariffService(DatabaseContext context) : ITariffService
{
    public async Task<GetServiceTariffsResponse> GetServiceTariffs(
        GetServiceTariffsRequest request,
        CancellationToken cancellationToken)
    {
        var currencyCode = request.CurrencyCode.ToUpper();

        var currencyRate = await context.CurrencyRates
            .SingleOrDefaultAsync(x => x.Code == currencyCode, cancellationToken);

        if (currencyRate is null)
        {
            throw new BadRequestException(
                nameof(request.CurrencyCode),
                string.Format(Errors.CurrencyRateNotFound, currencyCode));
        }

        var personalSubscriptions = await GetPersonalSubscriptionsAsync(request.ServiceId, currencyRate, cancellationToken);
        var teamSubscriptions = await GetTeamSubscriptionsAsync(request.ServiceId, currencyRate, cancellationToken);

        return new GetServiceTariffsResponse
        {
            PersonalSubscriptions = personalSubscriptions,
            TeamSubscriptions = teamSubscriptions,
        };
    }

    private Task<List<PersonalSubscription>> GetPersonalSubscriptionsAsync(
        ServiceId serviceId,
        CurrencyRate currencyRate,
        CancellationToken cancellationToken)
    {
        return serviceId switch
        {
            ServiceId.LaraueBoards => GetLaraueBoardsPersonalSubscriptionsAsync(currencyRate, cancellationToken),
            ServiceId.MarkdownTranslator => GetMarkdownTranslatorPersonalSubscriptionsAsync(currencyRate, cancellationToken),
            _ => throw new BadRequestException(
                nameof(GetServiceTariffsRequest.ServiceId),
                string.Format(Errors.UnknownService, serviceId)),
        };
    }

    private Task<List<TeamSubscription>> GetTeamSubscriptionsAsync(
        ServiceId serviceId,
        CurrencyRate currencyRate,
        CancellationToken cancellationToken)
    {
        return serviceId switch
        {
            ServiceId.LaraueBoards => GetLaraueBoardsTeamSubscriptionsAsync(currencyRate, cancellationToken),
            ServiceId.MarkdownTranslator => Task.FromResult(new List<TeamSubscription>()),
            _ => throw new BadRequestException(
                nameof(GetServiceTariffsRequest.ServiceId),
                string.Format(Errors.UnknownService, serviceId)),
        };
    }

    private async Task<List<PersonalSubscription>> GetLaraueBoardsPersonalSubscriptionsAsync(
        CurrencyRate currencyRate,
        CancellationToken cancellationToken)
    {
        var rows = await context.LaraueBoardsPersonalTariffs
            .Where(x => x.Tariff!.IsActive)
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
            .Select(x => (PersonalSubscription)new LaraueBoardsPersonalSubscription
            {
                Id = x.Id,
                Title = x.Title,
                Price = ConvertPrice(x.Price, currencyRate),
                CurrencyCode = currencyRate.Code,
                FormattedPrice = FormatPrice(x.Price, currencyRate),
                BillingDuration = x.BillingPeriod == BillingPeriod.Forever ? null : 1,
                BillingPeriod = x.BillingPeriod,
                IncludedTokensCount = x.IncludedTokensCount,
                LimitIssuesPerMonth = x.LimitIssuesPerMonth,
                LimitFreeTeamOrganizationsCount = x.LimitFreeTeamOrganizationsCount,
            })
            .OrderBy(x => x.Price)
            .ToList();
    }

    private async Task<List<PersonalSubscription>> GetMarkdownTranslatorPersonalSubscriptionsAsync(
        CurrencyRate currencyRate,
        CancellationToken cancellationToken)
    {
        var rows = await context.MarkdownTranslatorPersonalTariffs
            .Where(x => x.Tariff!.IsActive)
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
            .Select(x => (PersonalSubscription)new MarkdownTranslatorPersonalSubscription
            {
                Id = x.Id,
                Title = x.Title,
                Price = ConvertPrice(x.Price, currencyRate),
                CurrencyCode = currencyRate.Code,
                FormattedPrice = FormatPrice(x.Price, currencyRate),
                BillingDuration = x.BillingPeriod == BillingPeriod.Forever ? null : 1,
                BillingPeriod = x.BillingPeriod,
                IncludedTokensCount = x.IncludedTokensCount,
                IncludedDailyFreeTokensCount = x.IncludedDailyFreeTokensCount,
            })
            .OrderBy(x => x.Price)
            .ToList();
    }

    private async Task<List<TeamSubscription>> GetLaraueBoardsTeamSubscriptionsAsync(
        CurrencyRate currencyRate,
        CancellationToken cancellationToken)
    {
        var rows = await context.LaraueBoardsTeamTariffs
            .Where(x => x.Tariff!.IsActive)
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
            .Select(x => (TeamSubscription)new LaraueBoardsTeamSubscription
            {
                Id = x.Id,
                Title = x.Title,
                Price = ConvertPrice(x.Price, currencyRate),
                CurrencyCode = currencyRate.Code,
                FormattedPrice = FormatPrice(x.Price, currencyRate),
                BillingDuration = x.BillingPeriod == BillingPeriod.Forever ? null : 1,
                BillingPeriod = x.BillingPeriod,
                IncludedTokensCount = x.IncludedTokensCount,
                LimitIssuesPerMonth = x.LimitIssuesPerMonth,
            })
            .OrderBy(x => x.Price)
            .ToList();
    }

    private static decimal ConvertPrice(int priceInUsdCents, CurrencyRate currencyRate)
    {
        var amount = priceInUsdCents / 100m / currencyRate.RateToUsd;

        return RoundPrice(amount, currencyRate);
    }

    private static decimal RoundPrice(decimal amount, CurrencyRate currencyRate)
    {
        var step = currencyRate.RoundingStep;
        var steps = amount / step;

        var roundedSteps = currencyRate.RoundingMode switch
        {
            RoundingMode.Up => Math.Ceiling(steps),
            RoundingMode.Down => Math.Floor(steps),
            _ => Math.Round(steps, MidpointRounding.AwayFromZero),
        };

        return roundedSteps * step;
    }

    private static string FormatPrice(int priceInUsdCents, CurrencyRate currencyRate)
    {
        var amount = ConvertPrice(priceInUsdCents, currencyRate);
        var decimals = GetDecimalPlaces(currencyRate.RoundingStep);
        var format = decimals > 0 ? "0." + new string('#', decimals) : "0";

        return $"{amount.ToString(format, CultureInfo.InvariantCulture)}{currencyRate.Symbol}";
    }

    private static int GetDecimalPlaces(decimal step)
    {
        step = Math.Abs(step);

        var decimals = 0;
        while (step != Math.Floor(step) && decimals < 10)
        {
            step *= 10;
            decimals++;
        }

        return decimals;
    }
}

public record GetServiceTariffsRequest
{
    public ServiceId ServiceId { get; set; }
    public required string CurrencyCode { get; set; }
}

public record GetServiceTariffsResponse
{
    public required IList<PersonalSubscription> PersonalSubscriptions { get; set; }
    public required IList<TeamSubscription> TeamSubscriptions { get; set; }
}

public abstract record Subscription
{
    public required string Title { get; set; }
    public decimal Price { get; set; }
    public required string CurrencyCode { get; set; }
    public required string FormattedPrice { get; set; }

    /// <summary>
    /// Null when <see cref="BillingPeriod"/> is <see cref="Entities.BillingPeriod.Forever"/>.
    /// </summary>
    public int? BillingDuration { get; set; }
    public BillingPeriod BillingPeriod { get; set; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(LaraueBoardsPersonalSubscription), "LaraueBoardsPersonal")]
[JsonDerivedType(typeof(MarkdownTranslatorPersonalSubscription), "MarkdownTranslatorPersonal")]
public abstract record PersonalSubscription : Subscription
{
    public required Guid Id { get; set; }
}

public sealed record LaraueBoardsPersonalSubscription : PersonalSubscription
{
    public required long IncludedTokensCount { get; set; }
    public int? LimitIssuesPerMonth { get; set; }
    public int? LimitFreeTeamOrganizationsCount { get; set; }
}

public sealed record MarkdownTranslatorPersonalSubscription : PersonalSubscription
{
    public required long IncludedTokensCount { get; set; }
    public required long IncludedDailyFreeTokensCount { get; set; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(LaraueBoardsTeamSubscription), "LaraueBoardsTeam")]
public abstract record TeamSubscription : Subscription
{
    public required Guid Id { get; set; }
}

public sealed record LaraueBoardsTeamSubscription : TeamSubscription
{
    public required long IncludedTokensCount { get; set; }
    public int? LimitIssuesPerMonth { get; set; }
}
