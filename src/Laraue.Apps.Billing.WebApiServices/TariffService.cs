using System.Text.Json.Serialization;
using Laraue.Apps.Billing.DataAccess.Entities;
using Laraue.Apps.Billing.Services;

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

public class TariffService(ICoreTariffService coreTariffService) : ITariffService
{
    public async Task<GetServiceTariffsResponse> GetServiceTariffs(
        GetServiceTariffsRequest request,
        CancellationToken cancellationToken)
    {
        var currencyRate = await coreTariffService.GetCurrencyRateAsync(request.CurrencyCode, cancellationToken);

        var personalTariffs = await coreTariffService.GetPersonalTariffsAsync(request.ServiceId, currencyRate, cancellationToken);
        var teamTariffs = await coreTariffService.GetTeamTariffsAsync(request.ServiceId, currencyRate, cancellationToken);

        return new GetServiceTariffsResponse
        {
            PersonalSubscriptions = personalTariffs.Select(ToPersonalSubscription).ToList(),
            TeamSubscriptions = teamTariffs.Select(ToTeamSubscription).ToList(),
        };
    }

    private static PersonalSubscription ToPersonalSubscription(CoreTariff tariff) => tariff switch
    {
        CoreLaraueBoardsPersonalTariff t => new LaraueBoardsPersonalSubscription
        {
            Id = t.Id,
            Title = t.Title,
            Price = t.Price,
            CurrencyCode = t.CurrencyCode,
            FormattedPrice = t.FormattedPrice,
            BillingDuration = t.BillingDuration,
            BillingPeriod = t.BillingPeriod,
            IncludedTokensCount = t.IncludedTokensCount,
            LimitIssuesPerMonth = t.LimitIssuesPerMonth,
            LimitFreeTeamOrganizationsCount = t.LimitFreeTeamOrganizationsCount,
        },
        CoreMarkdownTranslatorPersonalTariff t => new MarkdownTranslatorPersonalSubscription
        {
            Id = t.Id,
            Title = t.Title,
            Price = t.Price,
            CurrencyCode = t.CurrencyCode,
            FormattedPrice = t.FormattedPrice,
            BillingDuration = t.BillingDuration,
            BillingPeriod = t.BillingPeriod,
            IncludedTokensCount = t.IncludedTokensCount,
            IncludedDailyFreeTokensCount = t.IncludedDailyFreeTokensCount,
        },
        _ => throw new InvalidOperationException($"Unmapped personal tariff type '{tariff.GetType()}'."),
    };

    private static TeamSubscription ToTeamSubscription(CoreTariff tariff) => tariff switch
    {
        CoreLaraueBoardsTeamTariff t => new LaraueBoardsTeamSubscription
        {
            Id = t.Id,
            Title = t.Title,
            Price = t.Price,
            CurrencyCode = t.CurrencyCode,
            FormattedPrice = t.FormattedPrice,
            BillingDuration = t.BillingDuration,
            BillingPeriod = t.BillingPeriod,
            IncludedTokensCount = t.IncludedTokensCount,
            LimitIssuesPerMonth = t.LimitIssuesPerMonth,
        },
        _ => throw new InvalidOperationException($"Unmapped team tariff type '{tariff.GetType()}'."),
    };
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
