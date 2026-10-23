using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MietHahn.Application.Receipts;
using MietHahn.Domain.Receipts;
using MietHahn.Infrastructure.Receipts.Extraction;

namespace MietHahn.Integration.Tests.Receipts.Extraction;

/// <summary>
/// Evaluation set (20 anonymised, invented receipts) with the hit rate per provider for amount and date (acceptance criterion
/// ≥ 95 %). The model answers are canned in the notations that real models produce (numbers, German text, code fences,
/// percent confidence), so the set guards the whole pipeline (request, answer handling, parsing, plausibility check) against
/// regressions. It does not measure the quality of a real model: for that, run the set against the live provider (docs/entwicklung.md).
/// One case is deliberately a wrong model answer (hallucinated amount) so that the hit rate is not trivially 100 %.
/// </summary>
public class EvaluationSetTests
{
    private static readonly DateOnly Today = new(2026, 10, 19);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Case(string Name, string Supplier, string Number, DateOnly Date, decimal Gross, decimal Net, decimal Vat, Style Style, decimal? WrongGross = null);

    private enum Style
    {
        Structured,
        GermanText,
        CodeFence,
        PercentConfidence,
        BareValues,
    }

    private static readonly Case[] Cases =
    [
        new("Hausmeisterservice Quartal", "Hausmeisterservice Schulz", "2026-0412", new(2026, 3, 31), 357.00m, 300.00m, 57.00m, Style.Structured),
        new("Gartenpflege", "Grünpflege Meier GmbH", "R-117", new(2026, 4, 30), 595.00m, 500.00m, 95.00m, Style.GermanText),
        new("Heizöl Lieferung", "Brennstoffe Nord KG", "HL-88213", new(2026, 1, 12), 2_856.40m, 2_400.34m, 456.06m, Style.CodeFence),
        new("Gebäudeversicherung", "Provinzial Versicherung AG", "V-2026-77", new(2026, 1, 5), 1_234.56m, 1_234.56m, 0m, Style.PercentConfidence),
        new("Schornsteinfeger", "Schornsteinfegermeister Krause", "2026/118", new(2026, 5, 20), 83.30m, 70.00m, 13.30m, Style.Structured),
        new("Wasser Abrechnung", "Stadtwerke Beispielstadt", "9981234", new(2026, 2, 3), 1_187.20m, 1_109.53m, 77.67m, Style.GermanText),
        new("Müllgebühren", "Abfallwirtschaftsbetrieb", "AWB-5512", new(2025, 12, 15), 312.00m, 312.00m, 0m, Style.BareValues),
        new("Dachreparatur", "Dachdeckerei Lorenz", "2026-031", new(2026, 6, 8), 4_760.00m, 4_000.00m, 760.00m, Style.CodeFence),
        new("Aufzugwartung", "Liftservice Brandt", "LS-4410", new(2026, 3, 2), 714.00m, 600.00m, 114.00m, Style.Structured),
        new("Allgemeinstrom", "Energie Beispiel", "E-2026-0099", new(2026, 4, 1), 168.45m, 141.55m, 26.90m, Style.PercentConfidence),
        new("Kontoführung", "Beispielbank eG", "K-2026-Q2", new(2026, 6, 30), 24.90m, 24.90m, 0m, Style.GermanText),
        new("Steuerberatung", "Kanzlei Weiß & Partner", "2026-1177", new(2026, 7, 14), 1_071.00m, 900.00m, 171.00m, Style.Structured),
        new("Rauchmelderwartung", "Brandschutz Winter", "BW-0815", new(2026, 8, 25), 142.80m, 120.00m, 22.80m, Style.BareValues),
        new("Treppenhausreinigung", "Reinigung Klar GmbH", "RK-2026-09", new(2026, 9, 1), 238.00m, 200.00m, 38.00m, Style.CodeFence),
        new("Gutschrift Heizkosten", "Brennstoffe Nord KG", "GS-1203", new(2026, 2, 18), -119.00m, -100.00m, -19.00m, Style.Structured),
        new("Winterdienst", "Winterdienst Frost", "WF-26-004", new(2026, 1, 31), 476.00m, 400.00m, 76.00m, Style.GermanText),
        new("Grundsteuer", "Stadt Beispielstadt", "GS-2026-3", new(2026, 1, 15), 842.18m, 842.18m, 0m, Style.PercentConfidence),
        new("Schlüsseldienst", "Schlüssel-Schmidt", "SS-2210", new(2026, 5, 6), 95.20m, 80.00m, 15.20m, Style.BareValues),
        new("Fotovoltaik-Wartung", "Solar Service Ost", "SSO-77", new(2026, 9, 12), 380.80m, 320.00m, 60.80m, Style.CodeFence),
        new("Fehler des Modells (erfundener Betrag)", "Hausmeisterservice Schulz", "2026-0500", new(2026, 9, 30), 357.00m, 300.00m, 57.00m, Style.Structured, WrongGross: 3_570.00m),
    ];

