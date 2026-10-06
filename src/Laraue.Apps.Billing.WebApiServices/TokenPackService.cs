using Laraue.Apps.Billing.DataAccess;
using Laraue.Apps.Billing.DataAccess.Entities;
using Laraue.Apps.Billing.Services.Payments;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Billing.WebApiServices;

public interface ITokenPackService
{
    /// <summary>
    /// Public interface that returns the token packs for sale to the frontend.
    /// </summary>
    Task<GetTokenPacksResponse> GetTokenPacks(GetTokenPacksRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Reads the token packs for sale, priced in the currency the payment provider charges in.
/// </summary>
public class TokenPackService(
    DatabaseContext context,
    IPaymentProviderRegistry paymentProviderRegistry) : ITokenPackService
{
    public async Task<GetTokenPacksResponse> GetTokenPacks(
        GetTokenPacksRequest request,
        CancellationToken cancellationToken)
    {
        // Only currencies a provider can charge in are offered, like for the tariffs.
        var currencyCode = paymentProviderRegistry.ResolveCurrency(request.CurrencyCode);
        var currencyRate = await context.GetCurrencyRateAsync(currencyCode, cancellationToken);

        var rows = await context.TokenPacks
            .Where(x => x.IsActive)
            .OrderBy(x => x.Price)
            .Select(x => new
            {
                x.Id,
                x.Code,
                x.Title,
                x.TokensCount,
                x.Price,
                x.ExpirationDuration,
                x.ExpirationPeriod,
            })
            .ToListAsync(cancellationToken);

        // The price is converted client-side: the rounding of PriceCalculator is not translatable to SQL.
        return new GetTokenPacksResponse
        {
            TokenPacks = rows
                .Select(x => new TokenPack
                {
                    Id = x.Id,
                    Code = x.Code,
                    Title = x.Title,
                    TokensCount = x.TokensCount,
                    Price = currencyRate.Convert(x.Price),
                    CurrencyCode = currencyRate.Code,
                    FormattedPrice = currencyRate.Format(x.Price),
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
