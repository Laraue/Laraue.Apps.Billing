using System.Globalization;
using Laraue.Apps.Billing.DataAccess;
using Laraue.Apps.Billing.DataAccess.Entities;
using Laraue.Apps.Billing.WebApiServices.Resources;
using Laraue.Core.Exceptions.Web;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Billing.WebApiServices;

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

        var tokenPacks = await GetTokenPacksAsync(currencyRate, cancellationToken);

        return new GetServiceTariffsResponse
        {
            PersonalSubscriptions = personalSubscriptions,
            TeamSubscriptions = teamSubscriptions,
            TokenPacks = tokenPacks,
        };
    }

    private Task<List<PersonalSubscription>> GetPersonalSubscriptionsAsync(
        ServiceId serviceId,
        CurrencyRate currencyRate,
        CancellationToken cancellationToken)
    {
        var tariffIds = serviceId switch
        {
            ServiceId.LaraueBoards => context.LaraueBoardsPersonalTariffs.Select(x => x.Id),
            ServiceId.MarkdownTranslator => context.MarkdownTranslatorPersonalTariffs.Select(x => x.Id),
            _ => throw new BadRequestException(
                nameof(GetServiceTariffsRequest.ServiceId),
                string.Format(Errors.UnknownService, serviceId)),
        };

        return GetSubscriptionsAsync(
            tariffIds,
            currencyRate,
            (id, title, price, formattedPrice, billingPeriod) => new PersonalSubscription
            {
                Id = id,
                Title = title,
                Price = price,
                FormattedPrice = formattedPrice,
                BillingDuration = billingPeriod == BillingPeriod.Forever ? null : 1,
                BillingPeriod = billingPeriod,
            },
            cancellationToken);
    }

    private Task<List<TeamSubscription>> GetTeamSubscriptionsAsync(
        ServiceId serviceId,
        CurrencyRate currencyRate,
        CancellationToken cancellationToken)
    {
        if (serviceId == ServiceId.MarkdownTranslator)
        {
            return Task.FromResult(new List<TeamSubscription>());
        }

        var tariffIds = serviceId switch
        {
            ServiceId.LaraueBoards => context.LaraueBoardsTeamTariffs.Select(x => x.Id),
            _ => throw new BadRequestException(
                nameof(GetServiceTariffsRequest.ServiceId),
                string.Format(Errors.UnknownService, serviceId)),
        };

        return GetSubscriptionsAsync(
            tariffIds,
            currencyRate,
            (id, title, price, formattedPrice, billingPeriod) => new TeamSubscription
            {
                Id = id,
                Title = title,
                Price = price,
                FormattedPrice = formattedPrice,
                BillingDuration = billingPeriod == BillingPeriod.Forever ? null : 1,
                BillingPeriod = billingPeriod,
            },
            cancellationToken);
    }

    private async Task<List<TSubscription>> GetSubscriptionsAsync<TSubscription>(
        IQueryable<Guid> tariffIds,
        CurrencyRate currencyRate,
        Func<Guid, string, decimal, string, BillingPeriod, TSubscription> createSubscription,
        CancellationToken cancellationToken)
        where TSubscription : Subscription
    {
        var tariffs = await context.Tariffs
            .Where(x => x.IsActive)
            .Join(tariffIds, t => t.Id, id => id, (t, _) => new { t.Id, t.Title, t.Price, t.BillingPeriod })
            .ToListAsync(cancellationToken);

        return tariffs
            .Select(x => createSubscription(
                x.Id,
                x.Title,
                ConvertPrice(x.Price, currencyRate),
                FormatPrice(x.Price, currencyRate),
                x.BillingPeriod))
            .OrderBy(x => x.Price)
            .ToList();
    }

    private async Task<List<TokenPack>> GetTokenPacksAsync(CurrencyRate currencyRate, CancellationToken cancellationToken)
    {
        var tokenPacks = await context.TokenPacks
            .Where(x => x.IsActive)
            .Select(x => new { x.Id, x.Title, x.Price, x.TokensCount, x.ExpirationDuration, x.ExpirationPeriod })
            .ToListAsync(cancellationToken);

        return tokenPacks
            .Select(x => new TokenPack
            {
                Id = x.Id,
                Title = x.Title,
                Price = ConvertPrice(x.Price, currencyRate),
                FormattedPrice = FormatPrice(x.Price, currencyRate),
                Amount = x.TokensCount,
                BillingDuration = x.ExpirationDuration,
                BillingPeriod = x.ExpirationPeriod,
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

        return $"{amount.ToString("F" + decimals, CultureInfo.InvariantCulture)} {currencyRate.Code}";
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
    public required IList<TokenPack> TokenPacks { get; set; }
}

public abstract record Tariff
{
    public required string Title { get; set; }
    public decimal Price { get; set; }
    public required string FormattedPrice { get; set; }

    /// <summary>
    /// Null when <see cref="BillingPeriod"/> is <see cref="Entities.BillingPeriod.Forever"/>.
    /// </summary>
    public int? BillingDuration { get; set; }
    public BillingPeriod BillingPeriod { get; set; }
}

public abstract record Subscription : Tariff
{
}

public record PersonalSubscription : Subscription
{
    public required Guid Id { get; set; }
}

public record TeamSubscription : Subscription
{
    public required Guid Id { get; set; }
}

public record TokenPack : Tariff
{
    public required Guid Id { get; set; }
    public long Amount { get; set; }
}
