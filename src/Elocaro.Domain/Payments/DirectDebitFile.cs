using System.Globalization;
using System.Text;
using System.Xml;
using Elocaro.Domain.Common;

namespace Elocaro.Domain.Payments;

/// <param name="EndToEndId">Reference that comes back on the account statement; letters, digits and "-" only, up to 35 characters.</param>
/// <param name="MandateReference">Reference of the mandate the collection is based on.</param>
public sealed record DirectDebit(
    string EndToEndId,
    decimal Amount,
    string MandateReference,
    DateOnly MandateSignedOn,
    string DebtorName,
    string DebtorIban,
    string? DebtorBic,
    string Purpose,
    MandateSequence Sequence);

/// <param name="CollectionDate">Day the debits are to be collected (the bank wants the file at least one bank working day before).</param>
/// <param name="CreditorId">Creditor identifier (Gläubiger-ID) of the landlord.</param>
public sealed record DirectDebitBatch(
    string MessageId,
    DateTimeOffset CreatedAt,
    DateOnly CollectionDate,
    string CreditorName,
    string CreditorIban,
    string? CreditorBic,
    string CreditorId,
    IReadOnlyList<DirectDebit> Debits);

/// <summary>
/// SEPA core direct debit file for the online banking of the landlord: ISO 20022 <c>pain.008.001.08</c> in the form of the German banks (Deutsche
/// Kreditwirtschaft). One payment block per sequence type (RCUR for recurring mandates, OOFF for one-off ones), all with the same collection
/// date. Like <see cref="CreditTransferFile"/> the file only describes collections; the landlord submits it in the own banking.
/// </summary>
public static class DirectDebitFile
{
    public const int MaxDebits = 99_999;
    private const int MaxName = 70;
    private const int MaxPurpose = 140;
    private const decimal MaxAmount = 999_999_999.99m;
    private const string Namespace = "urn:iso:std:iso:20022:tech:xsd:pain.008.001.08";

