using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MietHahn.Application.Documents;

namespace MietHahn.Web.Tests.Browser;

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
            await page.Locator("[data-insert='{{ salutation }}']").ClickAsync();
            await Assertions.Expect(body).ToHaveValueAsync(new System.Text.RegularExpressions.Regex(@"^\{\{ salutation \}\}<p>Guten Tag,</p>$"));

            await page.GetByRole(AriaRole.Button, new() { Name = "Speichern" }).ClickAsync();
            await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Ankündigung");
        });

        var saved = await App.WithScopeAsync(async sp => (await sp.GetRequiredService<IDocumentTemplates>().ListAsync(TestContext.Current.CancellationToken)).Single(t => t.Name == "Ankündigung"));
        var body = await App.WithScopeAsync(async sp => (await sp.GetRequiredService<IDocumentTemplates>().GetAsync(saved.Id, TestContext.Current.CancellationToken))!.Body);
        Assert.Contains("{{ salutation }}", body, StringComparison.Ordinal);
    }
}
