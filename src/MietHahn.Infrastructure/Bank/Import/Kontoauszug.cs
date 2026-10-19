using System.Text;
using System.Text.RegularExpressions;
using MietHahn.Domain.Bank;

namespace MietHahn.Infrastructure.Bank.Import;

/// <summary>Ergebnis eines Parsers: Kontonummer laut Datei (falls enthalten), Umsätze und Hinweise zu übersprungenen Zeilen.</summary>
internal sealed record Kontoauszug(string? Iban, IReadOnlyList<GelesenerUmsatz> Umsaetze, IReadOnlyList<string> Hinweise);

/// <summary>Die Datei ist kein lesbarer Kontoauszug; die Meldung ist für Benutzer formuliert.</summary>
internal sealed class BankdateiFehlerException(string message) : Exception(message);

internal static class Textdekodierung
{
    private static readonly UTF8Encoding StrengesUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    static Textdekodierung() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>„auto“: UTF-8 (mit oder ohne BOM), sonst Windows-1252, wie es deutsche Banken oft liefern.</summary>
    public static string Lesen(byte[] inhalt, string zeichensatz = "auto")
    {
        ArgumentNullException.ThrowIfNull(inhalt);
        var daten = inhalt.AsSpan();
        if (daten.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            return Encoding.UTF8.GetString(daten[3..]);
        }

        switch (zeichensatz.ToLowerInvariant())
        {
            case "utf-8":
                return Encoding.UTF8.GetString(daten);
            case "windows-1252":
                return Encoding.GetEncoding(1252).GetString(daten);
            default:
                try
                {
                    return StrengesUtf8.GetString(daten);
                }
                catch (DecoderFallbackException)
                {
                    return Encoding.GetEncoding(1252).GetString(daten);
                }
        }
    }
}

/// <summary>Zerlegt einen SEPA-Verwendungszweck mit Kennungen (EREF+, MREF+, CRED+, SVWZ+, …).</summary>
internal static partial class SepaZweck
{
    public sealed record Teile(string Verwendungszweck, string? EndToEndId, string? Mandatsreferenz, string? GlaeubigerId, string? AbweichenderName);

    public static Teile Zerlegen(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var treffer = Kennung().Matches(text);
        if (treffer.Count == 0)
        {
            return new Teile(Bereinigen(text), null, null, null, null);
        }

        var werte = new Dictionary<string, string>(StringComparer.Ordinal);
        var davor = text[..treffer[0].Index];
        for (var i = 0; i < treffer.Count; i++)
        {
            var start = treffer[i].Index + treffer[i].Length;
            var ende = i + 1 < treffer.Count ? treffer[i + 1].Index : text.Length;
            werte.TryAdd(treffer[i].Groups[1].Value, Bereinigen(text[start..ende]));
        }

        var zweck = werte.GetValueOrDefault("SVWZ") ?? Bereinigen(davor);
        return new Teile(
            zweck,
            Leer(werte.GetValueOrDefault("EREF")),
            Leer(werte.GetValueOrDefault("MREF")),
            Leer(werte.GetValueOrDefault("CRED")),
            Leer(werte.GetValueOrDefault("ABWA")));
    }

    public static string Bereinigen(string text) => Leerraum().Replace(text, " ").Trim();

    private static string? Leer(string? wert) => string.IsNullOrWhiteSpace(wert) || wert == "NOTPROVIDED" ? null : wert;

    [GeneratedRegex(@"(EREF|KREF|MREF|CRED|DEBT|SVWZ|ABWA|ABWE|IBAN|BIC|COAM|OAMT)\+")]
    private static partial Regex Kennung();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Leerraum();
}
