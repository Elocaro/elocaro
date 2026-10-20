using MietHahn.Application.Receipts;
using MietHahn.Domain.Receipts;

namespace MietHahn.Web.Tests.Receipts;

/// <summary>Provider stand-in: returns the prepared suggestion through the real use case (checks, rules, contacts).</summary>
internal sealed class StubExtractor : IReceiptExtractor
{
    public bool Enabled { get; set; } = true;

    public bool Ready { get; set; } = true;

    public bool External { get; set; } = true;

    public string? Failure { get; set; }

    public ReceiptSuggestion Suggestion { get; set; } = new();

    public List<int> Calls { get; } = [];

    public ExtractorInfo Info => new(
        !Enabled ? "Keiner" : External ? "Claude API" : "Ollama", "test-modell", Enabled, Ready && Enabled, External,
        External ? "Achtung: Zum Auslesen werden die Belege an einen Dritten übermittelt (Claude API)." : "Lokal.",
        Ready ? null : "Es ist kein API-Schlüssel konfiguriert.");

    public Task<ExtractorResult> ExtractAsync(IReadOnlyList<ExtractionDocument> documents, ExtractionContext context, CancellationToken cancellationToken = default)
    {
        Calls.Add(documents.Count);
        return Failure is not null
            ? throw new ExtractionException(Failure)
            : Task.FromResult(new ExtractorResult(Suggestion, new ExtractionUsage(1200, 300)));
    }

    /// <summary>The suggestion most tests start from (a gardening invoice with an uncertain cost type and number).</summary>
    public static ReceiptSuggestion GardenInvoice() => new()
    {
        SupplierName = new Suggested<string>("Grünpflege Meier", 0.97),
        InvoiceNumber = new Suggested<string>("R-117", 0.5),
        InvoiceDate = new Suggested<DateOnly?>(new DateOnly(2026, 4, 30), 0.98),
        Gross = new Suggested<decimal?>(595m, 0.99),
        Net = new Suggested<decimal?>(500m, 0.99),
        Vat = new Suggested<decimal?>(95m, 0.99),
        CostType = new Suggested<string>("betrkv-10", 0.6),
        Warnings = ["Der Umsatzsteuersatz (19,0 %) ist ungewöhnlich (Testwarnung: ungewöhnlich)."],
    };
}
