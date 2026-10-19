using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MietHahn.Domain.Bank;

namespace MietHahn.Infrastructure.Bank.Import;

/// <summary>
/// SWIFT MT940 mit deutschem strukturiertem Feld 86 (GVC, ?00 Buchungstext, ?20–?29 und ?60–?63 Verwendungszweck,
/// ?30 BLZ/BIC, ?31 Konto/IBAN, ?32/?33 Name). SEPA-Kennungen (EREF+, MREF+, CRED+, SVWZ+) werden ausgewertet.
/// </summary>
internal static partial class Mt940Parser
{
    public static bool Detects(byte[] content)
    {
        var text = TextDecoding.Read(content);
        return text.Contains(":20:", StringComparison.Ordinal) && text.Contains(":61:", StringComparison.Ordinal) && text.Contains(":25:", StringComparison.Ordinal);
    }

    public static BankStatement Read(byte[] content)
    {
        var fields = Fields(TextDecoding.Read(content));
        string? account = null;
        var transactions = new List<ParsedTransaction>();
        var hints = new List<string>();
        for (var i = 0; i < fields.Count; i++)
        {
            var (day, value) = fields[i];
            if (day == "25")
            {
                account ??= Account(value);
            }
            else if (day == "61")
            {
                var nextOne = i + 1 < fields.Count && fields[i + 1].Day == "86" ? fields[i + 1].Value : null;
                transactions.Add(Transaction(value, nextOne));
            }
        }

        if (transactions.Count == 0 && account is null)
        {
            throw new BankFileErrorException("Die Datei enthält keine MT940-Umsätze (:61:).");
        }

        return new BankStatement(account, transactions, hints);
    }

    /// <summary>Felder „:tag:wert“; Folgezeilen gehören zum vorherigen Feld (Zeilenumbrüche entfallen).</summary>
    private static List<(string Day, string Value)> Fields(string text)
    {
        var fields = new List<(string, StringBuilder)>();
        foreach (var line in text.Replace("\r", "", StringComparison.Ordinal).Split('\n'))
        {
            var match = FieldStart().Match(line);
            if (match.Success)
            {
                fields.Add((match.Groups[1].Value, new StringBuilder(line[match.Length..])));
            }
            else if (fields.Count > 0 && line.Length > 0 && line != "-" && !line.StartsWith('{'))
            {
                fields[^1].Item2.Append(line);
            }
        }

        return fields.Select(f => (f.Item1, f.Item2.ToString())).ToList();
    }

    // „BLZ/Kontonummer“ oder IBAN
    private static string? Account(string value)
    {
        var compact = value.Replace(" ", "", StringComparison.Ordinal);
        return Domain.Common.Iban.IsValid(compact) ? compact : null;
    }

    private static ParsedTransaction Transaction(string field61, string? field86)
    {
        var match = Field61().Match(field61);
        if (!match.Success)
        {
            throw new BankFileErrorException($"Unlesbare Umsatzzeile :61:{field61}");
        }

        var valueDate = DateOnly.ParseExact(match.Groups["valuta"].Value, "yyMMdd", CultureInfo.InvariantCulture);
        var booking = valueDate;
        if (match.Groups["buchung"].Success)
        {
            // Buchungsdatum ohne Jahr: liegt höchstens wenige Tage neben der Valuta, ggf. über den Jahreswechsel.
            var month = int.Parse(match.Groups["buchung"].Value[..2], CultureInfo.InvariantCulture);
            var day = int.Parse(match.Groups["buchung"].Value[2..], CultureInfo.InvariantCulture);
            var candidates = new[] { valueDate.Year - 1, valueDate.Year, valueDate.Year + 1 }
                .Where(j => day <= DateTime.DaysInMonth(j, month))
                .Select(j => new DateOnly(j, month, day));
            booking = candidates.MinBy(d => Math.Abs(d.DayNumber - valueDate.DayNumber));
        }

        var amount = decimal.Parse(match.Groups["betrag"].Value.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        if (match.Groups["dc"].Value is "D" or "RC")
        {
            amount = -amount;
        }

        if (field86 is null)
        {
            return new ParsedTransaction(booking, amount, "", valueDate);
        }

        var subfields = SubFields(field86);
        var purposeRaw = string.Concat(subfields.Where(u => u.Key is >= 20 and <= 29 or >= 60 and <= 63).OrderBy(u => u.Key).Select(u => u.Value));
        if (subfields.Count == 0)
        {
            purposeRaw = field86;
        }

        var sepa = SepaPurpose.Parse(purposeRaw);
        var name = (subfields.GetValueOrDefault(32) + subfields.GetValueOrDefault(33)).Trim();
        var bic = subfields.GetValueOrDefault(30)?.Trim();
        var iban = subfields.GetValueOrDefault(31)?.Trim();
        return new ParsedTransaction(
            booking,
            amount,
            sepa.Purpose,
            valueDate,
            CounterpartyName: name.Length == 0 ? sepa.DifferentName : name,
            CounterpartyIban: iban is not null && Domain.Common.Iban.IsValid(iban) ? iban : null,
            CounterpartyBic: bic is { Length: 8 or 11 } && char.IsLetter(bic[0]) ? bic : null,
            BookingText: subfields.GetValueOrDefault(0)?.Trim(),
            EndToEndId: sepa.EndToEndId,
            MandateReference: sepa.MandateReference,
            CreditorId: sepa.CreditorId);
    }

    /// <summary>„166?00GUTSCHRIFT?20EREF+…“: drei Ziffern GVC, danach Trennzeichen + zwei Ziffern je Unterfeld.</summary>
    private static Dictionary<int, string> SubFields(string field86)
    {
        var result = new Dictionary<int, string>();
        if (field86.Length < 4 || !char.IsAsciiDigit(field86[0]))
        {
            return result;
        }

        var separator = field86[3];
        foreach (var part in field86[4..].Split(separator))
        {
            if (part.Length >= 2 && int.TryParse(part[..2], NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                result[number] = result.GetValueOrDefault(number) + part[2..];
            }
        }

        return result;
    }

    [GeneratedRegex(@"^:(\d{2}[A-Z]?):")]
    private static partial Regex FieldStart();

    [GeneratedRegex(@"^(?<valuta>\d{6})(?<buchung>\d{4})?(?<dc>RC|RD|C|D)[A-Z]?(?<betrag>\d+,\d*)")]
    private static partial Regex Field61();
}
