using System.Net;
using System.Text;
using AngleSharp.Html.Dom;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Infrastructure.Receipts.Extraction;
using MietHahn.Web.Tests.Account;

namespace MietHahn.Web.Tests.Receipts;

/// <summary>The real Claude provider (configuration only) against a fake HTTP handler: no real network traffic.</summary>
public class ReceiptExtractionClaudeFlowTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private static readonly byte[] Pdf = "%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF\n"u8.ToArray();

    private readonly List<string> bodies = [];

    protected override IDictionary<string, string?>? Configuration => new Dictionary<string, string?>
    {
        ["ReceiptExtraction:Provider"] = "Claude",
        ["ReceiptExtraction:AcceptExternalProcessing"] = "true",
        ["ReceiptExtraction:Claude:ApiKey"] = "sk-ant-web-test",
        ["ReceiptExtraction:Claude:BaseUrl"] = "https://claude.test/",
    };

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddHttpClient<ClaudeReceiptExtractor>().ConfigurePrimaryHttpMessageHandler(() => new Handler(bodies));

    [Fact]
    public async Task Upload_extract_and_confirm_with_the_claude_provider()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        var page = await admin.GetAsync("/verwaltung/belege/neu");
        Assert.Contains("an einen Dritten übermittelt", page.Document.QuerySelector("#extraction-notice")!.TextContent, StringComparison.Ordinal);
        page = await admin.SubmitWithFileAsync(page, "extract", "ExtractFiles", Pdf, "rechnung.pdf", "application/pdf");

        Assert.Equal("Stadtwerke Beispielstadt", ((IHtmlInputElement)page.Document.QuerySelector("#receipt-supplier-name")!).Value);
        Assert.Equal("2026-03-15", ((IHtmlInputElement)page.Document.QuerySelector("#receipt-date")!).Value);
        Assert.Contains("640 Eingabe- und 120 Ausgabe-Tokens", page.Document.QuerySelector("#suggestion-usage")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("IBAN laut Beleg", page.Text, StringComparison.Ordinal);
        var body = Assert.Single(bodies);
        Assert.Contains("\"claude-sonnet-5-5\"", body, StringComparison.Ordinal);
        Assert.Contains("\"media_type\":\"application/pdf\"", body, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"document\"", body, StringComparison.Ordinal);

        var settings = await admin.GetAsync("/verwaltung/system/belegauslesung");
        Assert.Contains("externer Dienst", settings.Document.QuerySelector("#third-party-notice")!.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-ant-web-test", settings.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-ant-web-test", page.Html, StringComparison.Ordinal);
    }

    private sealed class Handler(List<string> bodies) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            const string json = """
                {"type":"message","content":[{"type":"tool_use","name":"record_receipt","input":{
                  "supplier_name":{"value":"Stadtwerke Beispielstadt","confidence":0.97},
                  "supplier_iban":{"value":"DE89370400440532013000","confidence":0.9},
                  "invoice_date":{"value":"2026-03-15","confidence":0.98},
                  "gross":{"value":119.00,"confidence":0.99},"net":{"value":100.00,"confidence":0.99},"vat":{"value":19.00,"confidence":0.99}
                }}],"usage":{"input_tokens":640,"output_tokens":120}}
                """;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
