using System.Numerics;

namespace MietHahn.Domain.Gemeinsam;

/// <summary>Prüfung und Formatierung internationaler Kontonummern (ISO 13616, Prüfsumme Modulo 97).</summary>
public static class Iban
{
    /// <summary>„de02 1203…“ → „DE021203…“ (ohne Leerzeichen, Großbuchstaben).</summary>
    public static string Kompakt(string iban)
    {
        ArgumentNullException.ThrowIfNull(iban);
        return new string(iban.Where(z => !char.IsWhiteSpace(z)).ToArray()).ToUpperInvariant();
    }

    /// <summary>„DE02 1203 0000 …“ (Vierergruppen, wie auf Dokumenten üblich).</summary>
    public static string Formatieren(string iban) => string.Join(' ', Kompakt(iban).Chunk(4).Select(c => new string(c)));

    public static bool IstGueltig(string? iban)
    {
        if (string.IsNullOrWhiteSpace(iban))
        {
            return false;
        }

        var kompakt = Kompakt(iban);
        if (kompakt.Length is < 15 or > 34 || !char.IsAsciiLetter(kompakt[0]) || !char.IsAsciiLetter(kompakt[1])
            || !char.IsAsciiDigit(kompakt[2]) || !char.IsAsciiDigit(kompakt[3]) || !kompakt.All(char.IsAsciiLetterOrDigit))
        {
            return false;
        }

        if (kompakt.StartsWith("DE", StringComparison.Ordinal) && kompakt.Length != 22)
        {
            return false;
        }

        // Ländercode und Prüfziffer ans Ende, Buchstaben als Zahlen (A = 10 … Z = 35), Rest modulo 97 muss 1 sein.
        var umgestellt = kompakt[4..] + kompakt[..4];
        var ziffern = string.Concat(umgestellt.Select(z => char.IsAsciiLetter(z) ? (z - 'A' + 10).ToString(System.Globalization.CultureInfo.InvariantCulture) : z.ToString()));
        return BigInteger.Parse(ziffern, System.Globalization.CultureInfo.InvariantCulture) % 97 == 1;
    }
}