    [Fact]
    public async Task Claude_provider_reads_amount_and_date_of_at_least_95_percent_of_the_set()
    {
        var rate = await EvaluateAsync("Claude API", async c =>
        {
            var handler = c.Style == Style.CodeFence
                ? FakeHttpHandler.Json(FakeHttpHandler.ClaudeTextResponse(ModelOutput(c)))
                : FakeHttpHandler.Json(FakeHttpHandler.ClaudeToolResponse(JsonElement.Parse(ModelOutput(c))));
            var options = Options("Claude");
            return await new ClaudeReceiptExtractor(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(options), NullLogger<ClaudeReceiptExtractor>.Instance)
                .ExtractAsync([new("rechnung.pdf", "application/pdf", "%PDF-1.4"u8.ToArray())], new ExtractionContext(Today), Ct);
        });

        Assert.True(rate.Amount >= 0.95 && rate.Date >= 0.95, $"Betrag {rate.Amount:P0}, Datum {rate.Date:P0}");
    }

    [Fact]
    public async Task Ollama_provider_reads_amount_and_date_of_at_least_95_percent_of_the_set()
    {
        var rate = await EvaluateAsync("Ollama", async c =>
        {
            var handler = FakeHttpHandler.Json(FakeHttpHandler.OllamaResponse(ModelOutput(c)));
            return await new OllamaReceiptExtractor(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(Options("Ollama")), NullLogger<OllamaReceiptExtractor>.Instance)
                .ExtractAsync([new("rechnung.jpg", "image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 })], new ExtractionContext(Today), Ct);
        });

        Assert.True(rate.Amount >= 0.95 && rate.Date >= 0.95, $"Betrag {rate.Amount:P0}, Datum {rate.Date:P0}");
    }

    [Fact]
    public void The_set_has_20_cases_and_exactly_one_deliberate_model_error()
    {
        Assert.Equal(20, Cases.Length);
        Assert.Single(Cases, c => c.WrongGross is not null);
    }

    private static async Task<(double Amount, double Date)> EvaluateAsync(string provider, Func<Case, Task<ExtractorResult>> run)
    {
        int amountHits = 0, dateHits = 0, supplierHits = 0, numberHits = 0;
        var report = new List<string>();
        foreach (var c in Cases)
        {
            var result = await run(c);
            var s = SuggestionChecks.Check(result.Suggestion, Today);
            var amount = s.Gross.Value == c.Gross;
            var date = s.InvoiceDate.Value == c.Date;
            amountHits += amount ? 1 : 0;
            dateHits += date ? 1 : 0;
            supplierHits += s.SupplierName.Value == c.Supplier ? 1 : 0;
            numberHits += s.InvoiceNumber.Value == c.Number ? 1 : 0;
            report.Add($"{(amount && date ? "ok  " : "MISS")} {c.Name}");
        }

        var n = (double)Cases.Length;
        TestContext.Current.SendDiagnosticMessage(string.Create(CultureInfo.InvariantCulture,
            $"Auswertung {provider}: Betrag {amountHits}/{Cases.Length}, Datum {dateHits}/{Cases.Length}, Lieferant {supplierHits}/{Cases.Length}, Rechnungsnr. {numberHits}/{Cases.Length}\n{string.Join('\n', report)}"));
        return (amountHits / n, dateHits / n);
    }

    private static ReceiptExtractionOptions Options(string provider) => new()
    {
        Provider = provider,
        AcceptExternalProcessing = true,
        Claude = { ApiKey = "sk-test", BaseUrl = new Uri("https://claude.test/") },
    };

    /// <summary>The model answer in the notation of the case style.</summary>
    private static string ModelOutput(Case c)
    {
        var gross = c.WrongGross ?? c.Gross;
        string Number(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
        string German(decimal value) => value.ToString("#,##0.00", CultureInfo.GetCultureInfo("de-DE")) + " €";
        string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string Dotted(DateOnly d) => d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

        return c.Style switch
        {
            Style.GermanText => $$$"""
                {"supplier_name": {"value": "{{{c.Supplier}}}", "confidence": 0.95}, "invoice_number": {"value": "{{{c.Number}}}", "confidence": 0.9},
                 "invoice_date": {"value": "{{{Dotted(c.Date)}}}", "confidence": 0.95}, "gross": {"value": "{{{German(gross)}}}", "confidence": 0.95},
                 "net": {"value": "{{{German(c.Net)}}}", "confidence": 0.9}, "vat": {"value": "{{{German(c.Vat)}}}", "confidence": 0.9}}
                """,
            Style.BareValues => $$$"""
                {"supplier_name": "{{{c.Supplier}}}", "invoice_number": "{{{c.Number}}}", "invoice_date": "{{{Iso(c.Date)}}}", "gross": {{{Number(gross)}}}, "net": {{{Number(c.Net)}}}, "vat": {{{Number(c.Vat)}}}}
                """,
            Style.PercentConfidence => $$$"""
                {"supplier_name": {"value": "{{{c.Supplier}}}", "confidence": 96}, "invoice_number": {"value": "{{{c.Number}}}", "confidence": 90},
                 "invoice_date": {"value": "{{{Iso(c.Date)}}}", "confidence": 97}, "gross": {"value": {{{Number(gross)}}}, "confidence": 99},
                 "net": {"value": {{{Number(c.Net)}}}, "confidence": 99}, "vat": {"value": {{{Number(c.Vat)}}}, "confidence": 99}}
                """,
            Style.CodeFence => $$$"""
                Gern, hier die Daten:
                ```json
                {"supplier_name": {"value": "{{{c.Supplier}}}", "confidence": 0.9}, "invoice_number": {"value": "{{{c.Number}}}", "confidence": 0.9},
                 "invoice_date": {"value": "{{{Dotted(c.Date)}}}", "confidence": 0.9}, "gross": {"value": "{{{Number(gross)}}}", "confidence": 0.9},
                 "net": {"value": {{{Number(c.Net)}}}, "confidence": 0.9}, "vat": {"value": {{{Number(c.Vat)}}}, "confidence": 0.9}}
                ```
                """,
            _ => $$$"""
                {"supplier_name": {"value": "{{{c.Supplier}}}", "confidence": 0.98}, "invoice_number": {"value": "{{{c.Number}}}", "confidence": 0.98},
                 "invoice_date": {"value": "{{{Iso(c.Date)}}}", "confidence": 0.98}, "gross": {"value": {{{Number(gross)}}}, "confidence": 0.99},
                 "net": {"value": {{{Number(c.Net)}}}, "confidence": 0.99}, "vat": {"value": {{{Number(c.Vat)}}}, "confidence": 0.99}}
                """,
        };
    }
}
