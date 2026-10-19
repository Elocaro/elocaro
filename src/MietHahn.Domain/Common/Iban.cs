using System.Numerics;

namespace MietHahn.Domain.Common;

/// <summary>Prüfung und Formatierung internationaler Kontonummern (ISO 13616, Prüfsumme Modulo 97).</summary>
public static class Iban
{
    /// <summary>„de02 1203…“ → „DE021203…“ (ohne Leerzeichen, Großbuchstaben).</summary>
    public static string Compact(string iban)
    {
        ArgumentNullException.ThrowIfNull(iban);
        return new string(iban.Where(z => !char.IsWhiteSpace(z)).ToArray()).ToUpperInvariant();
    }

    /// <summary>„DE02 1203 0000 …“ (Vierergruppen, wie auf Dokumenten üblich).</summary>
    public static string Format(string iban) => string.Join(' ', Compact(iban).Chunk(4).Select(c => new string(c)));

    public static bool IsValid(string? iban)
    {
        if (string.IsNullOrWhiteSpace(iban))
        {
            return false;
        }

        var compact = Compact(iban);
        if (compact.Length is < 15 or > 34 || !char.IsAsciiLetter(compact[0]) || !char.IsAsciiLetter(compact[1])
            || !char.IsAsciiDigit(compact[2]) || !char.IsAsciiDigit(compact[3]) || !compact.All(char.IsAsciiLetterOrDigit))
        {
            return false;
        }

        if (compact.StartsWith("DE", StringComparison.Ordinal) && compact.Length != 22)
        {
            return false;
        }

        // Ländercode und Prüfziffer ans Ende, Buchstaben als Zahlen (A = 10 … Z = 35), Rest modulo 97 muss 1 sein.
        var rearranged = compact[4..] + compact[..4];
        var digits = string.Concat(rearranged.Select(z => char.IsAsciiLetter(z) ? (z - 'A' + 10).ToString(System.Globalization.CultureInfo.InvariantCulture) : z.ToString()));
        return BigInteger.Parse(digits, System.Globalization.CultureInfo.InvariantCulture) % 97 == 1;
    }
}
