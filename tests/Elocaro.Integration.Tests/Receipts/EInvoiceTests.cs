using System.Text;
using Elocaro.Application.Files;
using Elocaro.Application.Receipts;
using Elocaro.Infrastructure.Receipts.Extraction;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Receipts;

/// <summary>Electronic invoices are read from their structured data, without an AI provider; anything else falls back to the normal reading.</summary>
public class EInvoiceTests(PostgresFixture postgres) : IAsyncLifetime
{
    internal const string Cii =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
        "<rsm:CrossIndustryInvoice xmlns:rsm=\"urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100\" xmlns:ram=\"urn:un:unece:uncefact:data:standard:ReusableAggregateBusinessInformationEntity:100\" xmlns:udt=\"urn:un:unece:uncefact:data:standard:UnqualifiedDataType:100\">" +
        "<rsm:ExchangedDocument><ram:ID>RE-2026-0815</ram:ID><ram:TypeCode>380</ram:TypeCode><ram:IssueDateTime><udt:DateTimeString format=\"102\">20260310</udt:DateTimeString></ram:IssueDateTime></rsm:ExchangedDocument>" +
        "<rsm:SupplyChainTradeTransaction>" +
        "<ram:IncludedSupplyChainTradeLineItem><ram:SpecifiedTradeProduct><ram:Name>Wartung Heizung</ram:Name></ram:SpecifiedTradeProduct><ram:SpecifiedLineTradeSettlement><ram:SpecifiedTradeSettlementLineMonetarySummation><ram:LineTotalAmount>200.00</ram:LineTotalAmount></ram:SpecifiedTradeSettlementLineMonetarySummation></ram:SpecifiedLineTradeSettlement></ram:IncludedSupplyChainTradeLineItem>" +
        "<ram:IncludedSupplyChainTradeLineItem><ram:SpecifiedTradeProduct><ram:Name>Anfahrt</ram:Name></ram:SpecifiedTradeProduct><ram:SpecifiedLineTradeSettlement><ram:SpecifiedTradeSettlementLineMonetarySummation><ram:LineTotalAmount>50.00</ram:LineTotalAmount></ram:SpecifiedTradeSettlementLineMonetarySummation></ram:SpecifiedLineTradeSettlement></ram:IncludedSupplyChainTradeLineItem>" +
        "<ram:ApplicableHeaderTradeAgreement><ram:SellerTradeParty><ram:Name>Mustermann Haustechnik GmbH</ram:Name></ram:SellerTradeParty></ram:ApplicableHeaderTradeAgreement>" +
        "<ram:ApplicableHeaderTradeSettlement><ram:InvoiceCurrencyCode>EUR</ram:InvoiceCurrencyCode>" +
        "<ram:SpecifiedTradeSettlementPaymentMeans><ram:TypeCode>58</ram:TypeCode><ram:PayeeSpecifiedCreditorFinancialAccount><ram:IBANID>DE89370400440532013000</ram:IBANID></ram:PayeeSpecifiedCreditorFinancialAccount></ram:SpecifiedTradeSettlementPaymentMeans>" +
        "<ram:SpecifiedTradePaymentTerms><ram:DueDateDateTime><udt:DateTimeString format=\"102\">20260324</udt:DateTimeString></ram:DueDateDateTime></ram:SpecifiedTradePaymentTerms>" +
        "<ram:BillingSpecifiedPeriod><ram:StartDateTime><udt:DateTimeString format=\"102\">20260301</udt:DateTimeString></ram:StartDateTime><ram:EndDateTime><udt:DateTimeString format=\"102\">20260302</udt:DateTimeString></ram:EndDateTime></ram:BillingSpecifiedPeriod>" +
        "<ram:SpecifiedTradeSettlementHeaderMonetarySummation><ram:LineTotalAmount>250.00</ram:LineTotalAmount><ram:TaxBasisTotalAmount>250.00</ram:TaxBasisTotalAmount><ram:TaxTotalAmount currencyID=\"EUR\">47.50</ram:TaxTotalAmount><ram:GrandTotalAmount>297.50</ram:GrandTotalAmount><ram:DuePayableAmount>297.50</ram:DuePayableAmount></ram:SpecifiedTradeSettlementHeaderMonetarySummation>" +
        "</ram:ApplicableHeaderTradeSettlement></rsm:SupplyChainTradeTransaction></rsm:CrossIndustryInvoice>";

