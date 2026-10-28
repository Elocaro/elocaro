using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MietHahn.Domain.Bank;
using MietHahn.Infrastructure.Common;

namespace MietHahn.Infrastructure.Bank.Import;

/// <summary>CSV transactions according to a <see cref="CsvProfile"/>; preamble lines before the header are skipped.</summary>
internal static class CsvParser
{
    private static readonly Regex IbanPattern = new(@"\b[A-Z]{2}\d{2}(?: ?[A-Z0-9]){11,30}\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Number of columns found if all required columns are in one header line, otherwise 0. Several profiles
    /// may match (same column names); the one with the most hits describes the file most accurately.
    /// </summary>
    public static int Hits(CsvProfile profile, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var lines = CsvText.Rows(TextDecoding.Read(content, profile.Charset), profile.Delimiter).Take(40);
        return lines.Select(z => Columns(profile, z)?.Count ?? 0).DefaultIfEmpty(0).Max();
    }

    public static BankStatement Read(CsvProfile profile, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var text = TextDecoding.Read(content, profile.Charset);
        var lines = CsvText.Rows(text, profile.Delimiter).ToList();
        var header = lines.FindIndex(z => Columns(profile, z) is not null);
        if (header < 0)
        {
            throw new BankFileErrorException($"Die Spaltenüberschriften passen nicht zum Profil „{profile.Name}“.");
        }

        var columns = Columns(profile, lines[header])!;
        // The preamble often contains the account's IBAN (ING, DKB).
        var iban = lines.Take(header).SelectMany(z => z).Select(f => IbanPattern.Match(f.ToUpperInvariant())).FirstOrDefault(m => m.Success && Domain.Common.Iban.IsValid(m.Value))?.Value;
        var split = profile.ParsePattern is null ? null : new Regex(profile.ParsePattern, RegexOptions.Singleline, TimeSpan.FromSeconds(1));
        var formats = profile.DateFormat.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var transactions = new List<ParsedTransaction>();
        var hints = new List<string>();

        foreach (var (line, number) in lines.Skip(header + 1).Select((z, i) => (z, i + header + 2)))
        {
            string? Value(string key) => columns.TryGetValue(key, out var index) && index < line.Count && line[index].Trim() is { Length: > 0 } w ? w : null;

            if (line.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var dateText = Value(nameof(CsvProfile.BookingDateColumn));
            if (dateText is null || !DateOnly.TryParseExact(dateText, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var booking))
            {
                // Footer lines such as „Kontostand“ or „Alter Kontostand“
                hints.Add($"Zeile {number} übersprungen (kein Buchungsdatum).");
                continue;
            }

            if (Value(nameof(CsvProfile.StatusColumn)) is { } status && (status.Contains("vorgemerkt", StringComparison.OrdinalIgnoreCase) || status.Contains("offen", StringComparison.OrdinalIgnoreCase)))
            {
                hints.Add($"Zeile {number} übersprungen (vorgemerkt).");
                continue;
            }

            decimal? amount = CsvText.Amount(Value(nameof(CsvProfile.AmountColumn)), profile.DecimalSeparator);
            if (amount is null && (profile.DebitColumn is not null || profile.CreditColumn is not null))
            {
                var target = CsvText.Amount(Value(nameof(CsvProfile.DebitColumn)), profile.DecimalSeparator);
                var creditItems = CsvText.Amount(Value(nameof(CsvProfile.CreditColumn)), profile.DecimalSeparator);
                amount = target is null && creditItems is null ? null : (creditItems ?? 0) - Math.Abs(target ?? 0);
            }

            if (amount is null)
            {
                hints.Add($"Zeile {number} übersprungen (kein Betrag).");
                continue;
            }

            var valueDate = Value(nameof(CsvProfile.ValueDateColumn)) is { } v && DateOnly.TryParseExact(v, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var valueDateRaw) ? valueDateRaw : (DateOnly?)null;
            var purposeRaw = Value(nameof(CsvProfile.PurposeColumn)) ?? "";
            var name = Value(nameof(CsvProfile.NameColumn))
                ?? (amount > 0 ? Value(nameof(CsvProfile.IncomingNameColumn)) : Value(nameof(CsvProfile.OutgoingNameColumn)));
            if (split?.Match(purposeRaw) is { Success: true } parts)
            {
                name ??= parts.Groups["name"].Success ? parts.Groups["name"].Value.Trim() : null;
                purposeRaw = parts.Groups["zweck"].Success ? parts.Groups["zweck"].Value : purposeRaw;
            }

            var sepa = SepaPurpose.Parse(purposeRaw);
            var counterpartyIban = Value(nameof(CsvProfile.IbanColumn))?.Replace(" ", "", StringComparison.Ordinal);
            transactions.Add(new ParsedTransaction(
                booking,
                amount.Value,
                sepa.Purpose,
                valueDate,
                Value(nameof(CsvProfile.CurrencyColumn)) ?? "EUR",
                CounterpartyName: name,
                CounterpartyIban: counterpartyIban is not null && Domain.Common.Iban.IsValid(counterpartyIban) ? counterpartyIban : null,
                CounterpartyBic: Value(nameof(CsvProfile.BicColumn)),
                BookingText: Value(nameof(CsvProfile.BookingTextColumn)),
                EndToEndId: NullIfNotProvided(Value(nameof(CsvProfile.EndToEndColumn))) ?? sepa.EndToEndId,
                MandateReference: Value(nameof(CsvProfile.MandateReferenceColumn)) ?? sepa.MandateReference,
                CreditorId: Value(nameof(CsvProfile.CreditorIdColumn)) ?? sepa.CreditorId));
        }

        return new BankStatement(iban is null ? null : Domain.Common.Iban.Compact(iban), transactions, hints);
    }

    private static string? NullIfNotProvided(string? value) => value is null or "NOTPROVIDED" ? null : value;


    /// <summary>Column index per profile property, or <c>null</c>, if required columns are missing.</summary>
    private static Dictionary<string, int>? Columns(CsvProfile profile, List<string> header)
    {
        var names = header.Select(Normalize).ToList();
        var assignment = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (property, value) in Fields(profile))
        {
            if (value is null)
            {
                continue;
            }

            var index = value.Split('|').Select(Normalize).Select(n => names.IndexOf(n)).FirstOrDefault(i => i >= 0, -1);
            if (index >= 0)
            {
                assignment[property] = index;
            }
        }

        var required = assignment.ContainsKey(nameof(CsvProfile.BookingDateColumn))
            && (assignment.ContainsKey(nameof(CsvProfile.AmountColumn)) || (assignment.ContainsKey(nameof(CsvProfile.DebitColumn)) && assignment.ContainsKey(nameof(CsvProfile.CreditColumn))))
            && (profile.PurposeColumn is null || assignment.ContainsKey(nameof(CsvProfile.PurposeColumn)));
        return required ? assignment : null;
    }

    private static IEnumerable<(string, string?)> Fields(CsvProfile p) =>
    [
        (nameof(p.BookingDateColumn), p.BookingDateColumn),
        (nameof(p.ValueDateColumn), p.ValueDateColumn),
        (nameof(p.AmountColumn), p.AmountColumn),
        (nameof(p.DebitColumn), p.DebitColumn),
        (nameof(p.CreditColumn), p.CreditColumn),
        (nameof(p.NameColumn), p.NameColumn),
        (nameof(p.IncomingNameColumn), p.IncomingNameColumn),
        (nameof(p.OutgoingNameColumn), p.OutgoingNameColumn),
        (nameof(p.IbanColumn), p.IbanColumn),
        (nameof(p.BicColumn), p.BicColumn),
        (nameof(p.PurposeColumn), p.PurposeColumn),
        (nameof(p.BookingTextColumn), p.BookingTextColumn),
        (nameof(p.EndToEndColumn), p.EndToEndColumn),
        (nameof(p.MandateReferenceColumn), p.MandateReferenceColumn),
        (nameof(p.CreditorIdColumn), p.CreditorIdColumn),
        (nameof(p.CurrencyColumn), p.CurrencyColumn),
        (nameof(p.StatusColumn), p.StatusColumn),
    ];

    private static string Normalize(string name) => name.Trim().Trim('"').Trim().ToLowerInvariant();
}

/// <summary>Bundled profiles (state of the exports 2024–2026).</summary>
internal static class CsvDefaults
{
    public static IReadOnlyList<CsvProfile> All { get; } =
    [
        new("Sparkasse (CSV-CAMT)", "Buchungstag")
        {
            IsDefault = true,
            DateFormat = "dd.MM.yy|dd.MM.yyyy",
            ValueDateColumn = "Valutadatum",
            AmountColumn = "Betrag",
            NameColumn = "Beguenstigter/Zahlungspflichtiger|Begünstigter/Zahlungspflichtiger",
            IbanColumn = "Kontonummer/IBAN|Kontonummer",
            BicColumn = "BIC (SWIFT-Code)",
            PurposeColumn = "Verwendungszweck",
            BookingTextColumn = "Buchungstext",
            EndToEndColumn = "Kundenreferenz (End-to-End)",
            MandateReferenceColumn = "Mandatsreferenz",
            CreditorIdColumn = "Glaeubiger ID|Gläubiger ID",
            CurrencyColumn = "Waehrung|Währung",
            StatusColumn = "Info",
        },
        new("Volksbank / Raiffeisenbank", "Buchungstag")
        {
            IsDefault = true,
            ValueDateColumn = "Valutadatum",
            AmountColumn = "Betrag",
            NameColumn = "Name Zahlungsbeteiligter",
            IbanColumn = "IBAN Zahlungsbeteiligter",
            BicColumn = "BIC (SWIFT-Code) Zahlungsbeteiligter",
            PurposeColumn = "Verwendungszweck",
            BookingTextColumn = "Buchungstext",
            MandateReferenceColumn = "Mandatsreferenz",
            CreditorIdColumn = "Glaeubiger ID|Gläubiger ID",
            CurrencyColumn = "Waehrung|Währung",
        },
        new("DKB", "Buchungsdatum")
        {
            IsDefault = true,
            DateFormat = "dd.MM.yy|dd.MM.yyyy",
            ValueDateColumn = "Wertstellung",
            AmountColumn = "Betrag (€)|Betrag (EUR)",
            IncomingNameColumn = "Zahlungspflichtige*r",
            OutgoingNameColumn = "Zahlungsempfänger*in",
            IbanColumn = "IBAN",
            PurposeColumn = "Verwendungszweck",
            BookingTextColumn = "Umsatztyp",
            EndToEndColumn = "Kundenreferenz",
            MandateReferenceColumn = "Mandatsreferenz",
            CreditorIdColumn = "Gläubiger-ID",
            StatusColumn = "Status",
        },
        new("ING", "Buchung|Buchungsdatum")
        {
            IsDefault = true,
            ValueDateColumn = "Wertstellungsdatum|Valuta",
            AmountColumn = "Betrag",
            NameColumn = "Auftraggeber/Empfänger",
            PurposeColumn = "Verwendungszweck",
            BookingTextColumn = "Buchungstext",
            CurrencyColumn = "Währung",
        },
        new("comdirect", "Buchungstag")
        {
            IsDefault = true,
            ValueDateColumn = "Wertstellung (Valuta)",
            AmountColumn = "Umsatz in EUR",
            PurposeColumn = "Buchungstext",
            BookingTextColumn = "Vorgang",
            ParsePattern = @"^(?:(?:Auftraggeber|Empfänger):\s*(?<name>.*?)\s+)?Buchungstext:\s*(?<zweck>.*?)(?:\s+Ref\.\s.*)?$",
        },
        new("Commerzbank", "Buchungstag")
        {
            IsDefault = true,
            ValueDateColumn = "Wertstellung",
            AmountColumn = "Betrag",
            PurposeColumn = "Buchungstext",
            BookingTextColumn = "Umsatzart",
            CurrencyColumn = "Währung|Waehrung",
            ParsePattern = @"^(?:(?:Auftraggeber|Empfänger):\s*(?<name>.*?)\s+)?Buchungstext:\s*(?<zweck>.*?)(?:\s+Ref\.\s.*)?$",
        },
        new("Deutsche Bank", "Buchungstag")
        {
            IsDefault = true,
            ValueDateColumn = "Wert",
            AmountColumn = "Betrag",
            DebitColumn = "Soll",
            CreditColumn = "Haben",
            NameColumn = "Begünstigter / Auftraggeber|Beguenstigter / Auftraggeber",
            IbanColumn = "IBAN",
            BicColumn = "BIC",
            PurposeColumn = "Verwendungszweck",
            BookingTextColumn = "Umsatzart",
            EndToEndColumn = "Kundenreferenz",
            MandateReferenceColumn = "Mandatsreferenz",
            CreditorIdColumn = "Gläubiger ID|Glaeubiger ID",
            CurrencyColumn = "Währung|Waehrung",
        },
        new("N26", "Booking Date|Datum")
        {
            IsDefault = true,
            Delimiter = ',',
            DateFormat = "yyyy-MM-dd",
            DecimalSeparator = '.',
            ValueDateColumn = "Value Date",
            AmountColumn = "Amount (EUR)|Betrag (EUR)",
            NameColumn = "Partner Name|Empfänger",
            IbanColumn = "Partner Iban|Kontonummer",
            PurposeColumn = "Payment Reference|Verwendungszweck",
            BookingTextColumn = "Type|Transaktionstyp",
        },
    ];
}
