using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using MietHahn.Domain.Bank;

namespace MietHahn.Infrastructure.Bank.Import;

/// <summary>
/// ISO 20022 camt.053 (bank statement), tested with .001.02 and .001.08. Elements are read by their local name
/// so that all versions work. Pending transactions (status PDNG) are skipped; batch bookings
/// with several transaction details yield one transaction per detail.
/// </summary>
internal static class Camt053Parser
{
    public static bool Detects(byte[] content)
    {
        var start = TextDecoding.Read(content.Length > 4096 ? content[..4096] : content);
        return start.Contains("camt.053", StringComparison.Ordinal) || start.Contains("BkToCstmrStmt", StringComparison.Ordinal);
    }

    public static BankStatement Read(byte[] content)
    {
        XDocument document;
        try
        {
            using var stream = new MemoryStream(content);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            document = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            throw new BankFileErrorException($"Die CAMT-Datei ist kein gültiges XML: {ex.Message}");
        }

        var statements = All(document.Root!, "Stmt").ToList();
        if (statements.Count == 0)
        {
            throw new BankFileErrorException("Die Datei enthält keinen camt.053-Kontoauszug (Stmt).");
        }

        var iban = Text(statements[0], "Acct", "Id", "IBAN");
        var transactions = new List<ParsedTransaction>();
        var hints = new List<string>();
        foreach (var entry in statements.SelectMany(a => Children(a, "Ntry")))
        {
            var status = Text(entry, "Sts", "Cd") ?? Text(entry, "Sts");
            if (status is not null and not "BOOK")
            {
                hints.Add($"Nicht gebuchter Umsatz übersprungen ({status}).");
                continue;
            }

            var booking = Date(Kind(entry, "BookgDt")) ?? throw new BankFileErrorException("Ein Umsatz hat kein Buchungsdatum.");
            var valueDate = Date(Kind(entry, "ValDt"));
            var incoming = Text(entry, "CdtDbtInd") == "CRDT";
            var amountEntry = Amount(Kind(entry, "Amt"));
            var currency = Kind(entry, "Amt")?.Attribute("Ccy")?.Value ?? "EUR";
            var bookingText = Text(entry, "AddtlNtryInf");
            var details = All(entry, "TxDtls").ToList();
            if (details.Count == 0)
            {
                transactions.Add(new ParsedTransaction(booking, incoming ? amountEntry : -amountEntry, SepaPurpose.Clean(bookingText ?? ""), valueDate, currency, BookingText: bookingText));
                continue;
            }

            foreach (var detail in details)
            {
                // For batch bookings, each detail has its own amount.
                var amountDetail = details.Count == 1 ? amountEntry : Amount(Path(detail, "AmtDtls", "TxAmt", "Amt") ?? Kind(detail, "Amt"));
                var incomingDetail = Text(detail, "CdtDbtInd") is { } ind ? ind == "CRDT" : incoming;
                var counter = incomingDetail ? "Dbtr" : "Cdtr";
                var party = Path(detail, "RltdPties", counter);
                var purpose = string.Join(" ", All(detail, "Ustrd").Select(u => u.Value));
                var sepa = SepaPurpose.Parse(purpose);
                transactions.Add(new ParsedTransaction(
                    booking,
                    incomingDetail ? amountDetail : -amountDetail,
                    sepa.Purpose,
                    valueDate,
                    currency,
                    CounterpartyName: party is null ? null : All(party, "Nm").FirstOrDefault()?.Value.Trim(),
                    CounterpartyIban: Text(detail, "RltdPties", counter + "Acct", "Id", "IBAN"),
                    CounterpartyBic: Path(detail, "RltdAgts", counter + "Agt", "FinInstnId") is { } institution ? (Text(institution, "BICFI") ?? Text(institution, "BIC")) : null,
                    BookingText: bookingText,
                    EndToEndId: NullIfNotProvided(Text(detail, "Refs", "EndToEndId")) ?? sepa.EndToEndId,
                    MandateReference: NullIfNotProvided(Text(detail, "Refs", "MndtId")) ?? sepa.MandateReference,
                    CreditorId: Creditor(detail) ?? sepa.CreditorId));
            }
        }

        return new BankStatement(iban, transactions, hints);
    }

    // .001.02: Cdtr/Id/PrvtId/Othr/Id, .001.08: Cdtr/Pty/Id/PrvtId/Othr/Id
    private static string? Creditor(XElement detail) =>
        Path(detail, "RltdPties", "Cdtr") is { } creditor && All(creditor, "PrvtId").FirstOrDefault() is { } isPrivate ? Text(isPrivate, "Othr", "Id") : null;

    private static string? NullIfNotProvided(string? value) => string.IsNullOrWhiteSpace(value) || value == "NOTPROVIDED" ? null : value.Trim();

    private static decimal Amount(XElement? element) =>
        element is not null && decimal.TryParse(element.Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : throw new BankFileErrorException("Ein Umsatz hat keinen lesbaren Betrag.");

    private static DateOnly? Date(XElement? element)
    {
        var text = element is null ? null : Text(element, "Dt") ?? Text(element, "DtTm");
        return text is null ? null : DateOnly.ParseExact(text[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static XElement? Kind(XElement element, string name) => element.Elements().FirstOrDefault(e => e.Name.LocalName == name);

    private static IEnumerable<XElement> Children(XElement element, string name) => element.Elements().Where(e => e.Name.LocalName == name);

    private static IEnumerable<XElement> All(XElement element, string name) => element.Descendants().Where(e => e.Name.LocalName == name);

    private static XElement? Path(XElement element, params string[] names)
    {
        XElement? current = element;
        foreach (var name in names)
        {
            current = current is null ? null : Kind(current, name);
        }

        return current;
    }

    private static string? Text(XElement element, params string[] names) => Path(element, names)?.Value.Trim() is { Length: > 0 } value ? value : null;
}