    /// <summary>The MINIMUM profile: only the header data, no lines, no bank details.</summary>
    private const string CiiMinimum =
        "<rsm:CrossIndustryInvoice xmlns:rsm=\"urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100\" xmlns:ram=\"urn:un:unece:uncefact:data:standard:ReusableAggregateBusinessInformationEntity:100\" xmlns:udt=\"urn:un:unece:uncefact:data:standard:UnqualifiedDataType:100\">" +
        "<rsm:ExchangedDocument><ram:ID>M-1</ram:ID><ram:IssueDateTime><udt:DateTimeString format=\"102\">20260115</udt:DateTimeString></ram:IssueDateTime></rsm:ExchangedDocument>" +
        "<rsm:SupplyChainTradeTransaction><ram:ApplicableHeaderTradeAgreement><ram:SellerTradeParty><ram:Name>Strom AG</ram:Name></ram:SellerTradeParty></ram:ApplicableHeaderTradeAgreement>" +
        "<ram:ApplicableHeaderTradeSettlement><ram:InvoiceCurrencyCode>EUR</ram:InvoiceCurrencyCode><ram:SpecifiedTradeSettlementHeaderMonetarySummation><ram:TaxBasisTotalAmount>100.00</ram:TaxBasisTotalAmount><ram:TaxTotalAmount currencyID=\"EUR\">19.00</ram:TaxTotalAmount><ram:GrandTotalAmount>119.00</ram:GrandTotalAmount><ram:DuePayableAmount>119.00</ram:DuePayableAmount></ram:SpecifiedTradeSettlementHeaderMonetarySummation></ram:ApplicableHeaderTradeSettlement>" +
        "</rsm:SupplyChainTradeTransaction></rsm:CrossIndustryInvoice>";

    private const string Ubl =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
        "<Invoice xmlns=\"urn:oasis:names:specification:ubl:schema:xsd:Invoice-2\" xmlns:cac=\"urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2\" xmlns:cbc=\"urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2\">" +
        "<cbc:ID>2026-77</cbc:ID><cbc:IssueDate>2026-02-10</cbc:IssueDate><cbc:DueDate>2026-02-24</cbc:DueDate><cbc:InvoiceTypeCode>380</cbc:InvoiceTypeCode><cbc:DocumentCurrencyCode>EUR</cbc:DocumentCurrencyCode>" +
        "<cac:InvoicePeriod><cbc:StartDate>2026-01-01</cbc:StartDate><cbc:EndDate>2026-01-31</cbc:EndDate></cac:InvoicePeriod>" +
        "<cac:AccountingSupplierParty><cac:Party><cac:PartyName><cbc:Name>Handelsname</cbc:Name></cac:PartyName><cac:PartyLegalEntity><cbc:RegistrationName>Müller Gebäudereinigung GmbH</cbc:RegistrationName></cac:PartyLegalEntity></cac:Party></cac:AccountingSupplierParty>" +
        "<cac:PaymentMeans><cbc:PaymentMeansCode>58</cbc:PaymentMeansCode><cac:PayeeFinancialAccount><cbc:ID>DE02120300000000202051</cbc:ID></cac:PayeeFinancialAccount></cac:PaymentMeans>" +
        "<cac:TaxTotal><cbc:TaxAmount currencyID=\"EUR\">38.00</cbc:TaxAmount></cac:TaxTotal>" +
        "<cac:LegalMonetaryTotal><cbc:LineExtensionAmount currencyID=\"EUR\">200.00</cbc:LineExtensionAmount><cbc:TaxExclusiveAmount currencyID=\"EUR\">200.00</cbc:TaxExclusiveAmount><cbc:TaxInclusiveAmount currencyID=\"EUR\">238.00</cbc:TaxInclusiveAmount><cbc:PayableAmount currencyID=\"EUR\">238.00</cbc:PayableAmount></cac:LegalMonetaryTotal>" +
        "<cac:InvoiceLine><cbc:ID>1</cbc:ID><cbc:LineExtensionAmount currencyID=\"EUR\">200.00</cbc:LineExtensionAmount><cac:Item><cbc:Name>Treppenhausreinigung Januar</cbc:Name></cac:Item></cac:InvoiceLine>" +
        "</Invoice>";

