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
            (id, price, formattedPrice, billingPeriod) => new PersonalSubscription
            {
                Id = id,
                Price = price,
                FormattedPrice = formattedPrice,
                BillingDuration = 1,
                BillingPeriod = billingPeriod,
            },
            cancellationToken);
    }

    private Task<List<TeamSubscription>> GetTeamSubscriptionsAsync(
        ServiceId serviceId,
        CurrencyRate currencyRate,
        CancellationToken cancellationToken)
    {
        var tariffIds = serviceId switch
        {
            ServiceId.LaraueBoards => context.LaraueBoardsTeamTariffs.Select(x => x.Id),
            ServiceId.MarkdownTranslator => Enumerable.Empty<TariffId>().AsQueryable(),
            _ => throw new BadRequestException(
                nameof(GetServiceTariffsRequest.ServiceId),
                string.Format(Errors.UnknownService, serviceId)),
        };

        return GetSubscriptionsAsync(
            tariffIds,
            currencyRate,
            (id, price, formattedPrice, billingPeriod) => new TeamSubscription
            {
                Id = id,
                Price = price,
                FormattedPrice = formattedPrice,
                BillingDuration = 1,
                BillingPeriod = billingPeriod,
            },
            cancellationToken);
    }

    private async Task<List<TSubscription>> GetSubscriptionsAsync<TSubscription>(
        IQueryable<TariffId> tariffIds,
        CurrencyRate currencyRate,
        Func<TariffId, decimal, string, BillingPeriod, TSubscription> createSubscription,
        CancellationToken cancellationToken)
        where TSubscription : Subscription
    {
        var tariffs = await context.Tariffs
            .Where(x => x.IsActive)
            .Join(tariffIds, t => t.Id, id => id, (t, _) => new { t.Id, t.Price, t.BillingPeriod })
            .ToListAsync(cancellationToken);

        return tariffs
            .Select(x => createSubscription(
                x.Id,
                ConvertPrice(x.Price, currencyRate),
                FormatPrice(x.Price, currencyRate),
                x.BillingPeriod))
            .OrderByDescending(x => x.Price)
            .ToList();
    }

    private async Task<List<TokenPack>> GetTokenPacksAsync(CurrencyRate currencyRate, CancellationToken cancellationToken)
    {
        var tokenPacks = await context.TokenPacks
            .Where(x => x.IsActive)
            .Select(x => new { x.Id, x.Price, x.TokensCount, x.ExpirationDuration })
            .ToListAsync(cancellationToken);

        return tokenPacks
            .Select(x => new TokenPack
            {
                Id = x.Id,
                Price = ConvertPrice(x.Price, currencyRate),
                FormattedPrice = FormatPrice(x.Price, currencyRate),
                Amount = x.TokensCount,
                ExpirationDuration = x.ExpirationDuration,
            })
            .OrderByDescending(x => x.Price)
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
    public decimal Price { get; set; }
    public required string FormattedPrice { get; set; }
}

public abstract record Subscription : Tariff
{
    public int BillingDuration { get; set; }
    public BillingPeriod BillingPeriod { get; set; }
}

public record PersonalSubscription : Subscription
{
    public required TariffId Id { get; set; }
}

public record TeamSubscription : Subscription
{
    public required TariffId Id { get; set; }
}

public record TokenPack : Tariff
{
    public required Guid Id { get; set; }
    public long Amount { get; set; }
    public TimeSpan ExpirationDuration { get; set; }
}
