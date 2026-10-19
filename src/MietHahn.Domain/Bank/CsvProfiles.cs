using MietHahn.Domain.Common;

namespace MietHahn.Domain.Bank;

/// <summary>
/// Description of a bank's CSV export. Columns are found by their header (several names separated by "|"),
/// preamble lines before the header are skipped.
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

    /// <summary>"auto" (UTF-8, otherwise Windows-1252), "utf-8" or "windows-1252".</summary>
    public string Charset { get; set; } = "auto";

    /// <summary>Several formats separated by "|", e.g. "dd.MM.yyyy|dd.MM.yy".</summary>
    public string DateFormat { get; set; } = "dd.MM.yyyy|dd.MM.yy";

    /// <summary>"," (German, thousands separator period) or ".".</summary>
    public char DecimalSeparator { get; set; } = ',';

    public string BookingDateColumn { get; set; }

    public string? ValueDateColumn { get; set; }

    /// <summary>Signed amount; alternatively separate debit/credit columns.</summary>
    public string? AmountColumn { get; set; }

    public string? DebitColumn { get; set; }

    public string? CreditColumn { get; set; }

    public string? NameColumn { get; set; }

    /// <summary>Name of the counterparty by direction (e.g. DKB: Zahlungspflichtige*r / Zahlungsempfänger*in).</summary>
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

    /// <summary>Optional status column; rows with "vorgemerkt" or "offen" are skipped.</summary>
    public string? StatusColumn { get; set; }

    /// <summary>
    /// Optional regular expression on the payment purpose with groups <c>name</c> and <c>zweck</c>, if the bank
    /// delivers both in one column (e.g. comdirect "Auftraggeber: … Buchungstext: …").
    /// </summary>
    public string? ParsePattern { get; set; }

    /// <summary>Bundled profile (not editable).</summary>
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
