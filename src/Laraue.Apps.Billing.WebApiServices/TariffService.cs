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
        var currencyRate = await context.CurrencyRates
            .SingleOrDefaultAsync(x => x.Code == request.CurrencyCode, cancellationToken);

        if (currencyRate is null)
        {
            throw new BadRequestException(
                nameof(request.CurrencyCode),
                string.Format(Errors.CurrencyRateNotFound, request.CurrencyCode));
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
            (price, formattedPrice, billingPeriod) => new PersonalSubscription
            {
                Price = price,
                FormattedPrice = formattedPrice,
                Duration = 1,
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
            (price, formattedPrice, billingPeriod) => new TeamSubscription
            {
                Price = price,
                FormattedPrice = formattedPrice,
                Duration = 1,
                BillingPeriod = billingPeriod,
            },
            cancellationToken);
    }

    private async Task<List<TSubscription>> GetSubscriptionsAsync<TSubscription>(
        IQueryable<TariffId> tariffIds,
        CurrencyRate currencyRate,
        Func<decimal, string, BillingPeriod, TSubscription> createSubscription,
        CancellationToken cancellationToken)
        where TSubscription : Subscription
    {
        var tariffs = await context.Tariffs
            .Where(x => x.IsActive)
            .Join(tariffIds, t => t.Id, id => id, (t, _) => new { t.Price, t.BillingPeriod })
            .ToListAsync(cancellationToken);

        return tariffs
            .Select(x => createSubscription(
                ConvertPrice(x.Price, currencyRate),
                FormatPrice(x.Price, currencyRate),
                x.BillingPeriod))
            .ToList();
    }

    private async Task<List<TokenPack>> GetTokenPacksAsync(CurrencyRate currencyRate, CancellationToken cancellationToken)
    {
        var tokenPacks = await context.TokenPacks
            .Where(x => x.IsActive)
            .Select(x => new { x.Price, x.TokensCount, x.ExpirationDuration })
            .ToListAsync(cancellationToken);

        return tokenPacks
            .Select(x => new TokenPack
            {
                Price = ConvertPrice(x.Price, currencyRate),
                FormattedPrice = FormatPrice(x.Price, currencyRate),
                Amount = x.TokensCount,
                ExpirationDuration = x.ExpirationDuration,
            })
            .ToList();
    }

    private static decimal ConvertPrice(int priceInUsdCents, CurrencyRate currencyRate) =>
        priceInUsdCents / 100m / currencyRate.RateToUsd;

    private static string FormatPrice(int priceInUsdCents, CurrencyRate currencyRate)
    {
        var amount = ConvertPrice(priceInUsdCents, currencyRate);

        return $"{amount:0.00} {currencyRate.Code}";
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
    public int Duration { get; set; }
    public BillingPeriod BillingPeriod { get; set; }
}

public record PersonalSubscription : Subscription
{
}

public record TeamSubscription : Subscription
{
}

public record TokenPack : Tariff
{
    public long Amount { get; set; }
    public TimeSpan ExpirationDuration { get; set; }
}
