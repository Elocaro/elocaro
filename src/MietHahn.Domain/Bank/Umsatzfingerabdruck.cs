using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MietHahn.Domain.Bank;

/// <summary>
/// Fingerabdruck eines Umsatzes für die Duplikaterkennung aus Konto, Buchungsdatum, Betrag, Verwendungszweck,
/// Gegen-IBAN und End-to-End-ID. Gleiche Umsätze innerhalb einer Datei (z. B. zwei gleiche Überweisungen am selben Tag)
/// werden durchnummeriert, damit sie nicht als Duplikat gelten, ein erneuter Import derselben Datei aber schon.
/// </summary>
public static class Umsatzfingerabdruck
{
    public static IReadOnlyList<string> Berechnen(string kontoIban, IEnumerable<GelesenerUmsatz> umsaetze)
    {
        ArgumentNullException.ThrowIfNull(umsaetze);
        var zaehler = new Dictionary<string, int>(StringComparer.Ordinal);
        var ergebnis = new List<string>();
        foreach (var umsatz in umsaetze)
        {
            var basis = Basis(kontoIban, umsatz);
            zaehler[basis] = zaehler.GetValueOrDefault(basis) + 1;
            ergebnis.Add(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{basis}|{zaehler[basis]}"))));
        }

        return ergebnis;
    }

    private static string Basis(string kontoIban, GelesenerUmsatz u) => string.Join('|',
        Gemeinsam.Iban.Kompakt(kontoIban),
        u.Buchungsdatum.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        u.Betrag.ToString("0.00", CultureInfo.InvariantCulture),
        Normalisieren(u.Verwendungszweck),
        u.GegenIban is null ? "" : Gemeinsam.Iban.Kompakt(u.GegenIban),
        Normalisieren(u.EndToEndId is "NOTPROVIDED" ? null : u.EndToEndId));

    // Banken umbrechen Verwendungszwecke unterschiedlich; Leerraum und Groß/Klein spielen keine Rolle.
    private static string Normalisieren(string? text) =>
        string.Concat((text ?? "").Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
}
