using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using Elocaro.Domain.Common;
using Elocaro.Domain.Payments;

namespace Elocaro.Domain.Tests.Payments;

public class DirectDebitFileTests
{
    private const string CreditorIban = "DE89 3704 0044 0532 0130 00";
    private const string DebtorIban = "DE02 1203 0000 0000 2020 51";
    private const string ValidCreditorId = "DE98ZZZ09999999999";
    private static readonly XNamespace Ns = "urn:iso:std:iso:20022:tech:xsd:pain.008.001.08";

    private static DirectDebitBatch Batch(params DirectDebit[] debits) => new(
        "ELOCARO-20261116-1", new DateTimeOffset(2026, 11, 16, 9, 30, 0, TimeSpan.Zero), new DateOnly(2026, 12, 3), "Hausverwaltung Müller", CreditorIban, null, ValidCreditorId, debits);

    private static DirectDebit Debit(string id = "ELO-1", decimal amount = 650m, MandateSequence sequence = MandateSequence.Recurring, string name = "Anna Schütz", string reference = "MND-20260105-1F3A9C2B") =>
        new(id, amount, reference, new DateOnly(2026, 1, 5), name, DebtorIban, null, "Miete 12/2026 Wohnung 1", sequence);

    private static XDocument Parse(byte[] file) => XDocument.Parse(Encoding.UTF8.GetString(file));

    [Theory]
    [InlineData("DE98ZZZ09999999999", true)]
    [InlineData("de98 zzz 0999 9999 999", true)]
    [InlineData("DE97ZZZ09999999999", false)]
    [InlineData("DE98ZZZ0999999999", false)]
    [InlineData("", false)]
    [InlineData("12345678901234567", false)]
    public void The_creditor_id_is_checked_by_length_and_check_digits(string value, bool valid) => Assert.Equal(valid, CreditorId.IsValid(value));

    [Fact]
    public void Recurring_and_one_off_debits_get_one_payment_block_each_with_creditor_and_mandate_data()
    {
        var file = DirectDebitFile.Build(Batch(Debit("ELO-1", 650m), Debit("ELO-2", 80.50m, MandateSequence.OneOff, "Max Beispiel", "MND-X-1"), Debit("ELO-3", 700m, reference: "MND-OTHER")));

        var xml = Parse(file);
        var header = xml.Root!.Element(Ns + "CstmrDrctDbtInitn")!.Element(Ns + "GrpHdr")!;
        Assert.Equal("3", header.Element(Ns + "NbOfTxs")!.Value);
        Assert.Equal("1430.50", header.Element(Ns + "CtrlSum")!.Value);
        var blocks = xml.Descendants(Ns + "PmtInf").ToList();
        Assert.Equal(["RCUR", "OOFF"], blocks.Select(b => b.Descendants(Ns + "SeqTp").Single().Value));
        Assert.Equal(["2", "1"], blocks.Select(b => b.Element(Ns + "NbOfTxs")!.Value));
        Assert.All(blocks, b =>
        {
            Assert.Equal("DD", b.Element(Ns + "PmtMtd")!.Value);
            Assert.Equal("CORE", b.Descendants(Ns + "LclInstrm").Single().Element(Ns + "Cd")!.Value);
            Assert.Equal("2026-12-03", b.Element(Ns + "ReqdColltnDt")!.Value);
            Assert.Equal("DE89370400440532013000", b.Element(Ns + "CdtrAcct")!.Descendants(Ns + "IBAN").Single().Value);
            Assert.Equal(ValidCreditorId, b.Element(Ns + "CdtrSchmeId")!.Descendants(Ns + "Id").Last().Value);
        });
        var first = blocks[0].Elements(Ns + "DrctDbtTxInf").First();
        Assert.Equal("MND-20260105-1F3A9C2B", first.Descendants(Ns + "MndtId").Single().Value);
        Assert.Equal("2026-01-05", first.Descendants(Ns + "DtOfSgntr").Single().Value);
        Assert.Equal("Anna Schuetz", first.Element(Ns + "Dbtr")!.Element(Ns + "Nm")!.Value);
        Assert.Equal("DE02120300000000202051", first.Element(Ns + "DbtrAcct")!.Descendants(Ns + "IBAN").Single().Value);
        Assert.Equal("650.00", first.Element(Ns + "InstdAmt")!.Value);
    }

