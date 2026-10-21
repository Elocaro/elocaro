using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MietHahn.Application.People;
using MietHahn.Domain.People;

namespace MietHahn.Web.Tests.Browser;

/// <summary>Pages with render mode Interactive Server (ADR 0005): the circuit connects and the page reacts without reloading.</summary>
public class InteractiveBrowserTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    [Fact]
    public async Task People_list_filters_live_while_typing()
    {
        await App.WithScopeAsync(async sp =>
        {
            var people = sp.GetRequiredService<IPersonService>();
            await people.CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt" }, null, TestContext.Current.CancellationToken);
            await people.CreateAsync(new PersonInput { FirstName = "Ben", LastName = "Weber" }, null, TestContext.Current.CancellationToken);
            return 0;
        });
        var page = await SignedInAsAdminAsync(mobile: false);

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await page.GotoAsync(BaseUrl + "/verwaltung/personen");
            await Assertions.Expect(page.Locator("tbody tr")).ToHaveCountAsync(2);
            await Assertions.Expect(page.Locator("[data-interactive=true]")).ToHaveCountAsync(1); // circuit connected

            // Typing alone (no submit, no reload) narrows the list as soon as the circuit is connected.
            await page.Locator("input[placeholder^='Name, E-Mail']").PressSequentiallyAsync("Weber");
            await ElapseDebounceAsync();
            await Assertions.Expect(page.Locator("tbody tr")).ToHaveCountAsync(1);
            await Assertions.Expect(page.Locator("tbody")).ToContainTextAsync("Weber, Ben");
        });
    }

    /// <summary>The app uses a fake clock in tests, so the debounce timer of the input only runs when the clock advances.</summary>
    private async Task ElapseDebounceAsync()
    {
        for (var i = 0; i < 10; i++)
        {
            App.Clock.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task People_list_fits_360_pixels()
    {
        var page = await SignedInAsAdminAsync();
        await page.GotoAsync(BaseUrl + "/verwaltung/personen");
        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Personen");
        await NoHorizontalScrollAsync(page);
    }

    [Fact]
    public async Task Person_is_created_in_a_dialog_without_leaving_the_list_and_audited_with_the_user()
    {
        var page = await SignedInAsAdminAsync(mobile: false);

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await page.GotoAsync(BaseUrl + "/verwaltung/personen");
            await Assertions.Expect(page.Locator("[data-interactive=true]")).ToHaveCountAsync(1);
            await Assertions.Expect(page.Locator("tbody")).ToContainTextAsync("Noch keine Personen.");

            await page.GetByRole(AriaRole.Button, new() { Name = "Person anlegen" }).ClickAsync();
            var dialog = page.Locator(".mud-dialog");
            await Assertions.Expect(dialog).ToBeVisibleAsync();

            // On a small window the actions stay reachable (content scrolls, buttons are pinned).
            await page.SetViewportSizeAsync(1280, 500);
            await Assertions.Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Person anlegen" })).ToBeInViewportAsync();
            await page.SetViewportSizeAsync(1280, 900);

            // Validation: the last name is required, nothing is saved.
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Person anlegen" }).ClickAsync();
            await Assertions.Expect(dialog).ToContainTextAsync("Bitte den Nachnamen bzw. Firmennamen angeben.");

            await dialog.GetByLabel("Vorname").FillAsync("Clara");
            await dialog.GetByLabel("Nachname bzw. Name").FillAsync("Neumann");
            await dialog.GetByLabel("E-Mail").FillAsync("clara@example.de");
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Person anlegen" }).ClickAsync();

            // Dialog closes, snackbar confirms, the list shows the person; no page change.
            await Assertions.Expect(dialog).ToHaveCountAsync(0);
            await ExpectSnackbarAsync(page, "Die Person wurde angelegt.");
            await Assertions.Expect(page.Locator("tbody")).ToContainTextAsync("Neumann, Clara");
            Assert.EndsWith("/verwaltung/personen", page.Url, StringComparison.Ordinal);
        });

        // The audit fields name the signed-in user, not "system" (there is no HttpContext inside a circuit).
        await App.WithDbAsync(async db =>
        {
            var person = await db.Set<Person>().SingleAsync(p => p.LastName == "Neumann", TestContext.Current.CancellationToken);
            Assert.Equal("admin@example.de", person.CreatedBy);
        });
    }

    [Fact]
    public async Task Person_is_edited_on_an_interactive_page_and_the_details_page_confirms()
    {
        var id = await App.WithScopeAsync(async sp =>
            (await sp.GetRequiredService<IPersonService>().CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt" }, null, TestContext.Current.CancellationToken)).Id!.Value);
        var page = await SignedInAsAdminAsync(mobile: false);

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await page.GotoAsync(BaseUrl + $"/verwaltung/personen/{id}");
            await Assertions.Expect(page.Locator("[data-interactive=true]")).ToHaveCountAsync(1);
            await page.GetByRole(AriaRole.Button, new() { Name = "Bearbeiten" }).ClickAsync();
            await page.GetByLabel("E-Mail").FillAsync("anna@example.de");
            await page.GetByRole(AriaRole.Button, new() { Name = "Speichern" }).ClickAsync();

            await ExpectSnackbarAsync(page, "Die Angaben wurden gespeichert.");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("anna@example.de");
        });

        await App.WithDbAsync(async db =>
        {
            var person = await db.Set<Person>().SingleAsync(p => p.Id == id, TestContext.Current.CancellationToken);
            Assert.Equal("admin@example.de", person.ModifiedBy);
        });
    }

    [Fact]
    public async Task Unknown_message_keys_in_the_url_show_nothing()
    {
        var id = await App.WithScopeAsync(async sp =>
            (await sp.GetRequiredService<IPersonService>().CreateAsync(new PersonInput { LastName = "Schmidt" }, null, TestContext.Current.CancellationToken)).Id!.Value);
        var page = await SignedInAsAdminAsync(mobile: false);

        await page.GotoAsync(BaseUrl + $"/verwaltung/personen/{id}?meldung=%3Cb%3Efrei%3C%2Fb%3E");
        await Assertions.Expect(page.Locator("[data-interactive=true]")).ToHaveCountAsync(1);

        await Assertions.Expect(page.Locator(".mud-snackbar")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("main")).Not.ToContainTextAsync("frei");
    }
}
