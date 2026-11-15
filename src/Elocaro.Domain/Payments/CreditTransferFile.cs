using System.Globalization;
using System.Text;
using System.Xml;
using Elocaro.Domain.Common;

namespace Elocaro.Domain.Payments;

/// <param name="EndToEndId">Reference that comes back on the account statement; letters, digits and "-" only, up to 35 characters.</param>
/// <param name="Purpose">Free text for the recipient (invoice number, "Guthaben Nebenkosten 2025").</param>
public sealed record CreditTransfer(string EndToEndId, string CreditorName, string CreditorIban, decimal Amount, string Purpose);

/// <param name="MessageId">Unique per file, up to 35 characters.</param>
/// <param name="ExecutionDate">Day the bank is to execute the transfers.</param>
public sealed record CreditTransferBatch(
    string MessageId,
    DateTimeOffset CreatedAt,
    DateOnly ExecutionDate,
    string DebtorName,
    string DebtorIban,
    string? DebtorBic,
    IReadOnlyList<CreditTransfer> Transfers);

/// <summary>
/// SEPA credit transfer file for the online banking of the landlord: ISO 20022 <c>pain.001.001.09</c> in the form the German banks (Deutsche
/// Kreditwirtschaft) accept, one payment block with all transfers. The file only describes payments; the landlord submits it in the own banking
/// and nothing is booked here. Text is converted to the SEPA character set (umlauts become ae/oe/ue, other letters lose their accents).
/// </summary>
public static class CreditTransferFile
{
    public const int MaxTransfers = 99_999;
    private const int MaxName = 70;
    private const int MaxPurpose = 140;
    private const decimal MaxAmount = 999_999_999.99m;
    private const string Namespace = "urn:iso:std:iso:20022:tech:xsd:pain.001.001.09";
    private const string AllowedPunctuation = "/-?:().,+ ";

