using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Elocaro.Domain.Bank;

/// <summary>
/// Fingerprint of a transaction for duplicate detection from account, booking date, amount, purpose,
/// counterparty IBAN and end-to-end id. Identical transactions within one file (e.g. two identical transfers on the same day)
/// are numbered so that they do not count as duplicates, whereas re-importing the same file does.
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

    // Banks wrap payment purposes differently; whitespace and letter case do not matter.
    private static string Normalize(string? text) =>
        string.Concat((text ?? "").Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
}
