namespace Laraue.Apps.Billing.Payments.Robokassa;

/// <summary>
/// The names and values of the parameters of the Robokassa checkout address and of its notifications.
/// </summary>
internal static class RobokassaParameters
{
    public const string MerchantLogin = "MerchantLogin";

    /// <summary>
    /// The amount, as a decimal with a dot.
    /// </summary>
    public const string OutSum = "OutSum";

    /// <summary>
    /// Robokassa's own payment number; we never send it.
    /// </summary>
    public const string InvId = "InvId";

    public const string Description = "Description";

    public const string SignatureValue = "SignatureValue";

    public const string Culture = "Culture";

    public const string IsTest = "IsTest";

    public const string IsTestEnabled = "1";

    /// <summary>
    /// Every parameter with this prefix is ours: Robokassa echoes it back and signs it.
    /// </summary>
    public const string CustomPrefix = "Shp_";

    /// <summary>
    /// Our payment id, sent as a custom parameter.
    /// </summary>
    public const string PaymentId = CustomPrefix + "paymentId";

    /// <summary>
    /// The answer to a handled notification is this followed by the <see cref="InvId"/>.
    /// </summary>
    public const string AcknowledgementPrefix = "OK";
}
