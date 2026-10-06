using System.Text.Json.Serialization;
using Laraue.Apps.Billing.DataAccess;
using Laraue.Apps.Billing.DataAccess.Entities;
using Laraue.Apps.Billing.Services;
using Laraue.Apps.Billing.Services.Payments;
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

/// <summary>
/// Reads the tariffs of a service, already priced in the currency the payment provider charges in.
/// The reads live here, in the host layer; core services only hold the domain logic and shared helpers
/// such as <see cref="PriceCalculator"/>.
/// </summary>
public class TariffService(
    DatabaseContext context,
    ICurrencyRateService currencyRateService,
    IPaymentProviderRegistry paymentProviderRegistry) : ITariffService
{
    public async Task<GetServiceTariffsResponse> GetServiceTariffs(
        GetServiceTariffsRequest request,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.ServiceId))
        {
            throw new BadRequestException(
                nameof(request.ServiceId),
                string.Format(Errors.UnknownService, request.ServiceId));
        }

        var currencyCode = paymentProviderRegistry.ResolveCurrency(request.CurrencyCode);
        var currencyRate = await currencyRateService.GetCurrencyRateAsync(currencyCode, cancellationToken);

        var personalTariffs = request.ServiceId switch
        {
            ServiceId.LaraueBoards => await GetLaraueBoardsPersonalTariffsAsync(currencyRate, cancellationToken),
            ServiceId.MarkdownTranslator => await GetMarkdownTranslatorPersonalTariffsAsync(currencyRate, cancellationToken),
            _ => throw new InvalidOperationException($"Personal tariffs of service '{request.ServiceId}' are not mapped."),
        };

        var teamTariffs = request.ServiceId switch
        {
            ServiceId.LaraueBoards => await GetLaraueBoardsTeamTariffsAsync(currencyRate, cancellationToken),
            // Markdown Translator only sells personal plans.
            ServiceId.MarkdownTranslator => [],
            _ => throw new InvalidOperationException($"Team tariffs of service '{request.ServiceId}' are not mapped."),
        };

        return new GetServiceTariffsResponse
        {
            PersonalSubscriptions = personalTariffs,
            TeamSubscriptions = teamTariffs,
        };
    }

    private async Task<List<PersonalSubscription>> GetLaraueBoardsPersonalTariffsAsync(
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

        // The price is converted client-side: the rounding of PriceCalculator is not translatable to SQL.
        return rows
            .Select(x => (PersonalSubscription)new LaraueBoardsPersonalSubscription
            {
                Id = x.Id,
                Title = x.Title,
                Price = currencyRateService.ConvertPrice(currencyRate, x.Price),
                CurrencyCode = currencyRate.Code,
                FormattedPrice = currencyRateService.FormatPrice(currencyRate, x.Price),
                BillingDuration = GetBillingDuration(x.BillingPeriod),
                BillingPeriod = x.BillingPeriod,
                IncludedTokensCount = x.IncludedTokensCount,
                LimitIssuesPerMonth = x.LimitIssuesPerMonth,
                LimitFreeTeamOrganizationsCount = x.LimitFreeTeamOrganizationsCount,
            })
            .ToList();
    }

    private async Task<List<PersonalSubscription>> GetMarkdownTranslatorPersonalTariffsAsync(
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
            .Select(x => (PersonalSubscription)new MarkdownTranslatorPersonalSubscription
            {
                Id = x.Id,
                Title = x.Title,
                Price = currencyRateService.ConvertPrice(currencyRate, x.Price),
                CurrencyCode = currencyRate.Code,
                FormattedPrice = currencyRateService.FormatPrice(currencyRate, x.Price),
                BillingDuration = GetBillingDuration(x.BillingPeriod),
                BillingPeriod = x.BillingPeriod,
                IncludedTokensCount = x.IncludedTokensCount,
                IncludedDailyFreeTokensCount = x.IncludedDailyFreeTokensCount,
            })
            .ToList();
    }

    private async Task<List<TeamSubscription>> GetLaraueBoardsTeamTariffsAsync(
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
            .Select(x => (TeamSubscription)new LaraueBoardsTeamSubscription
            {
                Id = x.Id,
                Title = x.Title,
                Price = currencyRateService.ConvertPrice(currencyRate, x.Price),
                CurrencyCode = currencyRate.Code,
                FormattedPrice = currencyRateService.FormatPrice(currencyRate, x.Price),
                BillingDuration = GetBillingDuration(x.BillingPeriod),
                BillingPeriod = x.BillingPeriod,
                IncludedTokensCount = x.IncludedTokensCount,
                LimitIssuesPerMonth = x.LimitIssuesPerMonth,
            })
            .ToList();
    }

    /// <summary>
    /// Null for a tariff that never expires, one period otherwise.
    /// </summary>
    private static int? GetBillingDuration(BillingPeriod billingPeriod) =>
        billingPeriod == BillingPeriod.Forever ? null : 1;
}

public record GetServiceTariffsRequest
{
    public ServiceId ServiceId { get; set; }

    /// <summary>
    /// The currency to price the tariffs in. Only currencies the payment provider can charge are
    /// available; omitted, the tariffs come in the provider's own currency.
    /// </summary>
    public string? CurrencyCode { get; set; }
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
    /// Null when <see cref="BillingPeriod"/> is <see cref="DataAccess.Entities.BillingPeriod.Forever"/>.
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
