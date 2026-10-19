using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MietHahn.Domain.Bank;

namespace MietHahn.Infrastructure.Bank.Import;

/// <summary>CSV-Umsätze nach einem <see cref="CsvProfil"/>; Vorspann-Zeilen vor der Überschrift werden übersprungen.</summary>
internal static class CsvParser
{
    private static readonly Regex IbanMuster = new(@"\b[A-Z]{2}\d{2}(?: ?[A-Z0-9]){11,30}\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Anzahl gefundener Spalten, wenn alle Pflichtspalten in einer Überschriftszeile stehen, sonst 0. Mehrere Profile
    /// können passen (gleiche Spaltennamen); das mit den meisten Treffern beschreibt die Datei am genauesten.
    /// </summary>
    public static int Treffer(CsvProfil profil, byte[] inhalt)
    {
        ArgumentNullException.ThrowIfNull(profil);
        var zeilen = Zeilen(Textdekodierung.Lesen(inhalt, profil.Zeichensatz), profil.Trennzeichen).Take(40);
        return zeilen.Select(z => Spalten(profil, z)?.Count ?? 0).DefaultIfEmpty(0).Max();
    }

    public static Kontoauszug Lesen(CsvProfil profil, byte[] inhalt)
    {
        ArgumentNullException.ThrowIfNull(profil);
        var text = Textdekodierung.Lesen(inhalt, profil.Zeichensatz);
        var zeilen = Zeilen(text, profil.Trennzeichen).ToList();
        var kopf = zeilen.FindIndex(z => Spalten(profil, z) is not null);
        if (kopf < 0)
        {
            throw new BankdateiFehlerException($"Die Spaltenüberschriften passen nicht zum Profil „{profil.Name}“.");
        }

        var spalten = Spalten(profil, zeilen[kopf])!;
        // Im Vorspann steht oft die IBAN des Kontos (ING, DKB).
        var iban = zeilen.Take(kopf).SelectMany(z => z).Select(f => IbanMuster.Match(f.ToUpperInvariant())).FirstOrDefault(m => m.Success && Domain.Gemeinsam.Iban.IstGueltig(m.Value))?.Value;
        var zerlegung = profil.Zerlegung is null ? null : new Regex(profil.Zerlegung, RegexOptions.Singleline, TimeSpan.FromSeconds(1));
        var formate = profil.Datumsformat.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var umsaetze = new List<GelesenerUmsatz>();
        var hinweise = new List<string>();

        foreach (var (zeile, nummer) in zeilen.Skip(kopf + 1).Select((z, i) => (z, i + kopf + 2)))
        {
            string? Wert(string schluessel) => spalten.TryGetValue(schluessel, out var index) && index < zeile.Count && zeile[index].Trim() is { Length: > 0 } w ? w : null;

            if (zeile.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var datumText = Wert(nameof(CsvProfil.SpalteBuchungsdatum));
            if (datumText is null || !DateOnly.TryParseExact(datumText, formate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var buchung))
            {
                // Fußzeilen wie „Kontostand“ oder „Alter Kontostand“
                hinweise.Add($"Zeile {nummer} übersprungen (kein Buchungsdatum).");
                continue;
            }

            if (Wert(nameof(CsvProfil.SpalteStatus)) is { } status && (status.Contains("vorgemerkt", StringComparison.OrdinalIgnoreCase) || status.Contains("offen", StringComparison.OrdinalIgnoreCase)))
            {
                hinweise.Add($"Zeile {nummer} übersprungen (vorgemerkt).");
                continue;
            }

            decimal? betrag = Betrag(Wert(nameof(CsvProfil.SpalteBetrag)), profil.Dezimaltrennzeichen);
            if (betrag is null && (profil.SpalteSoll is not null || profil.SpalteHaben is not null))
            {
                var soll = Betrag(Wert(nameof(CsvProfil.SpalteSoll)), profil.Dezimaltrennzeichen);
                var haben = Betrag(Wert(nameof(CsvProfil.SpalteHaben)), profil.Dezimaltrennzeichen);
                betrag = soll is null && haben is null ? null : (haben ?? 0) - Math.Abs(soll ?? 0);
            }

            if (betrag is null)
            {
                hinweise.Add($"Zeile {nummer} übersprungen (kein Betrag).");
                continue;
            }

            var valuta = Wert(nameof(CsvProfil.SpalteValutadatum)) is { } v && DateOnly.TryParseExact(v, formate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var valutaDatum) ? valutaDatum : (DateOnly?)null;
            var zweckRoh = Wert(nameof(CsvProfil.SpalteVerwendungszweck)) ?? "";
            var name = Wert(nameof(CsvProfil.SpalteName))
                ?? (betrag > 0 ? Wert(nameof(CsvProfil.SpalteNameEingang)) : Wert(nameof(CsvProfil.SpalteNameAusgang)));
            if (zerlegung?.Match(zweckRoh) is { Success: true } teile)
            {
                name ??= teile.Groups["name"].Success ? teile.Groups["name"].Value.Trim() : null;
                zweckRoh = teile.Groups["zweck"].Success ? teile.Groups["zweck"].Value : zweckRoh;
            }

            var sepa = SepaZweck.Zerlegen(zweckRoh);
            var gegenIban = Wert(nameof(CsvProfil.SpalteIban))?.Replace(" ", "", StringComparison.Ordinal);
            umsaetze.Add(new GelesenerUmsatz(
                buchung,
                betrag.Value,
                sepa.Verwendungszweck,
                valuta,
                Wert(nameof(CsvProfil.SpalteWaehrung)) ?? "EUR",
                GegenName: name,
                GegenIban: gegenIban is not null && Domain.Gemeinsam.Iban.IstGueltig(gegenIban) ? gegenIban : null,
                GegenBic: Wert(nameof(CsvProfil.SpalteBic)),
                Buchungstext: Wert(nameof(CsvProfil.SpalteBuchungstext)),
                EndToEndId: Ohne(Wert(nameof(CsvProfil.SpalteEndToEnd))) ?? sepa.EndToEndId,
                Mandatsreferenz: Wert(nameof(CsvProfil.SpalteMandatsreferenz)) ?? sepa.Mandatsreferenz,
                GlaeubigerId: Wert(nameof(CsvProfil.SpalteGlaeubigerId)) ?? sepa.GlaeubigerId));
        }

        return new Kontoauszug(iban is null ? null : Domain.Gemeinsam.Iban.Kompakt(iban), umsaetze, hinweise);
    }

    private static string? Ohne(string? wert) => wert is null or "NOTPROVIDED" ? null : wert;

    /// <summary>„-1.234,56“, „1234,56 €“, „+12,00“ bzw. mit Punkt als Dezimaltrennzeichen.</summary>
    internal static decimal? Betrag(string? text, char dezimal)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var bereinigt = new string(text.Where(c => char.IsAsciiDigit(c) || c is ',' or '.' or '-' or '+').ToArray());
        bereinigt = dezimal == ','
            ? bereinigt.Replace(".", "", StringComparison.Ordinal).Replace(',', '.')
            : bereinigt.Replace(",", "", StringComparison.Ordinal);
        return decimal.TryParse(bereinigt, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var betrag) ? betrag : null;
    }

    /// <summary>Spaltenindex je Profileigenschaft oder <c>null</c>, wenn Pflichtspalten fehlen.</summary>
    private static Dictionary<string, int>? Spalten(CsvProfil profil, List<string> kopf)
    {
        var namen = kopf.Select(Normalisieren).ToList();
        var zuordnung = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (eigenschaft, angabe) in Angaben(profil))
        {
            if (angabe is null)
            {
                continue;
            }

            var index = angabe.Split('|').Select(Normalisieren).Select(n => namen.IndexOf(n)).FirstOrDefault(i => i >= 0, -1);
            if (index >= 0)
            {
                zuordnung[eigenschaft] = index;
            }
        }

        var pflicht = zuordnung.ContainsKey(nameof(CsvProfil.SpalteBuchungsdatum))
            && (zuordnung.ContainsKey(nameof(CsvProfil.SpalteBetrag)) || (zuordnung.ContainsKey(nameof(CsvProfil.SpalteSoll)) && zuordnung.ContainsKey(nameof(CsvProfil.SpalteHaben))))
            && (profil.SpalteVerwendungszweck is null || zuordnung.ContainsKey(nameof(CsvProfil.SpalteVerwendungszweck)));
        return pflicht ? zuordnung : null;
    }