    [Theory]
    [InlineData("creditor-id")]
    [InlineData("iban")]
    [InlineData("amount")]
    [InlineData("reference")]
    [InlineData("signed-after-collection")]
    public void An_invalid_debit_or_creditor_is_refused_with_a_reason(string defect)
    {
        var batch = defect switch
        {
            "creditor-id" => Batch(Debit()) with { CreditorId = "DE00ZZZ00000000000" },
            "iban" => Batch(Debit() with { DebtorIban = "DE00 1234" }),
            "amount" => Batch(Debit(amount: 0m)),
            "reference" => Batch(Debit(reference: "Mandat mit Leerzeichen")),
            _ => Batch(Debit() with { MandateSignedOn = new DateOnly(2027, 1, 1) }),
        };

        Assert.Throws<RuleViolationException>(() => DirectDebitFile.Build(batch));
    }

    [Fact]
    public void An_empty_file_and_duplicate_references_are_refused()
    {
        Assert.Throws<RuleViolationException>(() => DirectDebitFile.Build(Batch()));
        Assert.Throws<RuleViolationException>(() => DirectDebitFile.Build(Batch(Debit("ELO-1"), Debit("ELO-1"))));
    }

    [Fact]
    public void A_mandate_is_active_from_its_signature_until_it_is_revoked_and_its_reference_is_checked()
    {
        var mandate = new DirectDebitMandate(Guid.NewGuid(), DirectDebitMandate.NewReference(Guid.Parse("1f3a9c2b-0000-0000-0000-000000000000"), new DateOnly(2026, 1, 5)), "Anna Schütz", DebtorIban, null, new DateOnly(2026, 1, 5), MandateSequence.Recurring);

        Assert.Equal("MND-20260105-1F3A9C2B", mandate.Reference);
        Assert.False(mandate.IsActiveOn(new DateOnly(2026, 1, 4)));
        Assert.True(mandate.IsActiveOn(new DateOnly(2026, 1, 5)));
        mandate.Revoke(new DateOnly(2026, 6, 30));
        Assert.True(mandate.IsActiveOn(new DateOnly(2026, 6, 29)));
        Assert.False(mandate.IsActiveOn(new DateOnly(2026, 6, 30)));
        Assert.Throws<RuleViolationException>(() => mandate.Revoke(new DateOnly(2025, 12, 31)));
        Assert.Throws<RuleViolationException>(() => new DirectDebitMandate(Guid.NewGuid(), "mit Leerzeichen", "Anna", DebtorIban, null, new DateOnly(2026, 1, 5), MandateSequence.Recurring));
        Assert.Throws<RuleViolationException>(() => new DirectDebitMandate(Guid.NewGuid(), "MND-1", "Anna", "DE00", null, new DateOnly(2026, 1, 5), MandateSequence.Recurring));
    }

    /// <summary>Like the transfer file: validated against the DK schema <c>pain.008.001.08_GBIC_5.xsd</c> when <c>ELOCARO_PAIN008_XSD</c> points to it (not in the repository, see docs/entwicklung.md).</summary>
    [Fact]
    public void The_file_is_valid_against_the_schema_when_it_is_available()
    {
        var path = Environment.GetEnvironmentVariable("ELOCARO_PAIN008_XSD");
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(path) && File.Exists(path), "ELOCARO_PAIN008_XSD zeigt nicht auf eine XSD (pain.008.001.08), Test übersprungen.");

        var schemas = new XmlSchemaSet();
        using (var reader = XmlReader.Create(path!))
        {
            schemas.Add("urn:iso:std:iso:20022:tech:xsd:pain.008.001.08", reader);
        }

        foreach (var file in new[]
        {
            DirectDebitFile.Build(Batch(Debit())),
            DirectDebitFile.Build(Batch(Debit("ELO-1"), Debit("ELO-2", 0.01m, MandateSequence.OneOff, "Müller & Söhne", "MND-X-1"), Debit("ELO-3", 999_999_999.99m, reference: "M3")) with { CreditorBic = "COBADEFFXXX" }),
        })
        {
            var errors = new List<string>();
            Parse(file).Validate(schemas, (_, e) => errors.Add(e.Message));
            Assert.True(errors.Count == 0, string.Join("\n", errors));
        }
    }
}
