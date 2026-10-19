using MietHahn.Domain.Gemeinsam;

namespace MietHahn.Domain.Bank;

/// <summary>
/// Beschreibung eines CSV-Exports einer Bank. Spalten werden über ihre Überschrift gefunden (mehrere Namen mit „|“
/// getrennt), Vorspann-Zeilen vor der Überschrift werden übersprungen.
/// </summary>
public sealed class CsvProfil : Entitaet
{
    private CsvProfil()
    {
        Name = "";
        SpalteBuchungsdatum = "";
    }

    public CsvProfil(string name, string spalteBuchungsdatum)
    {
        Name = name;
        SpalteBuchungsdatum = spalteBuchungsdatum;
    }

    public string Name { get; set; }

    public char Trennzeichen { get; set; } = ';';

    /// <summary>„auto“ (UTF-8, sonst Windows-1252), „utf-8“ oder „windows-1252“.</summary>
    public string Zeichensatz { get; set; } = "auto";

    /// <summary>Mehrere Formate mit „|“, z. B. „dd.MM.yyyy|dd.MM.yy“.</summary>
    public string Datumsformat { get; set; } = "dd.MM.yyyy|dd.MM.yy";

    /// <summary>„,“ (deutsch, Tausenderpunkt) oder „.“.</summary>
    public char Dezimaltrennzeichen { get; set; } = ',';

    public string SpalteBuchungsdatum { get; set; }

    public string? SpalteValutadatum { get; set; }

    /// <summary>Betrag mit Vorzeichen; alternativ getrennte Soll-/Haben-Spalten.</summary>
    public string? SpalteBetrag { get; set; }

    public string? SpalteSoll { get; set; }

    public string? SpalteHaben { get; set; }

    public string? SpalteName { get; set; }

    /// <summary>Name der Gegenseite getrennt nach Richtung (z. B. DKB: Zahlungspflichtige*r / Zahlungsempfänger*in).</summary>
    public string? SpalteNameEingang { get; set; }

    public string? SpalteNameAusgang { get; set; }

    public string? SpalteIban { get; set; }

    public string? SpalteBic { get; set; }

    public string? SpalteVerwendungszweck { get; set; }

    public string? SpalteBuchungstext { get; set; }

    public string? SpalteEndToEnd { get; set; }

    public string? SpalteMandatsreferenz { get; set; }

    public string? SpalteGlaeubigerId { get; set; }

    public string? SpalteWaehrung { get; set; }

    /// <summary>Optionale Spalte mit Status; Zeilen mit „vorgemerkt“ bzw. „offen“ werden übersprungen.</summary>
    public string? SpalteStatus { get; set; }

    /// <summary>
    /// Optionaler regulärer Ausdruck auf den Verwendungszweck mit Gruppen <c>name</c> und <c>zweck</c>, wenn die Bank
    /// beides in einer Spalte liefert (z. B. comdirect „Auftraggeber: … Buchungstext: …“).
    /// </summary>
    public string? Zerlegung { get; set; }

    /// <summary>Mitgeliefertes Profil (nicht änderbar).</summary>
    public bool Vorgabe { get; set; }

    public void Pruefen()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(SpalteBuchungsdatum);
        if (SpalteBetrag is null && (SpalteSoll is null || SpalteHaben is null))
        {
            throw new RegelverletzungException("Bitte eine Betragsspalte oder Soll- und Haben-Spalte angeben.");
        }

        if (Dezimaltrennzeichen is not (',' or '.'))
        {
            throw new RegelverletzungException("Dezimaltrennzeichen ist „,“ oder „.“.");
        }
    }
}
