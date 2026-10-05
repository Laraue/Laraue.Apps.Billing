using Laraue.Apps.Billing.DataAccess;
using Laraue.Apps.Billing.DataAccess.Entities;
using Laraue.Apps.Billing.Services.Resources;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Core.Exceptions.Web;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Billing.Services.Payments;

/// <summary>
/// Creates payments and handles the providers' notifications. Provider-agnostic: everything that
/// depends on a specific provider sits behind <see cref="IPaymentProvider"/>.
/// </summary>
public interface ICorePaymentService
{
    /// <summary>
    /// Records a pending payment and asks the provider for the page the customer pays on. The price
    /// is always taken from our own tariffs and packs, never from the caller.
    /// </summary>
    Task<PaymentCheckout> CreateAsync(CreatePaymentRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Handles a notification of the given provider. Safe to call repeatedly for the same payment:
    /// a payment is fulfilled only once, a repeated notification just gets the acknowledgement again.
    /// </summary>
    Task<PaymentNotificationResult> HandleNotificationAsync(
        string providerCode,
        PaymentNotificationRequest request,
        CancellationToken cancellationToken);
}

public sealed record CreatePaymentRequest
{
    public required ServiceId ServiceId { get; init; }

    public required PaymentKind Kind { get; init; }

    /// <summary>
    /// The tariff id for <see cref="PaymentKind.Subscription"/>, the token pack id for
    /// <see cref="PaymentKind.TokenPack"/>.
    /// </summary>
    public required Guid ItemId { get; init; }

    /// <summary>
    /// Who is covered, a user or an organization.
    /// </summary>
    public required Guid PaidEntityId { get; init; }

    /// <summary>
    /// <see langword="true"/> when <see cref="PaidEntityId"/> is an organization (team tariffs),
    /// <see langword="false"/> for a user (personal tariffs).
    /// </summary>
    public required bool IsOrganization { get; init; }

    /// <summary>
    /// Who is paying.
    /// </summary>
    public required Guid OwnerId { get; init; }

    public required string CurrencyCode { get; init; }

    /// <summary>
    /// A provider to use instead of the default one.
    /// </summary>
    public string? ProviderCode { get; init; }

    public string? ReturnUrl { get; init; }
}

public sealed record PaymentCheckout(Guid PaymentId, string Url);

public sealed record PaymentNotificationResult(string Acknowledgement);

public class CorePaymentService(
    DatabaseContext context,
    ICoreTariffService coreTariffService,
    IPaymentProviderRegistry providerRegistry,
    IPaymentFulfillment fulfillment,
    IDateTimeProvider dateTimeProvider) : ICorePaymentService
{
    public async Task<PaymentCheckout> CreateAsync(CreatePaymentRequest request, CancellationToken cancellationToken)
    {
        var provider = request.ProviderCode is null
            ? providerRegistry.Default
            : providerRegistry.Get(request.ProviderCode);

        var currencyCode = request.CurrencyCode.ToUpperInvariant();
        if (!provider.SupportedCurrencies.Contains(currencyCode))
        {
            throw new BadRequestException(
                nameof(request.CurrencyCode),
                string.Format(Errors.PaymentProviderCurrencyNotSupported, provider.Code, currencyCode));
        }

        var item = await GetItemAsync(request, cancellationToken);
        var currencyRate = await coreTariffService.GetCurrencyRateAsync(currencyCode, cancellationToken);

        var amount = PriceCalculator.ConvertPrice(
            item.PriceInUsdCents,
            currencyRate.RateToUsd,
            currencyRate.RoundingStep,
            currencyRate.RoundingMode);

        // Both supported currencies have two decimal places.
        var amountMinorUnits = (long)decimal.Round(amount * 100, 0, MidpointRounding.AwayFromZero);
        if (amountMinorUnits <= 0)
        {
            throw new BadRequestException(
                nameof(request.ItemId),
                string.Format(Errors.PaymentItemNotFound, request.ItemId));
        }

        var paymentId = Guid.NewGuid();

        var checkout = await provider.CreateCheckoutAsync(
            new PaymentCheckoutRequest
            {
                PaymentId = paymentId,
                AmountMinorUnits = amountMinorUnits,
                CurrencyCode = currencyCode,
                Description = item.Title,
                ReturnUrl = request.ReturnUrl,
            },
            cancellationToken);

        context.Payments.Add(new Payment
        {
            Id = paymentId,
            ServiceId = request.ServiceId,
            Kind = request.Kind,
            PaidEntityId = request.PaidEntityId,
            OwnerId = request.OwnerId,
            TariffId = request.Kind == PaymentKind.Subscription ? request.ItemId : null,
            TokenPackId = request.Kind == PaymentKind.TokenPack ? request.ItemId : null,
            AmountMinorUnits = amountMinorUnits,
            CurrencyCode = currencyCode,
            Status = PaymentStatus.Pending,
            Provider = provider.Code,
            ProviderPaymentId = checkout.ProviderPaymentId,
            ProviderData = checkout.ProviderData,
            CreatedAt = dateTimeProvider.UtcNow,
        });

        await context.SaveChangesAsync(cancellationToken);

        return new PaymentCheckout(paymentId, checkout.Url);
    }

    public async Task<PaymentNotificationResult> HandleNotificationAsync(
        string providerCode,
        PaymentNotificationRequest request,
        CancellationToken cancellationToken)
    {
        var provider = providerRegistry.Get(providerCode);
        var notification = provider.ParseNotification(request);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var paymentId = await FindPaymentIdAsync(provider.Code, notification, cancellationToken);

        // Two deliveries of the same notification must not fulfil the payment twice.
        await context.Database.PgAdvisoryXactLock(paymentId.ToString(), cancellationToken);

        var payment = await context.Payments.SingleAsync(x => x.Id == paymentId, cancellationToken);
        var acknowledgement = new PaymentNotificationResult(provider.CreateNotificationAck(notification));

        if (payment.Status == PaymentStatus.Paid)
        {
            return acknowledgement;
        }

        if (notification.Outcome == PaymentNotificationOutcome.Paid)
        {
            EnsureNotificationMatchesPayment(payment, notification);

            await fulfillment.FulfillAsync(payment, cancellationToken);

            payment.Status = PaymentStatus.Paid;
            payment.PaidAt = dateTimeProvider.UtcNow;
        }
        else if (payment.Status == PaymentStatus.Pending)
        {
            payment.Status = notification.Outcome == PaymentNotificationOutcome.Failed
                ? PaymentStatus.Failed
                : PaymentStatus.Canceled;
        }

        payment.ProviderPaymentId ??= notification.ProviderPaymentId;
        payment.ProviderData = notification.ProviderData ?? payment.ProviderData;

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return acknowledgement;
    }

    private async Task<Guid> FindPaymentIdAsync(
        string providerCode,
        PaymentNotification notification,
        CancellationToken cancellationToken)
    {
        var query = context.Payments.Where(x => x.Provider == providerCode);

        if (notification.PaymentId is { } id)
        {
            query = query.Where(x => x.Id == id);
        }
        else if (notification.ProviderPaymentId is { } providerPaymentId)
        {
            query = query.Where(x => x.ProviderPaymentId == providerPaymentId);
        }
        else
        {
            throw new BadRequestException(
                nameof(notification),
                Errors.PaymentNotificationWithoutReference);
        }

        var paymentId = await query
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);

        return paymentId
            ?? throw new NotFoundException(
                string.Format(Errors.PaymentNotFound, notification.PaymentId?.ToString() ?? notification.ProviderPaymentId));
    }

