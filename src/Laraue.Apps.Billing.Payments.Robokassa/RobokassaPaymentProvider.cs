using System.Globalization;
using System.Text.Json;
using Laraue.Apps.Billing.Payments.Robokassa.Resources;
using Laraue.Apps.Billing.Services.Payments;
using Laraue.Core.Exceptions.Web;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Laraue.Apps.Billing.Payments.Robokassa;

/// <summary>
/// <see cref="IPaymentProvider"/> for Robokassa. We do not send an <c>InvId</c>: Robokassa assigns it,
/// returns it in the result notification, and it is stored as the payment's provider payment id.
/// Our own payment id travels as the custom <c>Shp_paymentId</c> parameter, which Robokassa echoes
/// back and includes in the notification signature.
/// </summary>
public class RobokassaPaymentProvider(
    IOptions<RobokassaOptions> optionsAccessor,
    ILogger<RobokassaPaymentProvider> logger) : IPaymentProvider
{
    public const string ProviderCode = "robokassa";

    // Robokassa shows at most 100 characters.
    private const int MaxDescriptionLength = 100;

    private RobokassaOptions Options => optionsAccessor.Value;

    public string Code => ProviderCode;

    public IReadOnlySet<string> SupportedCurrencies =>
        Options.Currencies.Select(x => x.ToUpperInvariant()).ToHashSet(StringComparer.Ordinal);

    public Task<PaymentCheckoutResult> CreateCheckoutAsync(
        PaymentCheckoutRequest request,
        CancellationToken cancellationToken)
    {
        var options = Options;
        var outSum = FormatAmount(request.AmountMinorUnits);

        var customParameters = new Dictionary<string, string>
        {
            [RobokassaParameters.PaymentId] = request.PaymentId.ToString(),
        };

        var description = request.Description.Length > MaxDescriptionLength
            ? request.Description[..MaxDescriptionLength]
            : request.Description;

        var query = new List<KeyValuePair<string, string>>
        {
            new(RobokassaParameters.MerchantLogin, options.MerchantLogin),
            new(RobokassaParameters.OutSum, outSum),
            new(RobokassaParameters.Description, description),
            new(RobokassaParameters.SignatureValue, RobokassaSignature.ForCheckout(options, outSum, customParameters)),
            new(RobokassaParameters.Culture, options.Culture),
        };

        query.AddRange(customParameters);

        if (options.IsTest)
        {
            query.Add(new(RobokassaParameters.IsTest, RobokassaParameters.IsTestEnabled));
        }

        var url = options.PaymentUrl + "?" + string.Join(
            "&",
            query.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));

        logger.LogInformation(
            "Robokassa checkout built for payment {PaymentId}: shop {MerchantLogin}, OutSum {OutSum}, test mode {IsTest}, hash {HashAlgorithm}, culture {Culture}, description '{Description}'. Return url {ReturnUrl} is ignored, Robokassa uses the success and fail addresses from the shop settings",
            request.PaymentId,
            options.MerchantLogin,
            outSum,
            options.IsTest,
            options.HashAlgorithm,
            options.Culture,
            description,
            request.ReturnUrl);

        return Task.FromResult(new PaymentCheckoutResult
        {
            Url = url,
            ProviderData = JsonSerializer.Serialize(new { isTest = options.IsTest }),
        });
    }

    public PaymentNotification ParseNotification(PaymentNotificationRequest request)
    {
        var options = Options;

        var outSum = GetRequired(request, RobokassaParameters.OutSum);
        var invId = GetRequired(request, RobokassaParameters.InvId);
        var signature = GetRequired(request, RobokassaParameters.SignatureValue);
        var paymentIdValue = GetRequired(request, RobokassaParameters.PaymentId);

        // Every Shp_ parameter is part of the signature, not just our own.
        var customParameters = request.Parameters
            .Where(x => x.Key.StartsWith(RobokassaParameters.CustomPrefix, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(x => x.Key, x => x.Value);

        var expected = RobokassaSignature.ForResult(options, outSum, invId, customParameters);

        if (!RobokassaSignature.Matches(expected, signature))
        {
            // The signatures are not logged: the expected one would let anyone forge this notification.
            logger.LogWarning(
                "Robokassa notification signature mismatch: OutSum {OutSum}, InvId {InvId}, custom parameters {CustomParameters}, test mode {IsTest}, hash {HashAlgorithm}",
                outSum,
                invId,
                string.Join(", ", customParameters.Keys),
                options.IsTest,
                options.HashAlgorithm);

            throw new BadRequestException(RobokassaParameters.SignatureValue, Errors.NotificationSignatureInvalid);
        }

        if (!Guid.TryParse(paymentIdValue, out var paymentId))
        {
            logger.LogWarning(
                "Robokassa notification {InvId} has an invalid {Parameter} '{Value}'",
                invId,
                RobokassaParameters.PaymentId,
                paymentIdValue);

            throw new BadRequestException(
                RobokassaParameters.PaymentId,
                string.Format(Errors.NotificationParameterInvalid, RobokassaParameters.PaymentId));
        }

        if (!decimal.TryParse(outSum, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
        {
            logger.LogWarning(
                "Robokassa notification {InvId} has an invalid OutSum '{OutSum}'",
                invId,
                outSum);

            throw new BadRequestException(
                RobokassaParameters.OutSum,
                string.Format(Errors.NotificationParameterInvalid, RobokassaParameters.OutSum));
        }

        var amountMinorUnits = (long)decimal.Round(amount * 100, 0, MidpointRounding.AwayFromZero);

        logger.LogInformation(
            "Robokassa notification verified: payment {PaymentId}, InvId {InvId}, OutSum {OutSum} ({AmountMinorUnits} minor units), test mode {IsTest}, other parameters {ParameterNames}",
            paymentId,
            invId,
            outSum,
            amountMinorUnits,
            options.IsTest,
            string.Join(", ", request.Parameters.Keys));

        // Robokassa calls the result address only for a successful payment, a failed one never gets here.
        return new PaymentNotification
        {
            PaymentId = paymentId,
            ProviderPaymentId = invId,
            Outcome = PaymentNotificationOutcome.Paid,
            AmountMinorUnits = amountMinorUnits,
            // The notification does not carry a currency, it is the shop's one.
            CurrencyCode = null,
            ProviderData = JsonSerializer.Serialize(new { invId, outSum, isTest = options.IsTest }),
        };
    }

    public Guid? TryGetReturnedPaymentId(IReadOnlyDictionary<string, string> parameters)
    {
        // Robokassa may change the case of the custom parameters it echoes back.
        var value = parameters
            .FirstOrDefault(x => string.Equals(x.Key, RobokassaParameters.PaymentId, StringComparison.OrdinalIgnoreCase))
            .Value;

        return Guid.TryParse(value, out var paymentId) ? paymentId : null;
    }

    public string CreateNotificationAck(PaymentNotification notification)
    {
        return RobokassaParameters.AcknowledgementPrefix + notification.ProviderPaymentId;
    }

    /// <summary>
    /// Robokassa wants the amount as a decimal with a dot, e.g. <c>1250.00</c>.
    /// </summary>
    private static string FormatAmount(long amountMinorUnits)
    {
        return (amountMinorUnits / 100m).ToString("0.00", CultureInfo.InvariantCulture);
    }

    private string GetRequired(PaymentNotificationRequest request, string name)
    {
        if (request.Parameters.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        logger.LogWarning("Robokassa notification has no '{Parameter}' parameter, got: {ParameterNames}",
            name,
            string.Join(", ", request.Parameters.Keys));

        throw new BadRequestException(name, string.Format(Errors.NotificationParameterMissing, name));
    }
}