    private InfrastructureEnvironment environment = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => environment = await InfrastructureEnvironment.CreateAsync(postgres);

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public void A_cii_invoice_gives_all_header_fields_and_the_lines()
    {
        var s = EInvoiceReader.Parse(Encoding.UTF8.GetBytes(Cii))!;

        Assert.Equal("Mustermann Haustechnik GmbH", s.SupplierName.Value);
        Assert.Equal("RE-2026-0815", s.InvoiceNumber.Value);
        Assert.Equal(new DateOnly(2026, 3, 10), s.InvoiceDate.Value);
        Assert.Equal(new DateOnly(2026, 3, 24), s.DueOn.Value);
        Assert.Equal(new DateOnly(2026, 3, 1), s.ServiceFrom.Value);
        Assert.Equal(new DateOnly(2026, 3, 2), s.ServiceTo.Value);
        Assert.Equal("DE89 3704 0044 0532 0130 00", s.Iban.Value);
        Assert.Equal((297.50m, 250m, 47.50m), (s.Gross.Value, s.Net.Value, s.Vat.Value));
        Assert.Equal(["Wartung Heizung", "Anfahrt"], s.Lines.Select(l => l.Description));
        Assert.Equal([200m, 50m], s.Lines.Select(l => l.Amount));
        Assert.Empty(s.Warnings);
        Assert.Empty(s.UncertainFields()); // structured data is exact
    }

    [Fact]
    public void The_minimum_profile_without_lines_and_bank_details_still_gives_the_amounts()
    {
        var s = EInvoiceReader.Parse(Encoding.UTF8.GetBytes(CiiMinimum))!;

        Assert.Equal("Strom AG", s.SupplierName.Value);
        Assert.Equal((119m, 100m, 19m), (s.Gross.Value, s.Net.Value, s.Vat.Value));
        Assert.False(s.Iban.HasValue);
        Assert.Empty(s.Lines);
    }

    [Fact]
    public void An_xrechnung_in_ubl_is_read_with_umlauts_the_legal_name_and_the_period()
    {
        var s = EInvoiceReader.Parse(Encoding.UTF8.GetBytes(Ubl))!;

        Assert.Equal("Müller Gebäudereinigung GmbH", s.SupplierName.Value); // the legal name, not the trade name
        Assert.Equal("2026-77", s.InvoiceNumber.Value);
        Assert.Equal((238m, 200m, 38m), (s.Gross.Value, s.Net.Value, s.Vat.Value));
        Assert.Equal(new DateOnly(2026, 1, 31), s.ServiceTo.Value);
        Assert.Equal("DE02 1203 0000 0000 2020 51", s.Iban.Value);
        Assert.Equal("Treppenhausreinigung Januar", Assert.Single(s.Lines).Description);
    }

