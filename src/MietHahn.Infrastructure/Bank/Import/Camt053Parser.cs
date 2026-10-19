using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using MietHahn.Domain.Bank;

namespace MietHahn.Infrastructure.Bank.Import;

/// <summary>
/// ISO 20022 camt.053 (Kontoauszug), getestet mit .001.02 und .001.08. Elemente werden über ihren lokalen Namen
/// gelesen, damit alle Versionen funktionieren. Vorgemerkte Umsätze (Status PDNG) werden übersprungen; Sammelbuchungen
/// mit mehreren Transaktionsdetails ergeben je Detail einen Umsatz.
/// </summary>
internal static class Camt053Parser
{
    public static bool Erkennt(byte[] inhalt)
    {
        var anfang = Textdekodierung.Lesen(inhalt.Length > 4096 ? inhalt[..4096] : inhalt);
        return anfang.Contains("camt.053", StringComparison.Ordinal) || anfang.Contains("BkToCstmrStmt", StringComparison.Ordinal);
    }

    public static Kontoauszug Lesen(byte[] inhalt)
    {
        XDocument dokument;
        try
        {
            using var strom = new MemoryStream(inhalt);
            using var leser = XmlReader.Create(strom, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            dokument = XDocument.Load(leser);
        }
        catch (XmlException ex)
        {
            throw new BankdateiFehlerException($"Die CAMT-Datei ist kein gültiges XML: {ex.Message}");
        }

        var auszuege = Alle(dokument.Root!, "Stmt").ToList();
        if (auszuege.Count == 0)
        {
            throw new BankdateiFehlerException("Die Datei enthält keinen camt.053-Kontoauszug (Stmt).");
        }

        var iban = Text(auszuege[0], "Acct", "Id", "IBAN");
        var umsaetze = new List<GelesenerUmsatz>();
        var hinweise = new List<string>();
        foreach (var eintrag in auszuege.SelectMany(a => Kinder(a, "Ntry")))
        {
            var status = Text(eintrag, "Sts", "Cd") ?? Text(eintrag, "Sts");
            if (status is not null and not "BOOK")
            {
                hinweise.Add($"Nicht gebuchter Umsatz übersprungen ({status}).");
                continue;
            }

            var buchung = Datum(Kind(eintrag, "BookgDt")) ?? throw new BankdateiFehlerException("Ein Umsatz hat kein Buchungsdatum.");
            var valuta = Datum(Kind(eintrag, "ValDt"));
            var eingang = Text(eintrag, "CdtDbtInd") == "CRDT";
            var betragEintrag = Betrag(Kind(eintrag, "Amt"));
            var waehrung = Kind(eintrag, "Amt")?.Attribute("Ccy")?.Value ?? "EUR";
            var buchungstext = Text(eintrag, "AddtlNtryInf");
            var details = Alle(eintrag, "TxDtls").ToList();
            if (details.Count == 0)
            {
                umsaetze.Add(new GelesenerUmsatz(buchung, eingang ? betragEintrag : -betragEintrag, SepaZweck.Bereinigen(buchungstext ?? ""), valuta, waehrung, Buchungstext: buchungstext));
                continue;
            }

            foreach (var detail in details)
            {
                // Bei Sammelbuchungen hat jedes Detail seinen eigenen Betrag.
                var betragDetail = details.Count == 1 ? betragEintrag : Betrag(Pfad(detail, "AmtDtls", "TxAmt", "Amt") ?? Kind(detail, "Amt"));
                var eingangDetail = Text(detail, "CdtDbtInd") is { } ind ? ind == "CRDT" : eingang;
                var gegen = eingangDetail ? "Dbtr" : "Cdtr";
                var partei = Pfad(detail, "RltdPties", gegen);
                var zweck = string.Join(" ", Alle(detail, "Ustrd").Select(u => u.Value));
                var sepa = SepaZweck.Zerlegen(zweck);
                umsaetze.Add(new GelesenerUmsatz(
                    buchung,
                    eingangDetail ? betragDetail : -betragDetail,
                    sepa.Verwendungszweck,
                    valuta,
                    waehrung,
                    GegenName: partei is null ? null : Alle(partei, "Nm").FirstOrDefault()?.Value.Trim(),
                    GegenIban: Text(detail, "RltdPties", gegen + "Acct", "Id", "IBAN"),
                    GegenBic: Pfad(detail, "RltdAgts", gegen + "Agt", "FinInstnId") is { } institut ? (Text(institut, "BICFI") ?? Text(institut, "BIC")) : null,
                    Buchungstext: buchungstext,
                    EndToEndId: Ohne(Text(detail, "Refs", "EndToEndId")) ?? sepa.EndToEndId,
                    Mandatsreferenz: Ohne(Text(detail, "Refs", "MndtId")) ?? sepa.Mandatsreferenz,
                    GlaeubigerId: Glaeubiger(detail) ?? sepa.GlaeubigerId));
            }
        }

        return new Kontoauszug(iban, umsaetze, hinweise);
    }

    // .001.02: Cdtr/Id/PrvtId/Othr/Id, .001.08: Cdtr/Pty/Id/PrvtId/Othr/Id
    private static string? Glaeubiger(XElement detail) =>
        Pfad(detail, "RltdPties", "Cdtr") is { } glaeubiger && Alle(glaeubiger, "PrvtId").FirstOrDefault() is { } privat ? Text(privat, "Othr", "Id") : null;

    private static string? Ohne(string? wert) => string.IsNullOrWhiteSpace(wert) || wert == "NOTPROVIDED" ? null : wert.Trim();

    private static decimal Betrag(XElement? element) =>
        element is not null && decimal.TryParse(element.Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var betrag)
            ? betrag
            : throw new BankdateiFehlerException("Ein Umsatz hat keinen lesbaren Betrag.");

    private static DateOnly? Datum(XElement? element)
    {
        var text = element is null ? null : Text(element, "Dt") ?? Text(element, "DtTm");
        return text is null ? null : DateOnly.ParseExact(text[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static XElement? Kind(XElement element, string name) => element.Elements().FirstOrDefault(e => e.Name.LocalName == name);

    private static IEnumerable<XElement> Kinder(XElement element, string name) => element.Elements().Where(e => e.Name.LocalName == name);

    private static IEnumerable<XElement> Alle(XElement element, string name) => element.Descendants().Where(e => e.Name.LocalName == name);

    private static XElement? Pfad(XElement element, params string[] namen)
    {
        XElement? aktuell = element;
        foreach (var name in namen)
        {
            aktuell = aktuell is null ? null : Kind(aktuell, name);
        }

        return aktuell;
    }

    private static string? Text(XElement element, params string[] namen) => Pfad(element, namen)?.Value.Trim() is { Length: > 0 } wert ? wert : null;
}
