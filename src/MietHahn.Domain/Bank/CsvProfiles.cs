using MietHahn.Domain.Common;

namespace MietHahn.Domain.Bank;

/// <summary>
/// Beschreibung eines CSV-Exports einer Bank. Spalten werden über ihre Überschrift gefunden (mehrere Namen mit „|“
/// getrennt), Vorspann-Zeilen vor der Überschrift werden übersprungen.
/// </summary>
public sealed class CsvProfile : Entity
{
    private CsvProfile()
    {
        Name = "";
        BookingDateColumn = "";
    }

    public CsvProfile(string name, string bookingDateColumn)
    {
        Name = name;
        BookingDateColumn = bookingDateColumn;
    }

    public string Name { get; set; }

    public char Delimiter { get; set; } = ';';

    /// <summary>„auto“ (UTF-8, sonst Windows-1252), „utf-8“ oder „windows-1252“.</summary>
    public string Charset { get; set; } = "auto";

    /// <summary>Mehrere Formate mit „|“, z. B. „dd.MM.yyyy|dd.MM.yy“.</summary>
    public string DateFormat { get; set; } = "dd.MM.yyyy|dd.MM.yy";

    /// <summary>„,“ (deutsch, Tausenderpunkt) oder „.“.</summary>
    public char DecimalSeparator { get; set; } = ',';

    public string BookingDateColumn { get; set; }

    public string? ValueDateColumn { get; set; }

    /// <summary>Betrag mit Vorzeichen; alternativ getrennte Soll-/Haben-Spalten.</summary>
    public string? AmountColumn { get; set; }

    public string? DebitColumn { get; set; }

    public string? CreditColumn { get; set; }

    public string? NameColumn { get; set; }

    /// <summary>Name der Gegenseite getrennt nach Richtung (z. B. DKB: Zahlungspflichtige*r / Zahlungsempfänger*in).</summary>
    public string? IncomingNameColumn { get; set; }

    public string? OutgoingNameColumn { get; set; }

    public string? IbanColumn { get; set; }

    public string? BicColumn { get; set; }

    public string? PurposeColumn { get; set; }

    public string? BookingTextColumn { get; set; }

    public string? EndToEndColumn { get; set; }

    public string? MandateReferenceColumn { get; set; }

    public string? CreditorIdColumn { get; set; }

    public string? CurrencyColumn { get; set; }

    /// <summary>Optionale Spalte mit Status; Zeilen mit „vorgemerkt“ bzw. „offen“ werden übersprungen.</summary>
    public string? StatusColumn { get; set; }

    /// <summary>
    /// Optionaler regulärer Ausdruck auf den Verwendungszweck mit Gruppen <c>name</c> und <c>zweck</c>, wenn die Bank
    /// beides in einer Spalte liefert (z. B. comdirect „Auftraggeber: … Buchungstext: …“).
    /// </summary>
    public string? ParsePattern { get; set; }

    /// <summary>Mitgeliefertes Profil (nicht änderbar).</summary>
    public bool IsDefault { get; set; }

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(BookingDateColumn);
        if (AmountColumn is null && (DebitColumn is null || CreditColumn is null))
        {
            throw new RuleViolationException("Bitte eine Betragsspalte oder Soll- und Haben-Spalte angeben.");
        }

        if (DecimalSeparator is not (',' or '.'))
        {
            throw new RuleViolationException("Dezimaltrennzeichen ist „,“ oder „.“.");
        }
    }
}
