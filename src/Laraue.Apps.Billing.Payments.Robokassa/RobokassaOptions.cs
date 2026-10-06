using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Billing.Payments.Robokassa;

public class RobokassaOptions : IValidatableObject
{
    public const string SectionName = "Payments:Robokassa";

    /// <summary>
    /// The shop identifier ("Идентификатор магазина") from the Robokassa technical settings.
    /// </summary>
    [Required]
    public string MerchantLogin { get; set; } = null!;

    /// <summary>
    /// "Пароль #1": signs the checkout request.
    /// </summary>
    public string? Password1 { get; set; }

    /// <summary>
    /// "Пароль #2": verifies the result notification.
    /// </summary>
    public string? Password2 { get; set; }

    /// <summary>
    /// Robokassa keeps a separate pair of passwords for test payments.
    /// </summary>
    public string? TestPassword1 { get; set; }

    public string? TestPassword2 { get; set; }

    /// <summary>
    /// Sends payments in test mode (<c>IsTest=1</c>) and signs with the test passwords. Set it in
    /// appsettings explicitly: <c>true</c> in the base file, <c>false</c> in production.
    /// </summary>
    public bool IsTest { get; set; }

    /// <summary>
    /// Must be the algorithm chosen in the shop's technical settings.
    /// </summary>
    public RobokassaHashAlgorithm HashAlgorithm { get; set; }

    [Required]
    public string PaymentUrl { get; set; } = null!;

    /// <summary>
    /// Language of the payment page, e.g. <c>ru</c> or <c>en</c>.
    /// </summary>
    [Required]
    public string Culture { get; set; } = null!;

    /// <summary>
    /// ISO 4217 codes of the currencies the shop charges in, as enabled in the Robokassa account
    /// ("Валюты"). Only <c>RUB</c> is supported by the code so far: the checkout never sends
    /// <c>OutSumCurrency</c>, so any other currency would charge its amount in rubles.
    /// </summary>
    [Required]
    [MinLength(1)]
    public string[] Currencies { get; set; } = null!;

    /// <summary>
    /// The only currency the checkout can express.
    /// </summary>
    public const string SupportedCurrency = "RUB";

    public string? ActivePassword1 => IsTest ? TestPassword1 : Password1;

    public string? ActivePassword2 => IsTest ? TestPassword2 : Password2;

    /// <summary>
    /// Which passwords are required depends on <see cref="IsTest"/>, which an attribute cannot express.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var currency in Currencies.Where(x => !string.Equals(x, SupportedCurrency, StringComparison.OrdinalIgnoreCase)))
        {
            yield return new ValidationResult(
                $"Currency '{currency}' is not supported by the Robokassa provider yet, only {SupportedCurrency}.",
                [nameof(Currencies)]);
        }

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
