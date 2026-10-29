using Elocaro.Application.Documents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Elocaro.Web.Tests.Browser;

/// <summary>Template editor: placeholders are inserted at the cursor by one click.</summary>
public class TemplatesBrowserTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddScoped<IPdfGenerator, FakePdfGenerator>();

    [Fact]
    public async Task Placeholder_is_inserted_at_the_cursor_and_saved()
    {
        var page = await SignedInAsAdminAsync();
        await GotoInteractiveAsync(page, "/verwaltung/einstellungen/vorlagen/neu");

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await page.GetByLabel("Name", new() { Exact = false }).First.FillAsync("Ankündigung");
            await page.GetByLabel("Betreff").FillAsync("Gerüst");
            var body = page.Locator("#template-body textarea").First;
            await body.FillAsync("<p>Guten Tag,</p>");
            await body.PressAsync("Home");
            await page.GetByRole(AriaRole.Tab, new() { Name = "Platzhalter" }).ClickAsync();
            await page.Locator("[data-insert='{{ salutation }}']").ClickAsync();
            await Assertions.Expect(body).ToHaveValueAsync(new System.Text.RegularExpressions.Regex(@"^\{\{ salutation \}\}<p>Guten Tag,</p>$"));

            // The editor passes its text on to the page with a short delay; under load a click right after typing can come first.
            for (var attempt = 1; ; attempt++)
            {
                await page.GetByRole(AriaRole.Button, new() { Name = "Speichern" }).ClickAsync();
                try
                {
                    await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Ankündigung", new() { Timeout = 10000 });
                    break;
                }
                catch (PlaywrightException) when (attempt < 3)
                {
                }
            }
        });

        var saved = await App.WithScopeAsync(async sp => (await sp.GetRequiredService<IDocumentTemplates>().ListAsync(TestContext.Current.CancellationToken)).Single(t => t.Name == "Ankündigung"));
        var body = await App.WithScopeAsync(async sp => (await sp.GetRequiredService<IDocumentTemplates>().GetAsync(saved.Id, TestContext.Current.CancellationToken))!.Body);
        Assert.Contains("{{ salutation }}", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Editor_highlights_syntax_formats_with_the_toolbar_and_updates_the_live_preview()
    {
        var page = await SignedInAsAdminAsync(mobile: false);
        await GotoInteractiveAsync(page, "/verwaltung/einstellungen/vorlagen/neu");

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            var body = page.Locator("#template-body textarea").First;
            // The editor script attaches shortly after the page is interactive; a text entered before that is not highlighted. Enter it again until it is.
            for (var attempt = 1; ; attempt++)
            {
                await body.FillAsync("<p class=\"x\">Guten Tag, {{ if lease_end }}Ende{{ end }} &nbsp;</p>");
                try
                {
                    await Assertions.Expect(page.Locator(".code-highlight .tok-tag").First).ToHaveTextAsync("<p", new() { Timeout = 4000 });
                    break;
                }
                catch (PlaywrightException) when (attempt < 4)
                {
                    await body.FillAsync("");
                }
            }

            // Syntax highlighting: tag, Scriban delimiter, keyword and entity get their own tokens.
            await Assertions.Expect(page.Locator(".code-highlight .tok-delim").Filter(new() { HasText = "{{" }).First).ToHaveTextAsync("{{");
            await Assertions.Expect(page.Locator(".code-highlight .tok-keyword").First).ToHaveTextAsync("if");
            await Assertions.Expect(page.Locator(".code-highlight .tok-entity").First).ToHaveTextAsync("&nbsp;");

            // Toolbar: bold wraps the selection.
            await body.FillAsync("Wichtig");
            await body.SelectTextAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Fett" }).ClickAsync();
            await Assertions.Expect(body).ToHaveValueAsync("<strong>Wichtig</strong>");

            // Live preview: the text shows up in the sheet without saving and without the PDF service.
            await body.FillAsync("<p>Vorschautext</p>");
            await Assertions.Expect(page.FrameLocator("iframe.template-preview").Locator("p")).ToHaveTextAsync("Vorschautext", new() { Timeout = 10000 });
        });
    }

    [Fact]
    public async Task Preview_reports_template_errors_while_typing()
    {
        var page = await SignedInAsAdminAsync(mobile: false);
        await GotoInteractiveAsync(page, "/verwaltung/einstellungen/vorlagen/neu");

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await page.GetByLabel("Betreff").FillAsync("Test");
            await page.Locator("#template-body textarea").First.FillAsync("<p>{{ gibt_es_nicht }}</p>");
            await Assertions.Expect(page.Locator(".message").Filter(new() { HasText = "gibt_es_nicht" }).First).ToBeVisibleAsync(new() { Timeout = 10000 });
        });
    }
}