    private static void EnsureNotificationMatchesPayment(Payment payment, PaymentNotification notification)
    {
        var amountMatches = notification.AmountMinorUnits is null
            || notification.AmountMinorUnits == payment.AmountMinorUnits;

        var currencyMatches = notification.CurrencyCode is null
            || string.Equals(notification.CurrencyCode, payment.CurrencyCode, StringComparison.OrdinalIgnoreCase);

        if (!amountMatches || !currencyMatches)
        {
            throw new BadRequestException(
                nameof(notification),
                string.Format(Errors.PaymentNotificationMismatch, payment.Id));
        }
    }

    private async Task<PaymentItem> GetItemAsync(CreatePaymentRequest request, CancellationToken cancellationToken)
    {
        var item = request.Kind switch
        {
            PaymentKind.Subscription => await GetTariffItemAsync(request, cancellationToken),
            PaymentKind.TokenPack => await context.TokenPacks
                .Where(x => x.Id == request.ItemId && x.IsActive)
                .Select(x => new PaymentItem(x.Title, x.Price))
                .SingleOrDefaultAsync(cancellationToken),
            _ => null,
        };

        return item
            ?? throw new BadRequestException(
                nameof(request.ItemId),
                string.Format(Errors.PaymentItemNotFound, request.ItemId));
    }

    /// <summary>
    /// Only an active, paid tariff of the requested service and kind (personal or team) can be bought.
    /// </summary>
    private async Task<PaymentItem?> GetTariffItemAsync(CreatePaymentRequest request, CancellationToken cancellationToken)
    {
        var belongsToService = request.ServiceId switch
        {
            ServiceId.LaraueBoards when request.IsOrganization => await context.LaraueBoardsTeamTariffs
                .AnyAsync(x => x.Id == request.ItemId, cancellationToken),
            ServiceId.LaraueBoards => await context.LaraueBoardsPersonalTariffs
                .AnyAsync(x => x.Id == request.ItemId, cancellationToken),
            ServiceId.MarkdownTranslator when !request.IsOrganization => await context.MarkdownTranslatorPersonalTariffs
                .AnyAsync(x => x.Id == request.ItemId, cancellationToken),
            _ => false,
        };

        if (!belongsToService)
        {
            return null;
        }

        return await context.Tariffs
            .Where(x => x.Id == request.ItemId && x.IsActive && !x.IsFree)
            .Select(x => new PaymentItem(x.Title, x.Price))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private sealed record PaymentItem(string Title, int PriceInUsdCents);
}
