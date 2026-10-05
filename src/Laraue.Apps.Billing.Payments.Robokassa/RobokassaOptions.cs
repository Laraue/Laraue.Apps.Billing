using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Billing.Payments.Robokassa;

public class RobokassaOptions : IValidatableObject
{
    public const string SectionName = "Payments:Robokassa";

    /// <summary>
    /// The shop identifier ("Идентификатор магазина") from the Robokassa technical settings.
    /// </summary>
    [Required]
    public string MerchantLogin { get; set; } = string.Empty;

    /// <summary>
    /// "Пароль #1": signs the checkout request.
    /// </summary>
    public string Password1 { get; set; } = string.Empty;

    /// <summary>
    /// "Пароль #2": verifies the result notification.
    /// </summary>
    public string Password2 { get; set; } = string.Empty;

    /// <summary>
    /// Robokassa keeps a separate pair of passwords for test payments.
    /// </summary>
    public string TestPassword1 { get; set; } = string.Empty;

    public string TestPassword2 { get; set; } = string.Empty;

    /// <summary>
    /// Sends payments in test mode (<c>IsTest=1</c>) and signs with the test passwords.
    /// </summary>
    public bool IsTest { get; set; }

    /// <summary>
    /// Must be the algorithm chosen in the shop's technical settings.
    /// </summary>
    public RobokassaHashAlgorithm HashAlgorithm { get; set; } = RobokassaHashAlgorithm.Md5;

    public string PaymentUrl { get; set; } = "https://auth.robokassa.ru/Merchant/Index.aspx";

    /// <summary>
    /// Language of the payment page, e.g. <c>ru</c> or <c>en</c>.
    /// </summary>
    public string Culture { get; set; } = "ru";

    public string ActivePassword1 => IsTest ? TestPassword1 : Password1;

    public string ActivePassword2 => IsTest ? TestPassword2 : Password2;

    /// <summary>
    /// Which passwords are required depends on <see cref="IsTest"/>, which an attribute cannot express.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var (password1Name, password2Name) = IsTest
            ? (nameof(TestPassword1), nameof(TestPassword2))
            : (nameof(Password1), nameof(Password2));

        if (string.IsNullOrWhiteSpace(ActivePassword1))
        {
            yield return new ValidationResult($"{password1Name} is required.", [password1Name]);
        }

        if (string.IsNullOrWhiteSpace(ActivePassword2))
        {
            yield return new ValidationResult($"{password2Name} is required.", [password2Name]);
        }
    }
}

public enum RobokassaHashAlgorithm
{
    Md5,
    Sha256,
    Sha384,
    Sha512,
}