    [Fact]
    public void A_credit_note_and_a_foreign_currency_are_flagged()
    {
        var credit = EInvoiceReader.Parse(Encoding.UTF8.GetBytes(Ubl.Replace("<cbc:InvoiceTypeCode>380", "<cbc:InvoiceTypeCode>381", StringComparison.Ordinal)))!;
        var dollars = EInvoiceReader.Parse(Encoding.UTF8.GetBytes(Ubl.Replace("<cbc:DocumentCurrencyCode>EUR", "<cbc:DocumentCurrencyCode>USD", StringComparison.Ordinal)))!;

        Assert.Contains(credit.Warnings, w => w.Contains("Gutschrift", StringComparison.Ordinal));
        Assert.Contains(dollars.Warnings, w => w.Contains("USD", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("<html><body>keine Rechnung</body></html>")]
    [InlineData("<Invoice xmlns=\"urn:oasis:names:specification:ubl:schema:xsd:Invoice-2\"></Invoice>")] // no amounts
    public void Other_xml_or_an_invoice_without_amounts_is_no_e_invoice(string xml)
    {
        Assert.Null(EInvoiceReader.Parse(Encoding.UTF8.GetBytes(xml)));
    }

    [Fact]
    public void Broken_and_hostile_input_falls_back_without_an_exception()
    {
        var entity = "<?xml version=\"1.0\"?><!DOCTYPE x [<!ENTITY a SYSTEM \"file:///etc/passwd\">]><Invoice xmlns=\"urn:oasis:names:specification:ubl:schema:xsd:Invoice-2\">&a;</Invoice>";

        Assert.Null(EInvoiceReader.TryRead(new ExtractionDocument("x.xml", "application/xml", Encoding.UTF8.GetBytes(entity))));
        Assert.Null(EInvoiceReader.TryRead(new ExtractionDocument("x.xml", "application/xml", Encoding.UTF8.GetBytes("<nicht zu Ende"))));
        Assert.Null(EInvoiceReader.TryRead(new ExtractionDocument("x.pdf", "application/pdf", "%PDF-1.7 kaputt"u8.ToArray())));
        Assert.Null(EInvoiceReader.TryRead(new ExtractionDocument("x.png", "image/png", new byte[] { 1, 2, 3 })));
    }

    [Fact]
    public void A_pdf_with_an_attached_facturx_xml_is_read_and_one_without_is_not()
    {
        var zugferd = new ExtractionDocument("rechnung.pdf", "application/pdf", PdfWithAttachment("factur-x.xml", Cii));
        var plain = new ExtractionDocument("rechnung.pdf", "application/pdf", PdfWithAttachment("anhang.txt", "hallo"));

        Assert.Equal("RE-2026-0815", EInvoiceReader.TryRead(zugferd)!.InvoiceNumber.Value);
        Assert.Null(EInvoiceReader.TryRead(plain));
    }

    [Fact]
    public async Task Without_any_provider_a_zugferd_pdf_fills_the_suggestion_with_the_source_e_invoice()
    {
        var id = await StoreAsync(PdfWithAttachment("zugferd-invoice.xml", Cii));
        await using var scope = environment.Services.CreateAsyncScope();

        var outcome = await scope.ServiceProvider.GetRequiredService<IReceiptExtraction>().SuggestAsync([id], Ct);

        Assert.Null(outcome.Error);
        Assert.Equal("E-Rechnung", outcome.Source);
        Assert.Equal("RE-2026-0815", outcome.Suggestion!.InvoiceNumber.Value);
        Assert.Equal(297.50m, outcome.Suggestion.Gross.Value);
        Assert.Equal(0, outcome.Usage.InputTokens + outcome.Usage.OutputTokens);
    }

    [Fact]
    public async Task A_normal_pdf_without_a_provider_says_that_no_e_invoice_was_found()
    {
        var id = await StoreAsync(PdfWithAttachment("anhang.txt", "hallo"));
        await using var scope = environment.Services.CreateAsyncScope();

        var outcome = await scope.ServiceProvider.GetRequiredService<IReceiptExtraction>().SuggestAsync([id], Ct);

        Assert.Null(outcome.Suggestion);
        Assert.Contains("keine E-Rechnung", outcome.Error, StringComparison.Ordinal);
    }

    private async Task<Guid> StoreAsync(byte[] pdf)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        await using var content = new MemoryStream(pdf);
        return (await scope.ServiceProvider.GetRequiredService<IFileStore>().SaveAsync(content, "rechnung.pdf", Ct)).Id;
    }

    /// <summary>A minimal valid PDF with one empty page and a file attached as in ZUGFeRD (ASCII only, so that character offsets equal byte offsets).</summary>
    internal static byte[] PdfWithAttachment(string name, string content)
    {
        var pdf = new StringBuilder("%PDF-1.7\n");
        var offsets = new List<int>();

        void Add(string body)
        {
            offsets.Add(pdf.Length);
            pdf.Append(offsets.Count).Append(" 0 obj\n").Append(body).Append("\nendobj\n");
        }

        Add($"<< /Type /Catalog /Pages 2 0 R /Names << /EmbeddedFiles << /Names [({name}) 4 0 R] >> >> /AF [4 0 R] >>");
        Add("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>");
        Add($"<< /Type /Filespec /F ({name}) /UF ({name}) /AFRelationship /Alternative /EF << /F 5 0 R /UF 5 0 R >> >>");
        Add($"<< /Type /EmbeddedFile /Subtype /text#2Fxml /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream");

        var xref = pdf.Length;
        pdf.Append("xref\n0 ").Append(offsets.Count + 1).Append("\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            pdf.Append(offset.ToString("D10", System.Globalization.CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        pdf.Append("trailer\n<< /Size ").Append(offsets.Count + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }
}
