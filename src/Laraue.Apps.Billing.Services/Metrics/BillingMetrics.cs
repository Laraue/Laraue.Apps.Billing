using System.Diagnostics.Metrics;
using Laraue.Apps.Billing.DataAccess.Entities;

namespace Laraue.Apps.Billing.Services.Metrics;

/// <summary>
/// Billing's event counters, recorded by the host that handles the event (the token ledger over gRPC, payment
/// creation over gRPC, payment notifications on the web host). Every label has a few values at most: never
/// a payment, user or organization id. State that must survive a restart is a gauge, see
/// <c>BillingStateMetrics</c> in WorkerServices. A metric is recorded when the code runs, before the caller's
/// transaction commits, so a rolled-back transaction can overcount slightly.
/// </summary>
public sealed class BillingMetrics
{
    public const string MeterName = "Laraue.Apps.Billing";

    public const string ReservationStarted = "started";
    public const string ReservationConfirmed = "confirmed";
    public const string ReservationCancelled = "cancelled";
    public const string ReservationInsufficientBalance = "insufficient_balance";

    public const string NotificationAccepted = "accepted";
    public const string NotificationDuplicate = "duplicate";
    public const string NotificationInvalidSignature = "invalid_signature";
    public const string NotificationAmountMismatch = "amount_mismatch";
    public const string NotificationNotFound = "not_found";
    public const string NotificationError = "error";

    private readonly Counter<long> _tokensSpent;
    private readonly Counter<long> _tokenReservations;
    private readonly Counter<long> _paymentsCreated;
    private readonly Counter<long> _paymentsCompleted;
    private readonly Counter<long> _paymentsAmount;
    private readonly Counter<long> _paymentNotifications;
    private readonly Histogram<double> _paymentNotificationDuration;

    public BillingMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);

        _tokensSpent = meter.CreateCounter<long>(
            "billing.tokens.spent",
            unit: "{token}",
            description: "Tokens spent, added when a reservation is confirmed, by service.");

        _tokenReservations = meter.CreateCounter<long>(
            "billing.token.reservations",
            description: "Token reservations by service and result (started, confirmed, cancelled, insufficient_balance).");

        _paymentsCreated = meter.CreateCounter<long>(
            "billing.payments.created",
            description: "Payments created (a checkout issued), by provider, kind and currency.");

        _paymentsCompleted = meter.CreateCounter<long>(
            "billing.payments.completed",
            description: "Payments that left Pending, by provider, kind and final status (paid, failed, canceled).");

        _paymentsAmount = meter.CreateCounter<long>(
            "billing.payments.amount",
            description: "Money paid, in minor units of the currency, by provider and currency.");

        _paymentNotifications = meter.CreateCounter<long>(
            "billing.payment.notifications",
            description: "Payment notifications received, by provider and result (accepted, duplicate, invalid_signature, amount_mismatch, not_found, error).");

        _paymentNotificationDuration = meter.CreateHistogram<double>(
            "billing.payment.notification.duration",
            unit: "s",
            description: "Time to handle a payment notification, by provider.");

        // A counter series only exists after its first event, and rate()/increase() cannot see that first
        // event. Recording a zero for every service and result we know makes those series exist from the first
        // scrape. Provider and currency labels are not known here, so the payment series appear with the first
        // payment.
        foreach (var serviceId in Enum.GetValues<ServiceId>())
        {
            _tokensSpent.Add(0, Tag("service", serviceId.ToString()));

            foreach (var result in new[] { ReservationStarted, ReservationConfirmed, ReservationCancelled, ReservationInsufficientBalance })
            {
                _tokenReservations.Add(0, Tag("service", serviceId.ToString()), Tag("result", result));
            }
        }
    }

    public void RecordReservation(ServiceId serviceId, string result)
        => _tokenReservations.Add(1, Tag("service", serviceId.ToString()), Tag("result", result));

    public void RecordTokensSpent(ServiceId serviceId, long tokens)
        => _tokensSpent.Add(tokens, Tag("service", serviceId.ToString()));

    public void RecordPaymentCreated(string provider, PaymentKind kind, string currency)
        => _paymentsCreated.Add(1, Tag("provider", provider), Tag("kind", kind.ToString()), Tag("currency", currency));

    public void RecordPaymentCompleted(string provider, PaymentKind kind, PaymentStatus status)
        => _paymentsCompleted.Add(1, Tag("provider", provider), Tag("kind", kind.ToString()), Tag("status", status.ToString()));

    public void RecordPaymentAmount(string provider, string currency, long amountMinorUnits)
        => _paymentsAmount.Add(amountMinorUnits, Tag("provider", provider), Tag("currency", currency));

    public void RecordNotification(string provider, string result, TimeSpan duration)
    {
        _paymentNotifications.Add(1, Tag("provider", provider), Tag("result", result));
        _paymentNotificationDuration.Record(duration.TotalSeconds, Tag("provider", provider));
    }

    private static KeyValuePair<string, object?> Tag(string name, object? value) => new(name, value);
}
