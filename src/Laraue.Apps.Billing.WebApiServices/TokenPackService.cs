using Laraue.Apps.Billing.DataAccess.Entities;
using Laraue.Apps.Billing.Services;
using Laraue.Apps.Billing.Services.Payments;

namespace Laraue.Apps.Billing.WebApiServices;

public interface ITokenPackService
{
    /// <summary>
    /// Public interface that returns the token packs for sale to the frontend.
    /// </summary>
    Task<GetTokenPacksResponse> GetTokenPacks(GetTokenPacksRequest request, CancellationToken cancellationToken);
}

public class TokenPackService(
    ICoreTariffService coreTariffService,
    IPaymentProviderRegistry paymentProviderRegistry) : ITokenPackService
{
    public async Task<GetTokenPacksResponse> GetTokenPacks(
        GetTokenPacksRequest request,
        CancellationToken cancellationToken)
    {
        // Only currencies a provider can charge in are offered, like for the tariffs.
        var currencyCode = paymentProviderRegistry.ResolveCurrency(request.CurrencyCode);
        var currencyRate = await coreTariffService.GetCurrencyRateAsync(currencyCode, cancellationToken);

        var packs = await coreTariffService.GetTokenPacksAsync(currencyRate, cancellationToken);

        return new GetTokenPacksResponse
        {
            TokenPacks = packs
                .Select(x => new TokenPack
                {
                    Id = x.Id,
                    Code = x.Code,
                    Title = x.Title,
                    TokensCount = x.TokensCount,
                    Price = x.Price,
                    CurrencyCode = x.CurrencyCode,
                    FormattedPrice = x.FormattedPrice,
                    ExpirationDuration = x.ExpirationDuration,
                    ExpirationPeriod = x.ExpirationPeriod,
                })
                .ToList(),
        };
    }
}

public record GetTokenPacksRequest
{
    /// <summary>
    /// The currency to price the packs in. Only currencies the payment provider can charge are
    /// available; omitted, the packs come in the provider's own currency.
    /// </summary>
    public string? CurrencyCode { get; set; }
}

public record GetTokenPacksResponse
{
    public required IList<TokenPack> TokenPacks { get; set; }
}

public record TokenPack
{
    public required Guid Id { get; set; }
    public required string Code { get; set; }
    public required string Title { get; set; }
    public required long TokensCount { get; set; }
    public decimal Price { get; set; }
    public required string CurrencyCode { get; set; }
    public required string FormattedPrice { get; set; }

    /// <summary>
    /// The purchased tokens expire after <see cref="ExpirationDuration"/> of
    /// <see cref="ExpirationPeriod"/>.
    /// </summary>
    public int ExpirationDuration { get; set; }
    public BillingPeriod ExpirationPeriod { get; set; }
}