    /// <exception cref="RuleViolationException">A debit, the mandate data or the creditor is not valid; the message names it (German).</exception>
    public static byte[] Build(DirectDebitBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        Validate(batch);
        var creditorName = CreditTransferFile.Sepa(batch.CreditorName, MaxName);
        var groups = batch.Debits.GroupBy(d => d.Sequence).OrderBy(g => g.Key).ToList();

        using var stream = new MemoryStream();
        using (var xml = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true, OmitXmlDeclaration = false }))
        {
            xml.WriteStartDocument();
            xml.WriteStartElement("Document", Namespace);
            xml.WriteStartElement("CstmrDrctDbtInitn");

            xml.WriteStartElement("GrpHdr");
            xml.WriteElementString("MsgId", batch.MessageId);
            xml.WriteElementString("CreDtTm", batch.CreatedAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture));
            xml.WriteElementString("NbOfTxs", batch.Debits.Count.ToString(CultureInfo.InvariantCulture));
            xml.WriteElementString("CtrlSum", CreditTransferFile.Money(batch.Debits.Sum(d => d.Amount)));
            xml.WriteStartElement("InitgPty");
            xml.WriteElementString("Nm", creditorName);
            xml.WriteEndElement(); // InitgPty
            xml.WriteEndElement(); // GrpHdr

            foreach (var group in groups)
            {
                var sequence = group.Key == MandateSequence.OneOff ? "OOFF" : "RCUR";
                xml.WriteStartElement("PmtInf");
                xml.WriteElementString("PmtInfId", $"{batch.MessageId}-{sequence}");
                xml.WriteElementString("PmtMtd", "DD");
                xml.WriteElementString("BtchBookg", "true");
                xml.WriteElementString("NbOfTxs", group.Count().ToString(CultureInfo.InvariantCulture));
                xml.WriteElementString("CtrlSum", CreditTransferFile.Money(group.Sum(d => d.Amount)));
                xml.WriteStartElement("PmtTpInf");
                xml.WriteStartElement("SvcLvl");
                xml.WriteElementString("Cd", "SEPA");
                xml.WriteEndElement(); // SvcLvl
                xml.WriteStartElement("LclInstrm");
                xml.WriteElementString("Cd", "CORE");
                xml.WriteEndElement(); // LclInstrm
                xml.WriteElementString("SeqTp", sequence);
                xml.WriteEndElement(); // PmtTpInf
                xml.WriteElementString("ReqdColltnDt", batch.CollectionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                xml.WriteStartElement("Cdtr");
                xml.WriteElementString("Nm", creditorName);
                xml.WriteEndElement(); // Cdtr
                xml.WriteStartElement("CdtrAcct");
                xml.WriteStartElement("Id");
                xml.WriteElementString("IBAN", Iban.Compact(batch.CreditorIban));
                xml.WriteEndElement(); // Id
                xml.WriteEndElement(); // CdtrAcct
                WriteAgent(xml, "CdtrAgt", batch.CreditorBic);
                xml.WriteElementString("ChrgBr", "SLEV");
                xml.WriteStartElement("CdtrSchmeId");
                xml.WriteStartElement("Id");
                xml.WriteStartElement("PrvtId");
                xml.WriteStartElement("Othr");
                xml.WriteElementString("Id", CreditorId.Compact(batch.CreditorId));
                xml.WriteStartElement("SchmeNm");
                xml.WriteElementString("Prtry", "SEPA");
                xml.WriteEndElement(); // SchmeNm
                xml.WriteEndElement(); // Othr
                xml.WriteEndElement(); // PrvtId
                xml.WriteEndElement(); // Id
                xml.WriteEndElement(); // CdtrSchmeId

                foreach (var debit in group)
                {
                    xml.WriteStartElement("DrctDbtTxInf");
                    xml.WriteStartElement("PmtId");
                    xml.WriteElementString("EndToEndId", debit.EndToEndId);
                    xml.WriteEndElement(); // PmtId
                    xml.WriteStartElement("InstdAmt");
                    xml.WriteAttributeString("Ccy", "EUR");
                    xml.WriteString(CreditTransferFile.Money(debit.Amount));
                    xml.WriteEndElement(); // InstdAmt
                    xml.WriteStartElement("DrctDbtTx");
                    xml.WriteStartElement("MndtRltdInf");
                    xml.WriteElementString("MndtId", debit.MandateReference);
                    xml.WriteElementString("DtOfSgntr", debit.MandateSignedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    xml.WriteEndElement(); // MndtRltdInf
                    xml.WriteEndElement(); // DrctDbtTx
                    WriteAgent(xml, "DbtrAgt", debit.DebtorBic);
                    xml.WriteStartElement("Dbtr");
                    xml.WriteElementString("Nm", CreditTransferFile.Sepa(debit.DebtorName, MaxName));
                    xml.WriteEndElement(); // Dbtr
                    xml.WriteStartElement("DbtrAcct");
                    xml.WriteStartElement("Id");
                    xml.WriteElementString("IBAN", Iban.Compact(debit.DebtorIban));
                    xml.WriteEndElement(); // Id
                    xml.WriteEndElement(); // DbtrAcct
                    if (CreditTransferFile.Sepa(debit.Purpose, MaxPurpose) is { Length: > 0 } purpose)
                    {
                        xml.WriteStartElement("RmtInf");
                        xml.WriteElementString("Ustrd", purpose);
                        xml.WriteEndElement(); // RmtInf
                    }

                    xml.WriteEndElement(); // DrctDbtTxInf
                }

                xml.WriteEndElement(); // PmtInf
            }

            xml.WriteEndElement(); // CstmrDrctDbtInitn
            xml.WriteEndElement(); // Document
            xml.WriteEndDocument();
        }

        return stream.ToArray();
    }

    private static void WriteAgent(XmlWriter xml, string element, string? bic)
    {
        xml.WriteStartElement(element);
        xml.WriteStartElement("FinInstnId");
        if (CreditTransferFile.Bic(bic) is { } value)
        {
            xml.WriteElementString("BICFI", value);
        }
        else
        {
            xml.WriteStartElement("Othr");
            xml.WriteElementString("Id", "NOTPROVIDED");
            xml.WriteEndElement(); // Othr
        }

        xml.WriteEndElement(); // FinInstnId
        xml.WriteEndElement(); // element
    }

    private static void Validate(DirectDebitBatch batch)
    {
        if (batch.Debits.Count == 0)
        {
            throw new RuleViolationException("Es ist kein Einzug ausgewählt.");
        }

        if (batch.Debits.Count > MaxDebits)
        {
            throw new RuleViolationException($"Eine Datei darf höchstens {MaxDebits} Einzüge enthalten.");
        }

        if (string.IsNullOrWhiteSpace(batch.MessageId) || batch.MessageId.Length > 30)
        {
            throw new RuleViolationException("Die Nachrichten-Id fehlt oder ist länger als 30 Zeichen.");
        }

        if (CreditTransferFile.Sepa(batch.CreditorName, MaxName).Length == 0)
        {
            throw new RuleViolationException("Im Vermieterprofil fehlt der Name für den Einzug.");
        }

        if (!Iban.IsValid(batch.CreditorIban))
        {
            throw new RuleViolationException("Im Vermieterprofil fehlt eine gültige IBAN für das Konto, auf das eingezogen wird.");
        }

        if (!CreditorId.IsValid(batch.CreditorId))
        {
            throw new RuleViolationException("Im Vermieterprofil fehlt die Gläubiger-Identifikationsnummer oder sie ist ungültig (Prüfziffer).");
        }

        foreach (var debit in batch.Debits)
        {
            var who = string.IsNullOrWhiteSpace(debit.DebtorName) ? "ein Zahlungspflichtiger" : debit.DebtorName;
            if (CreditTransferFile.Sepa(debit.DebtorName, MaxName).Length == 0)
            {
                throw new RuleViolationException("Bei einem Einzug fehlt der Name des Kontoinhabers.");
            }

            if (!Iban.IsValid(debit.DebtorIban))
            {
                throw new RuleViolationException($"Für {who} fehlt eine gültige IBAN.");
            }

            if (!DirectDebitMandate.IsValidReference(debit.MandateReference))
            {
                throw new RuleViolationException($"Die Mandatsreferenz von {who} ist ungültig.");
            }

            if (debit.MandateSignedOn > batch.CollectionDate)
            {
                throw new RuleViolationException($"Das Mandat von {who} ist erst nach dem Einzugstag unterschrieben.");
            }

            if (debit.Amount < 0.01m || debit.Amount > MaxAmount || decimal.Round(debit.Amount, 2) != debit.Amount)
            {
                throw new RuleViolationException($"Der Betrag für {who} ist ungültig (mindestens 0,01 €, höchstens zwei Nachkommastellen).");
            }

            if (string.IsNullOrWhiteSpace(debit.EndToEndId) || debit.EndToEndId.Length > 35 || !debit.EndToEndId.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            {
                throw new RuleViolationException("Die Referenz eines Einzugs darf nur Buchstaben, Ziffern und Bindestriche enthalten (höchstens 35 Zeichen).");
            }
        }

        if (batch.Debits.Select(d => d.EndToEndId).Distinct(StringComparer.Ordinal).Count() != batch.Debits.Count)
        {
            throw new RuleViolationException("Die Referenzen der Einzüge müssen verschieden sein.");
        }
    }
}
