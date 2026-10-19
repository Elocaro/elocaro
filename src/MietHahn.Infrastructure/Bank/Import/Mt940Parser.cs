using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MietHahn.Domain.Bank;

namespace MietHahn.Infrastructure.Bank.Import;

/// <summary>
/// SWIFT MT940 mit deutschem strukturiertem Feld 86 (GVC, ?00 Buchungstext, ?20–?29 und ?60–?63 Verwendungszweck,
/// ?30 BLZ/BIC, ?31 Konto/IBAN, ?32/?33 Name). SEPA-Kennungen (EREF+, MREF+, CRED+, SVWZ+) werden ausgewertet.
/// </summary>
internal static partial class Mt940Parser
{
    public static bool Erkennt(byte[] inhalt)
    {
        var text = Textdekodierung.Lesen(inhalt);
        return text.Contains(":20:", StringComparison.Ordinal) && text.Contains(":61:", StringComparison.Ordinal) && text.Contains(":25:", StringComparison.Ordinal);
    }

    public static Kontoauszug Lesen(byte[] inhalt)
    {
        var felder = Felder(Textdekodierung.Lesen(inhalt));
        string? konto = null;
        var umsaetze = new List<GelesenerUmsatz>();
        var hinweise = new List<string>();
        for (var i = 0; i < felder.Count; i++)
        {
            var (tag, wert) = felder[i];
            if (tag == "25")
            {
                konto ??= Konto(wert);
            }
            else if (tag == "61")
            {
                var naechstes = i + 1 < felder.Count && felder[i + 1].Tag == "86" ? felder[i + 1].Wert : null;
                umsaetze.Add(Umsatz(wert, naechstes));
            }
        }

        if (umsaetze.Count == 0 && konto is null)
        {
            throw new BankdateiFehlerException("Die Datei enthält keine MT940-Umsätze (:61:).");
        }

        return new Kontoauszug(konto, umsaetze, hinweise);
    }

    /// <summary>Felder „:tag:wert“; Folgezeilen gehören zum vorherigen Feld (Zeilenumbrüche entfallen).</summary>
    private static List<(string Tag, string Wert)> Felder(string text)
    {
        var felder = new List<(string, StringBuilder)>();
        foreach (var zeile in text.Replace("\r", "", StringComparison.Ordinal).Split('\n'))
        {
            var treffer = Feldanfang().Match(zeile);
            if (treffer.Success)
            {
                felder.Add((treffer.Groups[1].Value, new StringBuilder(zeile[treffer.Length..])));
            }
            else if (felder.Count > 0 && zeile.Length > 0 && zeile != "-" && !zeile.StartsWith('{'))
            {
                felder[^1].Item2.Append(zeile);
            }
        }

        return felder.Select(f => (f.Item1, f.Item2.ToString())).ToList();
    }

    // „BLZ/Kontonummer“ oder IBAN
    private static string? Konto(string wert)
    {
        var kompakt = wert.Replace(" ", "", StringComparison.Ordinal);
        return Domain.Gemeinsam.Iban.IstGueltig(kompakt) ? kompakt : null;
    }

    private static GelesenerUmsatz Umsatz(string feld61, string? feld86)
    {
        var treffer = Feld61().Match(feld61);
        if (!treffer.Success)
        {
            throw new BankdateiFehlerException($"Unlesbare Umsatzzeile :61:{feld61}");
        }

        var valuta = DateOnly.ParseExact(treffer.Groups["valuta"].Value, "yyMMdd", CultureInfo.InvariantCulture);
        var buchung = valuta;
        if (treffer.Groups["buchung"].Success)
        {
            // Buchungsdatum ohne Jahr: liegt höchstens wenige Tage neben der Valuta, ggf. über den Jahreswechsel.
            var monat = int.Parse(treffer.Groups["buchung"].Value[..2], CultureInfo.InvariantCulture);
            var tag = int.Parse(treffer.Groups["buchung"].Value[2..], CultureInfo.InvariantCulture);
            var kandidaten = new[] { valuta.Year - 1, valuta.Year, valuta.Year + 1 }
                .Where(j => tag <= DateTime.DaysInMonth(j, monat))
                .Select(j => new DateOnly(j, monat, tag));
            buchung = kandidaten.MinBy(d => Math.Abs(d.DayNumber - valuta.DayNumber));
        }

        var betrag = decimal.Parse(treffer.Groups["betrag"].Value.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        if (treffer.Groups["dc"].Value is "D" or "RC")
        {
            betrag = -betrag;
        }

        if (feld86 is null)
        {
            return new GelesenerUmsatz(buchung, betrag, "", valuta);
        }

        var unterfelder = Unterfelder(feld86);
        var zweckRoh = string.Concat(unterfelder.Where(u => u.Key is >= 20 and <= 29 or >= 60 and <= 63).OrderBy(u => u.Key).Select(u => u.Value));
        if (unterfelder.Count == 0)
        {
            zweckRoh = feld86;
        }

        var sepa = SepaZweck.Zerlegen(zweckRoh);
        var name = (unterfelder.GetValueOrDefault(32) + unterfelder.GetValueOrDefault(33)).Trim();
        var bic = unterfelder.GetValueOrDefault(30)?.Trim();
        var iban = unterfelder.GetValueOrDefault(31)?.Trim();
        return new GelesenerUmsatz(
            buchung,
            betrag,
            sepa.Verwendungszweck,
            valuta,
            GegenName: name.Length == 0 ? sepa.AbweichenderName : name,
            GegenIban: iban is not null && Domain.Gemeinsam.Iban.IstGueltig(iban) ? iban : null,
            GegenBic: bic is { Length: 8 or 11 } && char.IsLetter(bic[0]) ? bic : null,
            Buchungstext: unterfelder.GetValueOrDefault(0)?.Trim(),
            EndToEndId: sepa.EndToEndId,
            Mandatsreferenz: sepa.Mandatsreferenz,
            GlaeubigerId: sepa.GlaeubigerId);
    }

    /// <summary>„166?00GUTSCHRIFT?20EREF+…“: drei Ziffern GVC, danach Trennzeichen + zwei Ziffern je Unterfeld.</summary>
    private static Dictionary<int, string> Unterfelder(string feld86)
    {
        var ergebnis = new Dictionary<int, string>();
        if (feld86.Length < 4 || !char.IsAsciiDigit(feld86[0]))
        {
            return ergebnis;
        }

        var trenner = feld86[3];
        foreach (var teil in feld86[4..].Split(trenner))
        {
            if (teil.Length >= 2 && int.TryParse(teil[..2], NumberStyles.None, CultureInfo.InvariantCulture, out var nummer))
            {
                ergebnis[nummer] = ergebnis.GetValueOrDefault(nummer) + teil[2..];
            }
        }

        return ergebnis;
    }

    [GeneratedRegex(@"^:(\d{2}[A-Z]?):")]
    private static partial Regex Feldanfang();

    [GeneratedRegex(@"^(?<valuta>\d{6})(?<buchung>\d{4})?(?<dc>RC|RD|C|D)[A-Z]?(?<betrag>\d+,\d*)")]
    private static partial Regex Feld61();
}
