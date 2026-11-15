using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using Elocaro.Domain.Common;
using Elocaro.Domain.Payments;

namespace Elocaro.Domain.Tests.Payments;

public class CreditTransferFileTests
{
    private const string DebtorIban = "DE89 3704 0044 0532 0130 00";
    private const string CreditorIban = "DE02 1203 0000 0000 2020 51";
    private static readonly XNamespace Ns = "urn:iso:std:iso:20022:tech:xsd:pain.001.001.09";

    private static CreditTransferBatch Batch(params CreditTransfer[] transfers) => new(
        "ELOCARO-20261116-1", new DateTimeOffset(2026, 11, 16, 9, 30, 0, TimeSpan.Zero), new DateOnly(2026, 11, 18), "Hausverwaltung Müller", DebtorIban, null, transfers);

    private static CreditTransfer Transfer(string id = "ELO-1", string name = "Schmidt Heizung GmbH", string iban = CreditorIban, decimal amount = 120.50m, string purpose = "Rechnung 2026-17") =>
        new(id, name, iban, amount, purpose);

    private static XDocument Parse(byte[] file) => XDocument.Parse(Encoding.UTF8.GetString(file));

    [Fact]
    public void The_file_has_the_group_header_one_payment_block_and_a_booking_per_transfer()
    {
        var file = CreditTransferFile.Build(Batch(Transfer("ELO-1", amount: 120.50m), Transfer("ELO-2", "Anna Schmidt", amount: 79.50m, purpose: "Guthaben Nebenkosten 2025")));

        var xml = Parse(file);
        var header = xml.Root!.Element(Ns + "CstmrCdtTrfInitn")!.Element(Ns + "GrpHdr")!;
        Assert.Equal("2", header.Element(Ns + "NbOfTxs")!.Value);
        Assert.Equal("200.00", header.Element(Ns + "CtrlSum")!.Value);
        Assert.Equal("2026-11-16T09:30:00", header.Element(Ns + "CreDtTm")!.Value);
        var payment = xml.Descendants(Ns + "PmtInf").Single();
        Assert.Equal("TRF", payment.Element(Ns + "PmtMtd")!.Value);
        Assert.Equal("2026-11-18", payment.Element(Ns + "ReqdExctnDt")!.Element(Ns + "Dt")!.Value);
        Assert.Equal("DE89370400440532013000", payment.Element(Ns + "DbtrAcct")!.Descendants(Ns + "IBAN").Single().Value);
        var bookings = payment.Elements(Ns + "CdtTrfTxInf").ToList();
        Assert.Equal(["ELO-1", "ELO-2"], bookings.Select(b => b.Descendants(Ns + "EndToEndId").Single().Value));
        Assert.Equal(["120.50", "79.50"], bookings.Select(b => b.Descendants(Ns + "InstdAmt").Single().Value));
        Assert.All(bookings, b => Assert.Equal("EUR", b.Descendants(Ns + "InstdAmt").Single().Attribute("Ccy")!.Value));
        Assert.Equal("DE02120300000000202051", bookings[0].Element(Ns + "CdtrAcct")!.Descendants(Ns + "IBAN").Single().Value);
        Assert.Equal("Guthaben Nebenkosten 2025", bookings[1].Descendants(Ns + "Ustrd").Single().Value);
    }

    [Fact]
    public void Names_and_purposes_come_out_in_the_sepa_character_set()
    {
        var file = CreditTransferFile.Build(Batch(Transfer(name: "Müller & Söhne GmbH", purpose: "Café Ärger: Straße #5 – 100 %")));

        var xml = Parse(file);
        Assert.Equal("Mueller + Soehne GmbH", xml.Descendants(Ns + "Cdtr").Single().Element(Ns + "Nm")!.Value);
        Assert.Equal("Cafe Aerger: Strasse 5 100", xml.Descendants(Ns + "Ustrd").Single().Value);
        Assert.Equal("Hausverwaltung Mueller", xml.Descendants(Ns + "Dbtr").Single().Element(Ns + "Nm")!.Value);
        Assert.Equal(70, CreditTransferFile.Sepa(new string('a', 100), 70).Length);
    }

