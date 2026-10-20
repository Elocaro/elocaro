using System.Text.Json;
using MietHahn.Domain.Bank;
using MietHahn.Infrastructure.Bank.Import;

namespace MietHahn.Infrastructure.Bank.FinTs;

/// <summary>
/// Content of a <see cref="BankImport"/> with <see cref="BankFormat.FinTs"/>: the transactions the bank delivered, as JSON,
/// so that preview and import use the same path as for files.
/// </summary>
internal static class FinTsPayload
{
    private const string Marker = "fints";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static byte[] Write(string iban, IReadOnlyList<ParsedTransaction> transactions) =>
        JsonSerializer.SerializeToUtf8Bytes(new Document(Marker, iban, transactions), Json);

    public static bool Detects(byte[] content)
    {
        var span = content.AsSpan().TrimStart((byte)' ');
        return span.Length > 0 && span[0] == (byte)'{' && System.Text.Encoding.UTF8.GetString(span[..Math.Min(span.Length, 64)]).Contains($"\"source\":\"{Marker}\"", StringComparison.Ordinal);
    }

    internal static BankStatement Read(byte[] content)
    {
        var document = JsonSerializer.Deserialize<Document>(content, Json) ?? throw new BankFileErrorException("Der FinTS-Abruf ist unlesbar.");
        return new BankStatement(document.Iban, document.Transactions, []);
    }

    private sealed record Document(string Source, string Iban, IReadOnlyList<ParsedTransaction> Transactions);
}