    /// <exception cref="RuleViolationException">A transfer or the debtor is not valid; the message names it (German).</exception>
    public static byte[] Build(CreditTransferBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        Validate(batch);
        var total = batch.Transfers.Sum(t => t.Amount);
        var debtorName = Sepa(batch.DebtorName, MaxName);
        var count = batch.Transfers.Count.ToString(CultureInfo.InvariantCulture);
        var sum = Money(total);

        using var stream = new MemoryStream();
        using (var xml = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true, OmitXmlDeclaration = false }))
        {
            xml.WriteStartDocument();
            xml.WriteStartElement("Document", Namespace);
            xml.WriteStartElement("CstmrCdtTrfInitn");

            xml.WriteStartElement("GrpHdr");
            xml.WriteElementString("MsgId", batch.MessageId);
            xml.WriteElementString("CreDtTm", batch.CreatedAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture));
            xml.WriteElementString("NbOfTxs", count);
            xml.WriteElementString("CtrlSum", sum);
            xml.WriteStartElement("InitgPty");
            xml.WriteElementString("Nm", debtorName);
            xml.WriteEndElement(); // InitgPty
            xml.WriteEndElement(); // GrpHdr

            xml.WriteStartElement("PmtInf");
            xml.WriteElementString("PmtInfId", batch.MessageId);
            xml.WriteElementString("PmtMtd", "TRF");
            xml.WriteElementString("BtchBookg", "true");
            xml.WriteElementString("NbOfTxs", count);
            xml.WriteElementString("CtrlSum", sum);
            xml.WriteStartElement("PmtTpInf");
            xml.WriteStartElement("SvcLvl");
            xml.WriteElementString("Cd", "SEPA");
            xml.WriteEndElement(); // SvcLvl
            xml.WriteEndElement(); // PmtTpInf
            xml.WriteStartElement("ReqdExctnDt");
            xml.WriteElementString("Dt", batch.ExecutionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            xml.WriteEndElement(); // ReqdExctnDt
            xml.WriteStartElement("Dbtr");
            xml.WriteElementString("Nm", debtorName);
            xml.WriteEndElement(); // Dbtr
            xml.WriteStartElement("DbtrAcct");
            xml.WriteStartElement("Id");
            xml.WriteElementString("IBAN", Iban.Compact(batch.DebtorIban));
            xml.WriteEndElement(); // Id
            xml.WriteEndElement(); // DbtrAcct
            xml.WriteStartElement("DbtrAgt");
            xml.WriteStartElement("FinInstnId");
            if (Bic(batch.DebtorBic) is { } bic)
            {
                xml.WriteElementString("BICFI", bic);
            }
            else
            {
                xml.WriteStartElement("Othr");
                xml.WriteElementString("Id", "NOTPROVIDED");
                xml.WriteEndElement(); // Othr
            }

            xml.WriteEndElement(); // FinInstnId
            xml.WriteEndElement(); // DbtrAgt
            xml.WriteElementString("ChrgBr", "SLEV");

            foreach (var transfer in batch.Transfers)
            {
                xml.WriteStartElement("CdtTrfTxInf");
                xml.WriteStartElement("PmtId");
                xml.WriteElementString("EndToEndId", transfer.EndToEndId);
                xml.WriteEndElement(); // PmtId
                xml.WriteStartElement("Amt");
                xml.WriteStartElement("InstdAmt");
                xml.WriteAttributeString("Ccy", "EUR");
                xml.WriteString(Money(transfer.Amount));
                xml.WriteEndElement(); // InstdAmt
                xml.WriteEndElement(); // Amt
                xml.WriteStartElement("Cdtr");
                xml.WriteElementString("Nm", Sepa(transfer.CreditorName, MaxName));
                xml.WriteEndElement(); // Cdtr
                xml.WriteStartElement("CdtrAcct");
                xml.WriteStartElement("Id");
                xml.WriteElementString("IBAN", Iban.Compact(transfer.CreditorIban));
                xml.WriteEndElement(); // Id
                xml.WriteEndElement(); // CdtrAcct
                if (Sepa(transfer.Purpose, MaxPurpose) is { Length: > 0 } purpose)
                {
                    xml.WriteStartElement("RmtInf");
                    xml.WriteElementString("Ustrd", purpose);
                    xml.WriteEndElement(); // RmtInf
                }

                xml.WriteEndElement(); // CdtTrfTxInf
            }

            xml.WriteEndElement(); // PmtInf
            xml.WriteEndElement(); // CstmrCdtTrfInitn
            xml.WriteEndElement(); // Document
            xml.WriteEndDocument();
        }

        return stream.ToArray();
    }

    /// <summary>Text in the SEPA character set (letters, digits, <c>/ - ? : ( ) . , + space</c>), cut to <paramref name="maxLength"/>; anything else is converted or replaced by a space.</summary>
    public static string Sepa(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var result = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormC))
        {
            result.Append(c switch
            {
                'ä' => "ae",
                'ö' => "oe",
                'ü' => "ue",
                'Ä' => "Ae",
                'Ö' => "Oe",
                'Ü' => "Ue",
                'ß' => "ss",
                '&' => "+",
                _ => Convert(c),
            });
        }

        var collapsed = string.Join(' ', result.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= maxLength ? collapsed : collapsed[..maxLength].TrimEnd();
    }

    private static string Convert(char c)
    {
        if (char.IsAsciiLetterOrDigit(c) || AllowedPunctuation.Contains(c, StringComparison.Ordinal))
        {
            return c.ToString();
        }

        // Accents are dropped ("é" -> "e"); everything outside the character set becomes a space.
        var letter = c.ToString().Normalize(NormalizationForm.FormD).FirstOrDefault(char.IsAsciiLetter);
        return letter != default ? letter.ToString() : " ";
    }

    internal static string Money(decimal amount) => decimal.Round(amount, 2).ToString("0.00", CultureInfo.InvariantCulture);

    internal static string? Bic(string? bic)
    {
        var compact = string.IsNullOrWhiteSpace(bic) ? "" : new string(bic.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
        return compact.Length is 8 or 11 && compact.All(char.IsAsciiLetterOrDigit) ? compact : null;
    }

    private static void Validate(CreditTransferBatch batch)
    {
        if (batch.Transfers.Count == 0)
        {
            throw new RuleViolationException("Es ist keine Überweisung ausgewählt.");
        }

        if (batch.Transfers.Count > MaxTransfers)
        {
            throw new RuleViolationException($"Eine Datei darf höchstens {MaxTransfers} Überweisungen enthalten.");
        }

        if (string.IsNullOrWhiteSpace(batch.MessageId) || batch.MessageId.Length > 35)
        {
            throw new RuleViolationException("Die Nachrichten-Id fehlt oder ist länger als 35 Zeichen.");
        }

        if (Sepa(batch.DebtorName, MaxName).Length == 0)
        {
            throw new RuleViolationException("Im Vermieterprofil fehlt der Name für die Überweisung.");
        }

        if (!Iban.IsValid(batch.DebtorIban))
        {
            throw new RuleViolationException("Im Vermieterprofil fehlt eine gültige IBAN für das Konto, von dem überwiesen wird.");
        }

        foreach (var transfer in batch.Transfers)
        {
            var who = string.IsNullOrWhiteSpace(transfer.CreditorName) ? "ein Empfänger" : transfer.CreditorName;
            if (Sepa(transfer.CreditorName, MaxName).Length == 0)
            {
                throw new RuleViolationException("Bei einer Überweisung fehlt der Name des Empfängers.");
            }

            if (!Iban.IsValid(transfer.CreditorIban))
            {
                throw new RuleViolationException($"Für {who} fehlt eine gültige IBAN.");
            }

            if (transfer.Amount < 0.01m || transfer.Amount > MaxAmount || decimal.Round(transfer.Amount, 2) != transfer.Amount)
            {
                throw new RuleViolationException($"Der Betrag für {who} ist ungültig (mindestens 0,01 €, höchstens zwei Nachkommastellen).");
            }

            if (string.IsNullOrWhiteSpace(transfer.EndToEndId) || transfer.EndToEndId.Length > 35 || !transfer.EndToEndId.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            {
                throw new RuleViolationException("Die Referenz einer Überweisung darf nur Buchstaben, Ziffern und Bindestriche enthalten (höchstens 35 Zeichen).");
            }
        }

        if (batch.Transfers.Select(t => t.EndToEndId).Distinct(StringComparer.Ordinal).Count() != batch.Transfers.Count)
        {
            throw new RuleViolationException("Die Referenzen der Überweisungen müssen verschieden sein.");
        }
    }
}
