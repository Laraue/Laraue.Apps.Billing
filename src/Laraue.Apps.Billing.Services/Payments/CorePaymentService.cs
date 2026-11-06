using Laraue.Apps.Billing.DataAccess;
using Laraue.Apps.Billing.DataAccess.Entities;
using Laraue.Apps.Billing.Services.Metrics;
using Laraue.Apps.Billing.Services.Resources;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Core.Exceptions.Web;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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
    /// Needs a database transaction started by the caller, which commits it: the changes are saved here
    /// but only become final with that commit.
    /// </summary>
    Task<PaymentNotificationResult> HandleNotificationAsync(
        string providerCode,
        PaymentNotificationRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// The service a customer returning from the provider paid for, found from the parameters of the
    /// return address. Null when the payment cannot be identified. The result only chooses where to
    /// send the customer, it says nothing about whether the payment succeeded.
    /// </summary>
    Task<ServiceId?> FindReturnedPaymentServiceAsync(
        string providerCode,
        IReadOnlyDictionary<string, string> parameters,
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

    /// <summary>
    /// A storage add-on bought with a <see cref="PaymentKind.Subscription"/> tariff, charged in the same
    /// payment. Null for none.
    /// </summary>
    public Guid? StorageAddonId { get; init; }

    /// <summary>
    /// The covered entity's current storage usage, when the calling service knows it. A checkout that
    /// would give less storage than this is refused with <see cref="StorageOverLimitException"/>.
    /// </summary>
    public long? StorageUsageBytes { get; init; }
}

public sealed record PaymentCheckout(Guid PaymentId, string Url);

public sealed record PaymentNotificationResult(string Acknowledgement);

public class CorePaymentService(
    DatabaseContext context,
    IPaymentProviderRegistry providerRegistry,
    IPaymentFulfillment fulfillment,
    IDateTimeProvider dateTimeProvider,
    BillingMetrics metrics,
    ILogger<CorePaymentService> logger) : ICorePaymentService
{
    public async Task<PaymentCheckout> CreateAsync(CreatePaymentRequest request, CancellationToken cancellationToken)
    {
        var currencyCode = request.CurrencyCode.ToUpperInvariant();

        // A named provider is used as asked; otherwise the one that charges in the currency.
        var provider = request.ProviderCode is null
            ? providerRegistry.GetForCurrency(currencyCode)
            : providerRegistry.Get(request.ProviderCode);

        logger.LogInformation(
            "Creating a payment: {Kind} {ItemId} of service {ServiceId} for paid entity {PaidEntityId} (organization: {IsOrganization}) by owner {OwnerId}, provider {Provider}, currency {CurrencyCode}, return url {ReturnUrl}",
            request.Kind,
            request.ItemId,
            request.ServiceId,
            request.PaidEntityId,
            request.IsOrganization,
            request.OwnerId,
            provider.Code,
            currencyCode,
            request.ReturnUrl);

        if (!provider.SupportedCurrencies.Contains(currencyCode))
        {
            logger.LogWarning(
                "Payment refused: provider {Provider} does not support currency {CurrencyCode}",
                provider.Code,
                currencyCode);

            throw new BadRequestException(
                nameof(request.CurrencyCode),
                string.Format(Errors.PaymentProviderCurrencyNotSupported, provider.Code, currencyCode));
        }

        var item = await GetItemAsync(request, cancellationToken);

        logger.LogInformation(
            "Payment item {ItemId} resolved: '{Title}', {PriceInUsdCents} USD cents",
            request.ItemId,
            item.Title,
            item.PriceInUsdCents);

        if (request.StorageAddonId is not null || request.StorageUsageBytes is not null)
        {
            item = await AddStorageAsync(request, item, cancellationToken);
        }

        var currencyRate = await GetCurrencyRateAsync(currencyCode, cancellationToken);

        var amount = PriceCalculator.ConvertPrice(
            item.PriceInUsdCents,
            currencyRate.RateToUsd,
            currencyRate.RoundingStep,
            currencyRate.RoundingMode);

        // Both supported currencies have two decimal places.
        var amountMinorUnits = (long)decimal.Round(amount * 100, 0, MidpointRounding.AwayFromZero);
        logger.LogInformation(
            "Payment item {ItemId} priced at {Amount} {CurrencyCode} ({AmountMinorUnits} minor units): rate to USD {RateToUsd}, rounding step {RoundingStep}, rounding mode {RoundingMode}",
            request.ItemId,
            amount,
            currencyCode,
            amountMinorUnits,
            currencyRate.RateToUsd,
            currencyRate.RoundingStep,
            currencyRate.RoundingMode);

        if (amountMinorUnits <= 0)
        {
            logger.LogWarning(
                "Payment refused: item {ItemId} has a non-positive amount {AmountMinorUnits} in {CurrencyCode}",
                request.ItemId,
                amountMinorUnits,
                currencyCode);

            throw new BadRequestException(
                nameof(request.ItemId),
                string.Format(Errors.PaymentItemNotFound, request.ItemId));
        }

        var paymentId = Guid.NewGuid();

        logger.LogInformation(
            "Asking provider {Provider} for a checkout of payment {PaymentId}",
            provider.Code,
            paymentId);

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

        logger.LogInformation(
            "Provider {Provider} created a checkout for payment {PaymentId}: provider payment id {ProviderPaymentId}, url {Url}",
            provider.Code,
            paymentId,
            checkout.ProviderPaymentId,
            checkout.Url);

        context.Payments.Add(new Payment
        {
            Id = paymentId,
            ServiceId = request.ServiceId,
            Kind = request.Kind,
            PaidEntityId = request.PaidEntityId,
            OwnerId = request.OwnerId,
            TariffId = request.Kind == PaymentKind.Subscription ? request.ItemId : null,
            TokenPackId = request.Kind == PaymentKind.TokenPack ? request.ItemId : null,
            StorageAddonId = request.StorageAddonId,
            AmountMinorUnits = amountMinorUnits,
            CurrencyCode = currencyCode,
            Status = PaymentStatus.Pending,
            Provider = provider.Code,
            ProviderPaymentId = checkout.ProviderPaymentId,
            ProviderData = checkout.ProviderData,
            CreatedAt = dateTimeProvider.UtcNow,
        });

        await context.SaveChangesAsync(cancellationToken);

        metrics.RecordPaymentCreated(provider.Code, request.Kind, currencyCode);

        logger.LogInformation(
            "Payment {PaymentId} saved with status {Status}",
            paymentId,
            PaymentStatus.Pending);

        return new PaymentCheckout(paymentId, checkout.Url);
    }

    public async Task<ServiceId?> FindReturnedPaymentServiceAsync(
        string providerCode,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken)
    {
        var provider = providerRegistry.Get(providerCode);

        var paymentId = provider.TryGetReturnedPaymentId(parameters);
        if (paymentId is null)
        {
            logger.LogInformation(
                "A customer returned from {Provider} without a payment id, parameters: {ParameterNames}",
                provider.Code,
                string.Join(", ", parameters.Keys));

            return null;
        }

        var serviceId = await context.Payments
            .Where(x => x.Id == paymentId && x.Provider == provider.Code)
            .Select(x => (ServiceId?)x.ServiceId)
            .SingleOrDefaultAsync(cancellationToken);

        logger.LogInformation(
            "A customer returned from {Provider} for payment {PaymentId}, service {ServiceId}",
            provider.Code,
            paymentId,
            serviceId);

        return serviceId;
    }

    public async Task<PaymentNotificationResult> HandleNotificationAsync(
        string providerCode,
        PaymentNotificationRequest request,
        CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();

        var startedAt = Stopwatch.GetTimestamp();
        var trace = new NotificationTrace();

        try
        {
            return await HandleNotificationCoreAsync(providerCode, request, trace, cancellationToken);
        }
        finally
        {
            metrics.RecordNotification(providerCode, trace.Result, Stopwatch.GetElapsedTime(startedAt));
        }
    }

    /// <summary>
    /// Carries the metrics result of one notification out of the handler: it stays
    /// <see cref="BillingMetrics.NotificationError"/> unless a step reports a more specific one.
    /// </summary>
    private sealed class NotificationTrace
    {
        public string Result { get; set; } = BillingMetrics.NotificationError;
    }

    private async Task<PaymentNotificationResult> HandleNotificationCoreAsync(
        string providerCode,
        PaymentNotificationRequest request,
        NotificationTrace trace,
        CancellationToken cancellationToken)
    {

        var provider = providerRegistry.Get(providerCode);

        // Names only: the values include the provider's signature.
        logger.LogInformation(
            "Received a {Provider} notification, parameters: {ParameterNames}, headers: {HeaderNames}, body length {BodyLength}",
            provider.Code,
            string.Join(", ", request.Parameters.Keys),
            string.Join(", ", request.Headers.Keys),
            request.Body?.Length ?? 0);

        PaymentNotification notification;
        try
        {
            notification = provider.ParseNotification(request);
        }
        catch (Exception ex)
        {
            trace.Result = BillingMetrics.NotificationInvalidSignature;

            logger.LogWarning(
                ex,
                "A {Provider} notification was rejected while being parsed or verified",
                provider.Code);
            throw;
        }

        logger.LogInformation(
            "{Provider} notification parsed: payment {PaymentId}, provider payment id {ProviderPaymentId}, outcome {Outcome}, amount {AmountMinorUnits} {CurrencyCode}",
            provider.Code,
            notification.PaymentId,
            notification.ProviderPaymentId,
            notification.Outcome,
            notification.AmountMinorUnits,
            notification.CurrencyCode);

        var paymentId = await FindPaymentIdAsync(provider.Code, notification, trace, cancellationToken);

        // Two deliveries of the same notification must not fulfil the payment twice.
        await context.Database.PgAdvisoryPaymentXactLock(paymentId, cancellationToken);

        var payment = await context.Payments.SingleAsync(x => x.Id == paymentId, cancellationToken);

        logger.LogInformation(
            "Handling a notification of payment {PaymentId}: status {Status}, {Kind} {TariffId}{TokenPackId}, paid entity {PaidEntityId}, expected {AmountMinorUnits} {CurrencyCode}",
            payment.Id,
            payment.Status,
            payment.Kind,
            payment.TariffId,
            payment.TokenPackId,
            payment.PaidEntityId,
            payment.AmountMinorUnits,
            payment.CurrencyCode);

        var acknowledgement = new PaymentNotificationResult(provider.CreateNotificationAck(notification));

        if (payment.Status == PaymentStatus.Paid)
        {
            logger.LogInformation(
                "Payment {PaymentId} is already paid, nothing to do, sending the acknowledgement again",
                payment.Id);

            trace.Result = BillingMetrics.NotificationDuplicate;

            return acknowledgement;
        }

        if (notification.Outcome == PaymentNotificationOutcome.Paid)
        {
            EnsureNotificationMatchesPayment(payment, notification, trace);

            logger.LogInformation("Fulfilling payment {PaymentId}", payment.Id);

            await fulfillment.FulfillAsync(payment, cancellationToken);

            payment.Status = PaymentStatus.Paid;
            payment.PaidAt = dateTimeProvider.UtcNow;

            metrics.RecordPaymentCompleted(payment.Provider, payment.Kind, PaymentStatus.Paid);
            metrics.RecordPaymentAmount(payment.Provider, payment.CurrencyCode, payment.AmountMinorUnits);

            logger.LogInformation("Payment {PaymentId} fulfilled and marked as paid", payment.Id);
        }
        else if (payment.Status == PaymentStatus.Pending)
        {
            payment.Status = notification.Outcome == PaymentNotificationOutcome.Failed
                ? PaymentStatus.Failed
                : PaymentStatus.Canceled;

            metrics.RecordPaymentCompleted(payment.Provider, payment.Kind, payment.Status);

            logger.LogInformation(
                "Payment {PaymentId} marked as {Status} by the provider",
                payment.Id,
                payment.Status);
        }
        else
        {
            logger.LogInformation(
                "Payment {PaymentId} is {Status}, the {Outcome} notification changes nothing",
                payment.Id,
                payment.Status,
                notification.Outcome);
        }

        payment.ProviderPaymentId ??= notification.ProviderPaymentId;
        payment.ProviderData = notification.ProviderData ?? payment.ProviderData;

        await context.SaveChangesAsync(cancellationToken);

        if (trace.Result == BillingMetrics.NotificationError)
        {
            trace.Result = BillingMetrics.NotificationAccepted;
        }

        logger.LogInformation(
            "Notification of payment {PaymentId} handled, status {Status}",
            payment.Id,
            payment.Status);

        return acknowledgement;
    }

    private async Task<CurrencyRate> GetCurrencyRateAsync(string currencyCode, CancellationToken cancellationToken)
    {
        var currencyRate = await context.CurrencyRates
            .SingleOrDefaultAsync(x => x.Code == currencyCode, cancellationToken);

        return currencyRate ?? throw new BadRequestException(
            nameof(currencyCode),
            string.Format(Errors.CurrencyRateNotFound, currencyCode));
    }

    private async Task<Guid> FindPaymentIdAsync(
        string providerCode,
        PaymentNotification notification,
        NotificationTrace trace,
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
            trace.Result = BillingMetrics.NotificationNotFound;
            logger.LogWarning("A {Provider} notification references no payment", providerCode);

            throw new BadRequestException(
                nameof(notification),
                Errors.PaymentNotificationWithoutReference);
        }

        var paymentId = await query
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);

        if (paymentId is null)
        {
            trace.Result = BillingMetrics.NotificationNotFound;

            logger.LogWarning(
                "No {Provider} payment found for payment id {PaymentId} / provider payment id {ProviderPaymentId}",
                providerCode,
                notification.PaymentId,
                notification.ProviderPaymentId);

            throw new NotFoundException(
                string.Format(Errors.PaymentNotFound, notification.PaymentId?.ToString() ?? notification.ProviderPaymentId));
        }

        return paymentId.Value;
    }

    private void EnsureNotificationMatchesPayment(Payment payment, PaymentNotification notification, NotificationTrace trace)
    {
        var amountMatches = notification.AmountMinorUnits is null
            || notification.AmountMinorUnits == payment.AmountMinorUnits;

        var currencyMatches = notification.CurrencyCode is null
            || string.Equals(notification.CurrencyCode, payment.CurrencyCode, StringComparison.OrdinalIgnoreCase);

        if (!amountMatches || !currencyMatches)
        {
            trace.Result = BillingMetrics.NotificationAmountMismatch;

            logger.LogWarning(
                "Notification does not match payment {PaymentId}: expected {ExpectedAmountMinorUnits} {ExpectedCurrencyCode}, got {AmountMinorUnits} {CurrencyCode}",
                payment.Id,
                payment.AmountMinorUnits,
                payment.CurrencyCode,
                notification.AmountMinorUnits,
                notification.CurrencyCode);

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

        if (item is null)
        {
            logger.LogWarning(
                "Payment refused: {Kind} {ItemId} of service {ServiceId} (organization: {IsOrganization}) is not found or not for sale",
                request.Kind,
                request.ItemId,
                request.ServiceId,
                request.IsOrganization);

            throw new BadRequestException(
                nameof(request.ItemId),
                string.Format(Errors.PaymentItemNotFound, request.ItemId));
        }

        return item;
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

    /// <summary>
    /// Adds the storage add-on to a subscription's price and title, and refuses a checkout that would give
    /// less storage than the customer already uses. An add-on is only sold with a paid tariff of its own
    /// service, which <see cref="GetTariffItemAsync"/> has already checked.
    /// </summary>
    private async Task<PaymentItem> AddStorageAsync(
        CreatePaymentRequest request,
        PaymentItem item,
        CancellationToken cancellationToken)
    {
        var addon = request.StorageAddonId is null
            ? null
            : await context.StorageAddons
                .Where(x => x.Id == request.StorageAddonId && x.ServiceId == request.ServiceId && x.IsActive)
                .Select(x => new { x.Title, x.Price })
                .SingleOrDefaultAsync(cancellationToken);

        var addonFound = request.StorageAddonId is null || addon is not null;

        // Null for a tariff without storage (another service's) and for a token pack.
        var quotaBytes = request.Kind == PaymentKind.Subscription && addonFound
            ? await StorageQuota.GetLaraueBoardsQuotaBytesAsync(
                context,
                request.ItemId,
                request.StorageAddonId,
                cancellationToken)
            : null;

        if (quotaBytes is null)
        {
            logger.LogWarning(
                "Payment refused: storage add-on {StorageAddonId} is not available with {Kind} {ItemId} of service {ServiceId}",
                request.StorageAddonId,
                request.Kind,
                request.ItemId,
                request.ServiceId);

            throw new BadRequestException(
                nameof(request.StorageAddonId),
                string.Format(Errors.StorageAddonNotAvailable, request.StorageAddonId));
        }

        if (request.StorageUsageBytes is { } usageBytes && usageBytes > quotaBytes)
        {
            logger.LogWarning(
                "Payment refused: paid entity {PaidEntityId} uses {UsageBytes} bytes, more than the {QuotaBytes} bytes of {ItemId} with storage add-on {StorageAddonId}",
                request.PaidEntityId,
                usageBytes,
                quotaBytes,
                request.ItemId,
                request.StorageAddonId);

            throw new StorageOverLimitException(usageBytes, quotaBytes.Value);
        }

        if (addon is null)
        {
            return item;
        }

        logger.LogInformation(
            "Payment item {ItemId}: storage add-on {StorageAddonId} '{AddonTitle}' added, {AddonPriceInUsdCents} USD cents",
            request.ItemId,
            request.StorageAddonId,
            addon.Title,
            addon.Price);

        return new PaymentItem($"{item.Title} {addon.Title}", item.PriceInUsdCents + addon.Price);
    }

    private sealed record PaymentItem(string Title, int PriceInUsdCents);
}
