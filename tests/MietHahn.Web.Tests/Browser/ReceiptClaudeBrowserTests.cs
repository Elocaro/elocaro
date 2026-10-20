using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MietHahn.Infrastructure.Receipts.Extraction;

namespace MietHahn.Web.Tests.Browser;

/// <summary>The real Claude provider (configuration only) against a fake HTTP handler: no real network traffic.</summary>
public class ReceiptClaudeBrowserTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
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
        var page = await SignedInAsAdminAsync(mobile: false);
        await GotoInteractiveAsync(page, "/verwaltung/belege/neu");

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await Assertions.Expect(page.Locator("#extraction-notice")).ToContainTextAsync("an einen Dritten übermittelt");
            await page.Locator("input[type=file]").SetInputFilesAsync(new FilePayload { Name = "rechnung.pdf", MimeType = "application/pdf", Buffer = Pdf });
            await page.GetByRole(AriaRole.Button, new() { Name = "Vorschlag aus Beleg auslesen" }).ClickAsync();

            await Assertions.Expect(page.GetByLabel("oder Name")).ToHaveValueAsync("Stadtwerke Beispielstadt");
            await Assertions.Expect(page.GetByLabel("Rechnungsdatum")).ToHaveValueAsync("15.03.2026");
            await Assertions.Expect(page.Locator("#suggestion-usage")).ToContainTextAsync("640 Eingabe- und 120 Ausgabe-Tokens");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("IBAN laut Beleg");
            Assert.DoesNotContain("sk-ant-web-test", await page.ContentAsync(), StringComparison.Ordinal);
        });

        var body = Assert.Single(bodies);
        Assert.Contains("\"claude-sonnet-5-5\"", body, StringComparison.Ordinal);
        Assert.Contains("\"media_type\":\"application/pdf\"", body, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"document\"", body, StringComparison.Ordinal);

        await page.GotoAsync(BaseUrl + "/verwaltung/system/belegauslesung");
        await Assertions.Expect(page.Locator("#third-party-notice")).ToContainTextAsync("externer Dienst");
        Assert.DoesNotContain("sk-ant-web-test", await page.ContentAsync(), StringComparison.Ordinal);
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
