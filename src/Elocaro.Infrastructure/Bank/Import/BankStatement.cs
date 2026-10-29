using System.Text;
using System.Text.RegularExpressions;
using Elocaro.Domain.Bank;

namespace Elocaro.Infrastructure.Bank.Import;

/// <summary>Result of a parser: account number according to the file (if present), transactions and notes on skipped lines.</summary>
internal sealed record BankStatement(string? Iban, IReadOnlyList<ParsedTransaction> Transactions, IReadOnlyList<string> Hints);

/// <summary>The file is not a readable bank statement; the message is worded for users.</summary>
internal sealed class BankFileErrorException(string message) : Exception(message);

/// <summary>Splits a SEPA remittance information field with identifiers (EREF+, MREF+, CRED+, SVWZ+, …).</summary>
internal static partial class SepaPurpose
{
    public sealed record Parts(string Purpose, string? EndToEndId, string? MandateReference, string? CreditorId, string? DifferentName);

    public static Parts Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var match = Identifier().Matches(text);
        if (match.Count == 0)
        {
            return new Parts(Clean(text), null, null, null, null);
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var before = text[..match[0].Index];
        for (var i = 0; i < match.Count; i++)
        {
            var start = match[i].Index + match[i].Length;
            var end = i + 1 < match.Count ? match[i + 1].Index : text.Length;
            values.TryAdd(match[i].Groups[1].Value, Clean(text[start..end]));
        }

        var purpose = values.GetValueOrDefault("SVWZ") ?? Clean(before);
        return new Parts(
            purpose,
            TrimToNull(values.GetValueOrDefault("EREF")),
            TrimToNull(values.GetValueOrDefault("MREF")),
            TrimToNull(values.GetValueOrDefault("CRED")),
            TrimToNull(values.GetValueOrDefault("ABWA")));
    }

    public static string Clean(string text) => Whitespace().Replace(text, " ").Trim();

    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) || value == "NOTPROVIDED" ? null : value;

    [GeneratedRegex(@"(EREF|KREF|MREF|CRED|DEBT|SVWZ|ABWA|ABWE|IBAN|BIC|COAM|OAMT)\+")]
    private static partial Regex Identifier();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