    private static IEnumerable<(string, string?)> Angaben(CsvProfil p) =>
    [
        (nameof(p.SpalteBuchungsdatum), p.SpalteBuchungsdatum),
        (nameof(p.SpalteValutadatum), p.SpalteValutadatum),
        (nameof(p.SpalteBetrag), p.SpalteBetrag),
        (nameof(p.SpalteSoll), p.SpalteSoll),
        (nameof(p.SpalteHaben), p.SpalteHaben),
        (nameof(p.SpalteName), p.SpalteName),
        (nameof(p.SpalteNameEingang), p.SpalteNameEingang),
        (nameof(p.SpalteNameAusgang), p.SpalteNameAusgang),
        (nameof(p.SpalteIban), p.SpalteIban),
        (nameof(p.SpalteBic), p.SpalteBic),
        (nameof(p.SpalteVerwendungszweck), p.SpalteVerwendungszweck),
        (nameof(p.SpalteBuchungstext), p.SpalteBuchungstext),
        (nameof(p.SpalteEndToEnd), p.SpalteEndToEnd),
        (nameof(p.SpalteMandatsreferenz), p.SpalteMandatsreferenz),
        (nameof(p.SpalteGlaeubigerId), p.SpalteGlaeubigerId),
        (nameof(p.SpalteWaehrung), p.SpalteWaehrung),
        (nameof(p.SpalteStatus), p.SpalteStatus),
    ];

