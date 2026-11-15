using System.Numerics;

namespace Elocaro.Domain.Common;

/// <summary>
/// Creditor identifier (Gläubiger-Identifikationsnummer, e.g. <c>DE98ZZZ09999999999</c>) that a creditor needs for SEPA direct debits. The Bundesbank
/// issues it. Check: country code, two check digits, a three-character business code (ignored for the check) and the national part; the check
/// digits follow ISO 7064 (modulo 97-10) over the national part, the country code and the check digits.
/// </summary>
public static class CreditorId
{
    public const int MaxLength = 35;

    /// <summary>Without spaces, uppercase.</summary>
    public static string Compact(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new string(value.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
    }

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var id = Compact(value);
        // Germany: 18 characters; other countries up to 35.
        if (id.Length is < 8 or > MaxLength || !char.IsAsciiLetter(id[0]) || !char.IsAsciiLetter(id[1]) || !char.IsAsciiDigit(id[2]) || !char.IsAsciiDigit(id[3])
            || !id.All(c => char.IsAsciiLetterOrDigit(c)))
        {
            return false;
        }

        if (id.StartsWith("DE", StringComparison.Ordinal) && id.Length != 18)
        {
            return false;
        }

        var rearranged = id[7..] + id[..4];
        var digits = string.Concat(rearranged.Select(c => char.IsAsciiLetter(c) ? (c - 'A' + 10).ToString(System.Globalization.CultureInfo.InvariantCulture) : c.ToString()));
        return BigInteger.Parse(digits, System.Globalization.CultureInfo.InvariantCulture) % 97 == 1;
    }
}
