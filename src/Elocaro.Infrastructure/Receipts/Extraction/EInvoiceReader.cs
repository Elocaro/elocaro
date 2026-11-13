using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Elocaro.Application.Receipts;
using Elocaro.Domain.Common;
using Elocaro.Domain.Receipts;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;

namespace Elocaro.Infrastructure.Receipts.Extraction;

/// <summary>
/// Reads an electronic invoice without any AI: a ZUGFeRD/Factur-X PDF (the XML is attached to the PDF) or an XRechnung/UBL or CII XML.
/// Everything stays on the own server. A file that is not an e-invoice, is broken or uses an unknown structure gives <c>null</c>
/// and the normal extraction takes over.
/// </summary>
internal static class EInvoiceReader
{
    private const long MaxXmlBytes = 8L * 1024 * 1024;

    /// <summary>The names under which the invoice XML is attached to a ZUGFeRD / Factur-X PDF (any case).</summary>
    private static readonly string[] AttachmentNames = ["factur-x.xml", "zugferd-invoice.xml", "xrechnung.xml"];

    /// <summary>The suggestion read from the document, or <c>null</c> if it carries no readable e-invoice.</summary>
    public static ReceiptSuggestion? TryRead(ExtractionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        try
        {
            var bytes = document.Content.ToArray();
            if (document.MimeType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
            {
                return EmbeddedXml(bytes) is { } xml ? Parse(xml) : null;
            }

            return document.MimeType is "application/xml" or "text/xml" ? Parse(bytes) : null;
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException or FormatException or ArgumentException or IOException or NotSupportedException or OverflowException or PdfDocumentFormatException or KeyNotFoundException or IndexOutOfRangeException or NullReferenceException)
        {
            return null; // damaged or unknown: fall back to the normal reading
        }
    }

    /// <summary>The XML attached to a ZUGFeRD / Factur-X PDF; <c>null</c> if there is none.</summary>
    public static byte[]? EmbeddedXml(byte[] pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        using var document = PdfDocument.Open(pdf);
        if (!document.Advanced.TryGetEmbeddedFiles(out var files))
        {
            return null;
        }

        foreach (var file in files)
        {
            if (AttachmentNames.Contains(file.Name, StringComparer.OrdinalIgnoreCase))
            {
                var bytes = file.Bytes.ToArray();
                return bytes.Length > 0 && bytes.LongLength <= MaxXmlBytes ? bytes : null;
            }
        }

        return null;
    }

    /// <summary>Reads a CII (ZUGFeRD, Factur-X, XRechnung CII) or UBL (XRechnung UBL) invoice; <c>null</c> for any other XML.</summary>
    public static ReceiptSuggestion? Parse(byte[] xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        if (xml.Length > MaxXmlBytes)
        {
            return null;
        }

        using var stream = new MemoryStream(xml);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var root = XDocument.Load(reader).Root;
        return root?.Name.LocalName switch
        {
            "CrossIndustryInvoice" => FromCii(root),
            "Invoice" or "CreditNote" => FromUbl(root),
            _ => null,
        };
    }

    // ---- CII (UN/CEFACT) -------------------------------------------------------------------------------------------

    private static ReceiptSuggestion? FromCii(XElement root)
    {
        var document = Child(root, "ExchangedDocument");
        var trade = Child(root, "SupplyChainTradeTransaction");
        var settlement = Child(trade, "ApplicableHeaderTradeSettlement");
        var sums = Child(settlement, "SpecifiedTradeSettlementHeaderMonetarySummation");
        var period = Child(settlement, "BillingSpecifiedPeriod");
        var isCredit = Text(document, "TypeCode") == "381";

        var lines = trade?.Elements().Where(e => e.Name.LocalName == "IncludedSupplyChainTradeLineItem").Select(line => new SuggestedLine(
            Text(Child(line, "SpecifiedTradeProduct"), "Name") ?? "Position",
            Amount(Child(Child(line, "SpecifiedLineTradeSettlement"), "SpecifiedTradeSettlementLineMonetarySummation"), "LineTotalAmount"),
            null)).ToList() ?? [];

        return Build(
            supplier: Text(Child(Child(trade, "ApplicableHeaderTradeAgreement"), "SellerTradeParty"), "Name"),
            iban: Text(Child(Child(settlement, "SpecifiedTradeSettlementPaymentMeans"), "PayeeSpecifiedCreditorFinancialAccount"), "IBANID"),
            number: Text(document, "ID"),
            invoiceDate: DateOf(Child(document, "IssueDateTime")),
            serviceFrom: DateOf(Child(period, "StartDateTime")),
            serviceTo: DateOf(Child(period, "EndDateTime")),
            dueOn: DateOf(Child(Child(settlement, "SpecifiedTradePaymentTerms"), "DueDateDateTime")),
            gross: Amount(sums, "GrandTotalAmount"),
            net: Amount(sums, "TaxBasisTotalAmount"),
            vat: Amount(sums, "TaxTotalAmount"),
            currency: Text(settlement, "InvoiceCurrencyCode"),
            isCredit,
            lines);
    }

    // ---- UBL (OASIS) -----------------------------------------------------------------------------------------------

    private static ReceiptSuggestion? FromUbl(XElement root)
    {
        var supplier = Child(Child(root, "AccountingSupplierParty"), "Party");
        var totals = Child(root, "LegalMonetaryTotal");
        var period = Child(root, "InvoicePeriod");
        var isCredit = root.Name.LocalName == "CreditNote" || Text(root, "InvoiceTypeCode") == "381";

        var lines = root.Elements().Where(e => e.Name.LocalName is "InvoiceLine" or "CreditNoteLine").Select(line => new SuggestedLine(
            Text(Child(line, "Item"), "Name") ?? "Position",
            Amount(line, "LineExtensionAmount"),
            null)).ToList();

        return Build(
            supplier: Text(Child(supplier, "PartyLegalEntity"), "RegistrationName") ?? Text(Child(supplier, "PartyName"), "Name"),
            iban: Text(Child(Child(root, "PaymentMeans"), "PayeeFinancialAccount"), "ID"),
            number: Text(root, "ID"),
            invoiceDate: Date(Text(root, "IssueDate")),
            serviceFrom: Date(Text(period, "StartDate")),
            serviceTo: Date(Text(period, "EndDate")),
            dueOn: Date(Text(root, "DueDate")),
            gross: Amount(totals, "TaxInclusiveAmount") ?? Amount(totals, "PayableAmount"),
            net: Amount(totals, "TaxExclusiveAmount"),
            vat: Amount(Child(root, "TaxTotal"), "TaxAmount"),
            currency: Text(root, "DocumentCurrencyCode"),
            isCredit,
            lines);
    }

    // ---- common ----------------------------------------------------------------------------------------------------

    private static ReceiptSuggestion? Build(
        string? supplier, string? iban, string? number, DateOnly? invoiceDate, DateOnly? serviceFrom, DateOnly? serviceTo, DateOnly? dueOn,
        decimal? gross, decimal? net, decimal? vat, string? currency, bool isCredit, List<SuggestedLine> lines)
    {
        // Without an amount it is no usable invoice for us (the MINIMUM profile has one).
        if (gross is null && net is null)
        {
            return null;
        }

        var warnings = new List<string>();
        if (isCredit)
        {
            warnings.Add("Das ist eine Gutschrift (Rechnungskorrektur): Bitte prüfen, wie sie zu erfassen ist.");
        }

        if (!string.IsNullOrWhiteSpace(currency) && !currency.Equals("EUR", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add($"Die Rechnung lautet auf {currency}, nicht auf Euro. Die Beträge bitte umrechnen.");
        }

        return new ReceiptSuggestion
        {
            SupplierName = Sure(supplier?.Trim()),
            Iban = Sure(Iban.IsValid(iban) ? Iban.Format(iban!) : null),
            InvoiceNumber = Sure(number?.Trim()),
            InvoiceDate = Sure(invoiceDate),
            ServiceFrom = Sure(serviceFrom),
            ServiceTo = Sure(serviceTo),
            DueOn = Sure(dueOn),
            Gross = Sure(gross ?? (net is { } n && vat is { } v ? n + v : null)),
            Net = Sure(net),
            Vat = Sure(vat),
            Lines = lines,
            Warnings = warnings,
        };
    }

    /// <summary>Structured data is exact: full confidence for a value that is present.</summary>
    private static Suggested<T> Sure<T>(T? value) => new(value, 1.0);

    private static XElement? Child(XElement? parent, string name) => parent?.Elements().FirstOrDefault(e => e.Name.LocalName == name);

    private static string? Text(XElement? parent, string name) => Child(parent, name)?.Value.Trim() is { Length: > 0 } text ? text : null;

    private static decimal? Amount(XElement? parent, string name) =>
        Text(parent, name) is { } text && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;

    /// <summary>CII: a container with <c>DateTimeString</c> (format 102 = yyyyMMdd).</summary>
    private static DateOnly? DateOf(XElement? container) => Date(Text(container, "DateTimeString"));

    private static DateOnly? Date(string? text) =>
        text is not null && DateOnly.TryParseExact(text, ["yyyyMMdd", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
}