    private static string Normalisieren(string name) => name.Trim().Trim('"').Trim().ToLowerInvariant();

    /// <summary>Zerlegt CSV nach RFC 4180 (Anführungszeichen, Zeilenumbrüche in Feldern).</summary>
    internal static IEnumerable<List<string>> Zeilen(string text, char trenner)
    {
        var feld = new StringBuilder();
        var zeile = new List<string>();
        var inAnfuehrung = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inAnfuehrung)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    feld.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    inAnfuehrung = false;
                }
                else
                {
                    feld.Append(c);
                }
            }
            else if (c == '"')
            {
                inAnfuehrung = true;
            }
            else if (c == trenner)
            {
                zeile.Add(feld.ToString());
                feld.Clear();
            }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                zeile.Add(feld.ToString());
                feld.Clear();
                yield return zeile;
                zeile = [];
            }
            else
            {
                feld.Append(c);
            }
        }

        if (feld.Length > 0 || zeile.Count > 0)
        {
            zeile.Add(feld.ToString());
            yield return zeile;
        }
    }
}

/// <summary>Mitgelieferte Profile (Stand der Exporte 2024–2026).</summary>
internal static class CsvVorgaben
{
    public static IReadOnlyList<CsvProfil> Alle { get; } =
    [
        new("Sparkasse (CSV-CAMT)", "Buchungstag")
        {
            Vorgabe = true,
            Datumsformat = "dd.MM.yy|dd.MM.yyyy",
            SpalteValutadatum = "Valutadatum",
            SpalteBetrag = "Betrag",
            SpalteName = "Beguenstigter/Zahlungspflichtiger|Begünstigter/Zahlungspflichtiger",
            SpalteIban = "Kontonummer/IBAN|Kontonummer",
            SpalteBic = "BIC (SWIFT-Code)",
            SpalteVerwendungszweck = "Verwendungszweck",
            SpalteBuchungstext = "Buchungstext",
            SpalteEndToEnd = "Kundenreferenz (End-to-End)",
            SpalteMandatsreferenz = "Mandatsreferenz",
            SpalteGlaeubigerId = "Glaeubiger ID|Gläubiger ID",
            SpalteWaehrung = "Waehrung|Währung",
            SpalteStatus = "Info",
        },
        new("Volksbank / Raiffeisenbank", "Buchungstag")
        {
            Vorgabe = true,
            SpalteValutadatum = "Valutadatum",
            SpalteBetrag = "Betrag",
            SpalteName = "Name Zahlungsbeteiligter",
            SpalteIban = "IBAN Zahlungsbeteiligter",
            SpalteBic = "BIC (SWIFT-Code) Zahlungsbeteiligter",
            SpalteVerwendungszweck = "Verwendungszweck",
            SpalteBuchungstext = "Buchungstext",
            SpalteMandatsreferenz = "Mandatsreferenz",
            SpalteGlaeubigerId = "Glaeubiger ID|Gläubiger ID",
            SpalteWaehrung = "Waehrung|Währung",
        },
        new("DKB", "Buchungsdatum")
        {
            Vorgabe = true,
            Datumsformat = "dd.MM.yy|dd.MM.yyyy",
            SpalteValutadatum = "Wertstellung",
            SpalteBetrag = "Betrag (€)|Betrag (EUR)",
            SpalteNameEingang = "Zahlungspflichtige*r",
            SpalteNameAusgang = "Zahlungsempfänger*in",
            SpalteIban = "IBAN",
            SpalteVerwendungszweck = "Verwendungszweck",
            SpalteBuchungstext = "Umsatztyp",
            SpalteEndToEnd = "Kundenreferenz",
            SpalteMandatsreferenz = "Mandatsreferenz",
            SpalteGlaeubigerId = "Gläubiger-ID",
            SpalteStatus = "Status",
        },
        new("ING", "Buchung|Buchungsdatum")
        {
            Vorgabe = true,
            SpalteValutadatum = "Wertstellungsdatum|Valuta",
            SpalteBetrag = "Betrag",
            SpalteName = "Auftraggeber/Empfänger",
            SpalteVerwendungszweck = "Verwendungszweck",
            SpalteBuchungstext = "Buchungstext",
            SpalteWaehrung = "Währung",
        },
        new("comdirect", "Buchungstag")
        {
            Vorgabe = true,
            SpalteValutadatum = "Wertstellung (Valuta)",
            SpalteBetrag = "Umsatz in EUR",
            SpalteVerwendungszweck = "Buchungstext",
            SpalteBuchungstext = "Vorgang",
            Zerlegung = @"^(?:(?:Auftraggeber|Empfänger):\s*(?<name>.*?)\s+)?Buchungstext:\s*(?<zweck>.*?)(?:\s+Ref\.\s.*)?$",
        },
    ];
}
