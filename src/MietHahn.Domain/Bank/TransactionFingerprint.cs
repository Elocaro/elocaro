using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MietHahn.Domain.Bank;

/// <summary>
/// Fingerabdruck eines Umsatzes für die Duplikaterkennung aus Konto, Buchungsdatum, Betrag, Verwendungszweck,
/// Gegen-IBAN und End-to-End-ID. Gleiche Umsätze innerhalb einer Datei (z. B. zwei gleiche Überweisungen am selben Tag)
/// werden durchnummeriert, damit sie nicht als Duplikat gelten, ein erneuter Import derselben Datei aber schon.
/// </summary>
public static class TransactionFingerprint
{
    public static IReadOnlyList<string> Calculate(string accountIban, IEnumerable<ParsedTransaction> transactions)
    {
        ArgumentNullException.ThrowIfNull(transactions);
        var meter = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var transaction in transactions)
        {
            var baseValue = Base(accountIban, transaction);
            meter[baseValue] = meter.GetValueOrDefault(baseValue) + 1;
            result.Add(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{baseValue}|{meter[baseValue]}"))));
        }

        return result;
    }

    private static string Base(string accountIban, ParsedTransaction u) => string.Join('|',
        Common.Iban.Compact(accountIban),
        u.BookingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        u.Amount.ToString("0.00", CultureInfo.InvariantCulture),
        Normalize(u.Purpose),
        u.CounterpartyIban is null ? "" : Common.Iban.Compact(u.CounterpartyIban),
        Normalize(u.EndToEndId is "NOTPROVIDED" ? null : u.EndToEndId));

    // Banken umbrechen Verwendungszwecke unterschiedlich; Leerraum und Groß/Klein spielen keine Rolle.
    private static string Normalize(string? text) =>
        string.Concat((text ?? "").Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
}
