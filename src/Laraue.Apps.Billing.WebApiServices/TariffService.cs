using Laraue.Apps.Billing.DataAccess.Entities;

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

public record PersonalSubscription : Tariff
{
}

public record TeamSubscription : Tariff
{
}

public record TokenPack : Tariff
{
}