    [Fact]
    public void Without_a_bic_the_debtor_bank_is_marked_as_not_provided_and_with_one_it_is_named()
    {
        var withoutBic = Parse(CreditTransferFile.Build(Batch(Transfer())));
        Assert.Equal("NOTPROVIDED", withoutBic.Descendants(Ns + "DbtrAgt").Single().Descendants(Ns + "Id").Single().Value);

        var withBic = Parse(CreditTransferFile.Build(Batch(Transfer()) with { DebtorBic = "cobadeff 370" }));
        Assert.Equal("COBADEFF370", withBic.Descendants(Ns + "DbtrAgt").Single().Descendants(Ns + "BICFI").Single().Value);
    }

    [Theory]
    [InlineData("iban")]
    [InlineData("amount-zero")]
    [InlineData("amount-cents")]
    [InlineData("name")]
    [InlineData("reference")]
    public void An_invalid_transfer_is_refused_with_a_reason(string defect)
    {
        var transfer = defect switch
        {
            "iban" => Transfer(iban: "DE00 1234"),
            "amount-zero" => Transfer(amount: 0m),
            "amount-cents" => Transfer(amount: 10.005m),
            "name" => Transfer(name: "   "),
            _ => Transfer(id: "ELO 1/2"),
        };

        Assert.Throws<RuleViolationException>(() => CreditTransferFile.Build(Batch(transfer)));
    }

    [Fact]
    public void An_empty_file_duplicate_references_and_a_missing_debtor_account_are_refused()
    {
        Assert.Throws<RuleViolationException>(() => CreditTransferFile.Build(Batch()));
        Assert.Throws<RuleViolationException>(() => CreditTransferFile.Build(Batch(Transfer("ELO-1"), Transfer("ELO-1"))));
        Assert.Throws<RuleViolationException>(() => CreditTransferFile.Build(Batch(Transfer()) with { DebtorIban = null! }));
        Assert.Throws<RuleViolationException>(() => CreditTransferFile.Build(Batch(Transfer()) with { MessageId = new string('x', 36) }));
    }

    /// <summary>
    /// Validates against the schema of the German banks (DK, <c>pain.001.001.09_GBIC_5.xsd</c>, from ebics.de, ISO 20022 original from iso20022.org). It is not part of the
    /// repository (terms of use of the publishers); set <c>ELOCARO_PAIN001_XSD</c> to the file to run this test, see docs/entwicklung.md.
    /// </summary>
    [Fact]
    public void The_file_is_valid_against_the_schema_when_it_is_available()
    {
        var path = Environment.GetEnvironmentVariable("ELOCARO_PAIN001_XSD");
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(path) && File.Exists(path), "ELOCARO_PAIN001_XSD zeigt nicht auf eine XSD (pain.001.001.09), Test übersprungen.");

        var schemas = new XmlSchemaSet();
        using (var reader = XmlReader.Create(path!))
        {
            schemas.Add("urn:iso:std:iso:20022:tech:xsd:pain.001.001.09", reader);
        }

        foreach (var file in new[]
        {
            CreditTransferFile.Build(Batch(Transfer())),
            CreditTransferFile.Build(Batch(Transfer("ELO-1"), Transfer("ELO-2", "Müller & Söhne", amount: 0.01m, purpose: ""), Transfer("ELO-3", amount: 999_999_999.99m)) with { DebtorBic = "COBADEFFXXX" }),
        })
        {
            var errors = new List<string>();
            Parse(file).Validate(schemas, (_, e) => errors.Add(e.Message));
            Assert.True(errors.Count == 0, string.Join("\n", errors));
        }
    }
}
