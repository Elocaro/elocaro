using Elocaro.Application.Admin;
using Elocaro.Application.Documents;
using Elocaro.Application.Properties;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Elocaro.Web.Tests.Browser;

/// <summary>
/// The setup wizard as a user operates it: step by step with progress, back, and finish. With <c>ELOCARO_TOUR_DIR</c> set the test also
/// saves screenshots of the steps (light and dark), to look at the design.
/// </summary>
public class SetupWizardBrowserTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Wizard_leads_through_few_steps_with_progress_and_creates_the_first_data(bool mobile)
    {
        var page = await SignedInAsAdminAsync(mobile);
        await page.GotoAsync(BaseUrl + "/verwaltung/einrichtung");
        await Assertions.Expect(page.Locator("[data-interactive=true]")).ToHaveCountAsync(1);

        var heading = page.Locator("#wizard-heading");
        var count = page.Locator(".wizard-count");
        var next = page.GetByRole(AriaRole.Button, new() { Name = "Weiter", Exact = true });
        await Assertions.Expect(heading).ToHaveTextAsync("Willkommen");
        await Assertions.Expect(count).ToContainTextAsync("Schritt 1 von 7");
        await Shot(page, "01-welcome");

        // The navigation that leads away is hidden while the wizard runs.
        if (!mobile)
        {
            await Assertions.Expect(page.Locator(".sidebar")).ToBeHiddenAsync();
        }

        await next.ClickAsync();
        await Assertions.Expect(heading).ToHaveTextAsync("Funktionen");
        await Assertions.Expect(count).ToContainTextAsync("Schritt 2 von 7");
        await Shot(page, "02-features");

        await page.GetByRole(AriaRole.Button, new() { Name = "Zurück" }).ClickAsync();
        await Assertions.Expect(heading).ToHaveTextAsync("Willkommen");
        await next.ClickAsync();

        // Turn off a module, then walk on: options, sender data, first property, books, summary.
        await page.GetByLabel("Bank & Zahlungsabgleich").UncheckAsync();
        await next.ClickAsync();
        await Assertions.Expect(heading).ToHaveTextAsync("Ihre Vermietung");
        await next.ClickAsync();

        // Required sender data keep the wizard on this step.
        await Assertions.Expect(heading).ToHaveTextAsync("Ihre Absenderdaten");
        await next.ClickAsync();
        await Assertions.Expect(heading).ToHaveTextAsync("Ihre Absenderdaten");
        await Assertions.Expect(page.Locator(".wizard-card")).ToContainTextAsync("Bitte den Namen angeben.");
        await page.GetByLabel("Name", new() { Exact = true }).FillAsync("Hausverwaltung Beispiel");
        await page.GetByLabel("Straße und Hausnummer", new() { Exact = true }).FillAsync("Weg 1");
        await page.GetByLabel("PLZ", new() { Exact = true }).FillAsync("50667");
        await page.GetByLabel("Ort", new() { Exact = true }).FillAsync("Köln");
        await next.ClickAsync();

        await Assertions.Expect(heading).ToHaveTextAsync("Erstes Objekt");
        await page.GetByLabel("Erstes Objekt jetzt anlegen").CheckAsync();
        await page.GetByLabel("Bezeichnung", new() { Exact = true }).FillAsync("Lindenweg 3");
        await page.GetByLabel("Straße und Hausnummer", new() { Exact = true }).FillAsync("Lindenweg 3");
        await page.GetByLabel("PLZ", new() { Exact = true }).FillAsync("50667");
        await page.GetByLabel("Ort", new() { Exact = true }).FillAsync("Köln");
        await AddSetupUnitAsync(page, "Wohnung 1", "55,5");
        await AddSetupUnitAsync(page, "Wohnung 2");
        await next.ClickAsync();

        await Assertions.Expect(heading).ToHaveTextAsync("Buchhaltung");
        await next.ClickAsync();

        await Assertions.Expect(heading).ToHaveTextAsync("Zusammenfassung");
        await Assertions.Expect(count).ToContainTextAsync("Schritt 7 von 7");
        await Assertions.Expect(page.Locator(".wizard-card")).ToContainTextAsync("Objekt „Lindenweg 3“ mit 2 Wohnung(en)");
        await Shot(page, "04-summary");

        await page.GetByRole(AriaRole.Button, new() { Name = "Einrichtung abschließen" }).ClickAsync();
        await page.WaitForURLAsync("**/verwaltung");

        Assert.False(await App.WithScopeAsync(sp => Task.FromResult(sp.GetRequiredService<IModuleState>().IsEnabled(AppModule.Bank))));
        Assert.True(await App.WithScopeAsync(sp => Task.FromResult(sp.GetRequiredService<IModuleState>().IsEnabled(AppModule.Tickets))));
        Assert.Equal("Hausverwaltung Beispiel", (await App.WithScopeAsync(sp => sp.GetRequiredService<ILandlordProfileService>().GetAsync(TestContext.Current.CancellationToken)))?.Name);
        var properties = await App.WithScopeAsync(sp => sp.GetRequiredService<IPropertyService>().ListAsync(null, TestContext.Current.CancellationToken));
        var house = Assert.Single(properties);
        Assert.Equal("Lindenweg 3", house.Name);
        Assert.Equal(2, house.Units);
        Assert.Equal(55.5m, house.LivingArea);
    }

    private static async Task Shot(IPage page, string name)
    {
        if (Environment.GetEnvironmentVariable("ELOCARO_TOUR_DIR") is not { } directory)
        {
            return;
        }

        Directory.CreateDirectory(directory);
        var size = page.ViewportSize!.Width;
        if (size < 500)
        {
            await page.SetViewportSizeAsync(size, 1500); // phone: tall enough to see the buttons above the bottom bar
        }

        foreach (var scheme in new[] { ColorScheme.Light, ColorScheme.Dark })
        {
            await page.EmulateMediaAsync(new() { ColorScheme = scheme });
            await page.WaitForTimeoutAsync(400);
            await page.ScreenshotAsync(new() { Path = Path.Combine(directory, $"wizard-{name}-{size}-{scheme}.png"), FullPage = true });
        }

        await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Light });
        if (size < 500)
        {
            await page.SetViewportSizeAsync(size, 780);
        }
    }
}

