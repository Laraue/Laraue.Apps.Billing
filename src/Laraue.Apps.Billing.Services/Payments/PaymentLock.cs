namespace Laraue.Apps.Billing.Services.Payments;

/// <summary>
/// The advisory lock key of one payment. Everything that changes a payment's status takes it (the notification handler,
/// the job that expires abandoned payments), so two of them never interleave on one payment. Prefixed, because the
/// per-paid-entity locks (tokens, subscriptions, fulfilment) use a bare id as their key and must never share a key
/// with a payment lock.
/// </summary>
public static class PaymentLock
{
    public static string Key(Guid paymentId) => $"payment:{paymentId}";
}
