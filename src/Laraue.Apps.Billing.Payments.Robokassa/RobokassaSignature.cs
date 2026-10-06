using System.Security.Cryptography;
using System.Text;

namespace Laraue.Apps.Billing.Payments.Robokassa;

/// <summary>
/// Builds and checks Robokassa signatures: the colon-joined values followed by the custom
/// <c>Shp_</c> parameters sorted by name, hashed and written as hex.
/// </summary>
internal static class RobokassaSignature
{
    /// <summary>
    /// <c>MerchantLogin:OutSum:InvId:Password1[:Shp_a=1[:Shp_b=2]]</c>. We never send an <c>InvId</c>,
    /// Robokassa assigns one, so its place stays empty (<c>login:sum::password</c>).
    /// </summary>
    public static string ForCheckout(
        RobokassaOptions options,
        string outSum,
        IReadOnlyDictionary<string, string> customParameters)
    {
        return Compute(
            options.HashAlgorithm,
            $"{options.MerchantLogin}:{outSum}::{options.ActivePassword1}{FormatCustomParameters(customParameters)}");
    }

    /// <summary>
    /// <c>OutSum:InvId:Password2[:Shp_a=1[:Shp_b=2]]</c>, with <c>OutSum</c> and <c>InvId</c> exactly as
    /// received.
    /// </summary>
    public static string ForResult(
        RobokassaOptions options,
        string outSum,
        string invId,
        IReadOnlyDictionary<string, string> customParameters)
    {
        return Compute(
            options.HashAlgorithm,
            $"{outSum}:{invId}:{options.ActivePassword2}{FormatCustomParameters(customParameters)}");
    }

    public static bool Matches(string expected, string actual)
    {
        // Hex is case-insensitive.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected.ToLowerInvariant()),
            Encoding.UTF8.GetBytes(actual.Trim().ToLowerInvariant()));
    }

    private static string FormatCustomParameters(IReadOnlyDictionary<string, string> customParameters)
    {
        var builder = new StringBuilder();

        foreach (var (name, value) in customParameters.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(':').Append(name).Append('=').Append(value);
        }

        return builder.ToString();
    }

    private static string Compute(RobokassaHashAlgorithm algorithm, string data)
    {
        var bytes = Encoding.UTF8.GetBytes(data);

        var hash = algorithm switch
        {
            RobokassaHashAlgorithm.Md5 => MD5.HashData(bytes),
            RobokassaHashAlgorithm.Sha256 => SHA256.HashData(bytes),
            RobokassaHashAlgorithm.Sha384 => SHA384.HashData(bytes),
            RobokassaHashAlgorithm.Sha512 => SHA512.HashData(bytes),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, null),
        };

        return Convert.ToHexStringLower(hash);
    }
}