/// <summary>The first start: the question "Wie vermieten Sie?" sets a starting point, "Später" is offered, and what exists is not asked again.</summary>
public class SetupWizardFirstRunBrowserTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    protected override bool SetupCompleted => false;

    [Fact]
    public async Task The_answer_about_the_kind_of_letting_sets_a_starting_point_and_can_be_changed()
    {
        var page = await SignedInAsAdminAsync(mobile: false);
        await page.GotoAsync(BaseUrl + "/verwaltung/einrichtung");
        await Assertions.Expect(page.Locator("[data-interactive=true]")).ToHaveCountAsync(1);
        var next = page.GetByRole(AriaRole.Button, new() { Name = "Weiter", Exact = true });

        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Später" })).ToBeVisibleAsync();
        await page.GetByLabel("Ich vermiete einzelne Zimmer (WG)").CheckAsync();
        await next.ClickAsync();
        await Assertions.Expect(page.GetByLabel("Tickets & Hausmeister")).Not.ToBeCheckedAsync();
        await Assertions.Expect(page.GetByLabel("Bank & Zahlungsabgleich")).ToBeCheckedAsync();

        await next.ClickAsync();
        await Assertions.Expect(page.Locator("#wizard-heading")).ToHaveTextAsync("Ihre Vermietung");
        await Assertions.Expect(page.GetByLabel("Zimmerweise Vermietung (WG)")).ToBeCheckedAsync();
        await Assertions.Expect(page.GetByLabel("Staffelmiete")).Not.ToBeCheckedAsync();
        await Assertions.Expect(page.GetByLabel("Vorauszahlung mit jährlicher Abrechnung")).ToBeCheckedAsync();

        // Going back and choosing another answer starts from that answer's defaults, and every module can still be changed.
        await page.GetByRole(AriaRole.Button, new() { Name = "Zurück" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Zurück" }).ClickAsync();
        await page.GetByLabel("Ich habe einen größeren Bestand oder einen Hausmeister").CheckAsync();
        await next.ClickAsync();
        await Assertions.Expect(page.GetByLabel("Tickets & Hausmeister")).ToBeCheckedAsync();
        await page.GetByLabel("Tickets & Hausmeister").UncheckAsync();
        await next.ClickAsync();
        await Assertions.Expect(page.GetByLabel("Staffelmiete")).ToBeCheckedAsync();
        await Assertions.Expect(page.GetByLabel("Zimmerweise Vermietung (WG)")).Not.ToBeCheckedAsync();
    }

    [Fact]
    public async Task Later_leaves_the_wizard_without_changing_anything_and_the_dashboard_shows_the_first_steps()
    {
        var page = await SignedInAsAdminAsync(mobile: false);
        await page.GotoAsync(BaseUrl + "/verwaltung/einrichtung");
        await Assertions.Expect(page.Locator("[data-interactive=true]")).ToHaveCountAsync(1);

        await page.GetByRole(AriaRole.Button, new() { Name = "Später" }).ClickAsync();
        await page.WaitForURLAsync("**/verwaltung");

        await Assertions.Expect(page.Locator("main")).ToContainTextAsync("So geht es los");
        await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Vermieterprofil anlegen");
        await Assertions.Expect(page.Locator("main")).Not.ToContainTextAsync("Alles erledigt");
    }

    [Fact]
    public async Task An_untouched_property_step_is_skipped_with_next_instead_of_asking_for_required_fields()
    {
        var page = await SignedInAsAdminAsync(mobile: false);
        await page.GotoAsync(BaseUrl + "/verwaltung/einrichtung");
        await Assertions.Expect(page.Locator("[data-interactive=true]")).ToHaveCountAsync(1);
        var next = page.GetByRole(AriaRole.Button, new() { Name = "Weiter", Exact = true });
        for (var i = 0; i < 3; i++)
        {
            await next.ClickAsync();
        }

        await page.GetByLabel("Name", new() { Exact = true }).FillAsync("Hausverwaltung Beispiel");
        await page.GetByLabel("Straße und Hausnummer", new() { Exact = true }).FillAsync("Weg 1");
        await page.GetByLabel("PLZ", new() { Exact = true }).FillAsync("50667");
        await page.GetByLabel("Ort", new() { Exact = true }).FillAsync("Köln");
        await next.ClickAsync();

        await Assertions.Expect(page.Locator("#wizard-heading")).ToHaveTextAsync("Erstes Objekt");
        await next.ClickAsync();

        await Assertions.Expect(page.Locator("#wizard-heading")).ToHaveTextAsync("Buchhaltung");
    }
}
