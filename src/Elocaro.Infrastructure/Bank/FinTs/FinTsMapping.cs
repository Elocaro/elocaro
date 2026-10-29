using Elocaro.Domain.Bank;
using Elocaro.Domain.Common;
using Elocaro.Infrastructure.Bank.Import;
using libfintx.FinTS.Camt;

namespace Elocaro.Infrastructure.Bank.FinTs;

/// <summary>Translates the camt transactions of libfintx into <see cref="ParsedTransaction"/>s.</summary>
internal static class FinTsMapping
{
    public static IReadOnlyList<ParsedTransaction> FromCamt(IEnumerable<CamtStatement> statements)
    {
        ArgumentNullException.ThrowIfNull(statements);
        return [.. statements.SelectMany(s => s.Transactions.Select(t => FromCamt(t, s.Currency)))
            .OfType<ParsedTransaction>()];
    }

    /// <summary>Pending (not yet booked) items are skipped; <c>null</c> for them.</summary>
    public static ParsedTransaction? FromCamt(CamtTransaction transaction, string? currency = null)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (transaction.Pending)
        {
            return null;
        }

        var booking = DateOnly.FromDateTime(transaction.InputDate != default ? transaction.InputDate : transaction.ValueDate);
        var counterIban = Clean(transaction.AccountCode);
        var counterBic = Clean(transaction.BankCode);
        return new ParsedTransaction(
            booking,
            transaction.Amount,
            SepaPurpose.Clean(transaction.Description ?? ""),
            transaction.ValueDate == default ? null : DateOnly.FromDateTime(transaction.ValueDate),
            string.IsNullOrWhiteSpace(currency) ? "EUR" : currency,
            Clean(transaction.PartnerName),
            counterIban is not null && Iban.IsValid(counterIban) ? Iban.Compact(counterIban) : null,
            counterBic is { Length: 8 or 11 } && counterBic.All(char.IsAsciiLetterOrDigit) ? counterBic : null,
            Clean(transaction.Text),
            Reference(transaction.EndToEndId),
            Reference(transaction.MandateId));
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : SepaPurpose.Clean(value);

    private static string? Reference(string? value) => Clean(value) is { } text && text != "NOTPROVIDED" ? text : null;
}
