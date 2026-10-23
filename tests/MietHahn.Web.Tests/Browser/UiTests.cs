using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MietHahn.Application.People;

namespace MietHahn.Web.Tests.Browser;

public class UiTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    [Fact]
    public async Task Pages_are_usable_at_360_pixels_without_horizontal_scrolling()
    {
        var page = await SignedInAsAdminAsync();

        foreach (var path in new[] { "/verwaltung", "/verwaltung/benutzer", "/verwaltung/system/mails", "/verwaltung/system/jobs", "/verwaltung/system/datensicherung", "/verwaltung/vpi", "/konto/sicherheit" })
        {
            await page.GotoAsync(BaseUrl + path);
            await NoHorizontalScrollAsync(page);
        }

        var anonymous = await NewPageAsync();
        foreach (var path in new[] { "/konto/anmelden", "/konto/link", "/konto/passwort-vergessen" })
        {
            await anonymous.GotoAsync(BaseUrl + path);
            await NoHorizontalScrollAsync(anonymous);
        }
    }

    [Fact]
    public async Task Mobile_opens_the_menu_without_JavaScript_and_navigates()
    {
        var page = await SignedInAsAdminAsync();

        Assert.False(await page.IsVisibleAsync(".sidebar"));
        Assert.False(await page.IsVisibleAsync(".top-bar"));
        Assert.True(await page.IsVisibleAsync(".bottom-bar"));
        Assert.False(await page.IsVisibleAsync(".mobile-navigation .navigation"));

        await page.ClickAsync(".mobile-navigation > summary");
        Assert.True(await page.IsVisibleAsync(".mobile-navigation .navigation"));

        await page.ClickAsync(".mobile-navigation >> text=Benutzer");
        await page.WaitForURLAsync("**/verwaltung/benutzer");
        await page.WaitForSelectorAsync("h1:text-is('Benutzer')");
    }

    [Fact]
    public async Task Desktop_shows_the_sidebar_with_highlighted_page()
    {
        var page = await SignedInAsAdminAsync(mobile: false);
        await page.GotoAsync(BaseUrl + "/verwaltung/benutzer");

        Assert.True(await page.IsVisibleAsync(".sidebar"));
        Assert.False(await page.IsVisibleAsync(".bottom-bar"));
        Assert.Equal("Benutzer", await page.TextContentAsync(".sidebar a.active"));
    }

    [Fact]
    public async Task Interactive_form_invites_a_user_and_updates_the_list()
    {
        var page = await SignedInAsAdminAsync();
        await GotoInteractiveAsync(page, "/verwaltung/benutzer");

        await page.GetByRole(AriaRole.Button, new() { Name = "Person einladen" }).ClickAsync();
        await page.GetByLabel("Name", new() { Exact = true }).FillAsync("Erika Mustermann");
        await page.GetByLabel("E-Mail-Adresse").FillAsync("erika@example.de");
        await SelectAsync(page, "Rolle", "Mieter");
        await page.GetByRole(AriaRole.Button, new() { Name = "Einladung senden" }).ClickAsync();

        await ExpectSnackbarAsync(page, "erika@example.de");
        await Assertions.Expect(page.Locator("tbody")).ToContainTextAsync("Erika Mustermann");
    }

    [Fact]
    public async Task Quick_search_finds_pages_and_records_with_the_keyboard()
    {
        await App.WithScopeAsync(async sp =>
            await sp.GetRequiredService<IPersonService>().CreateAsync(new PersonInput { FirstName = "Erika", LastName = "Musterfrau" }, null, TestContext.Current.CancellationToken));
        var page = await SignedInAsAdminAsync(mobile: false);
        await GotoInteractiveAsync(page, "/verwaltung/benutzer");

        await page.Keyboard.PressAsync("Control+k");
        var input = page.GetByLabel("Suchbegriff");
        await Assertions.Expect(input).ToBeFocusedAsync();
        await input.PressSequentiallyAsync("Musterfr");
        await Assertions.Expect(page.Locator(".quick-search-results")).ToContainTextAsync("Erika Musterfrau");

        await page.Keyboard.PressAsync("Enter");
        await page.WaitForURLAsync("**/verwaltung/personen/*");
        await page.WaitForSelectorAsync("h1:text-is('Erika Musterfrau')");
        await Assertions.Expect(page.Locator(".quick-search")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Quick_search_closes_with_escape_and_backdrop_and_survives_fast_typing()
    {
        await App.WithScopeAsync(async sp =>
        {
            var people = sp.GetRequiredService<IPersonService>();
            for (var i = 1; i <= 12; i++)
            {
                await people.CreateAsync(new PersonInput { FirstName = "Erika" + i, LastName = "Musterfrau" }, null, TestContext.Current.CancellationToken);
            }

            return true;
        });
        var page = await SignedInAsAdminAsync(mobile: false);
        await GotoInteractiveAsync(page, "/verwaltung/benutzer");

        // Keystrokes around the debounce time: searches overlap with the next input and with the page's own loading.
        await page.Keyboard.PressAsync("Control+k");
        var input = page.GetByLabel("Suchbegriff");
        await input.PressSequentiallyAsync("Musterfrau", new() { Delay = 190 });
        await Assertions.Expect(page.Locator(".quick-search-results")).ToContainTextAsync("Erika1");

        await input.PressSequentiallyAsync(" gibtesnicht", new() { Delay = 190 });
        await Assertions.Expect(page.Locator(".quick-search-empty")).ToContainTextAsync("Keine Treffer");
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator(".quick-search")).ToHaveCountAsync(0);

        await page.Keyboard.PressAsync("Control+k");
        await Assertions.Expect(input).ToBeFocusedAsync();
        await page.Locator(".quick-search-backdrop").ClickAsync(new() { Position = new() { X = 5, Y = 5 } });
        await Assertions.Expect(page.Locator(".quick-search")).ToHaveCountAsync(0);

        // The connection is still alive: the search works once more.
        await page.Keyboard.PressAsync("Control+k");
        await page.GetByLabel("Suchbegriff").PressSequentiallyAsync("Musterfr");
        await Assertions.Expect(page.Locator(".quick-search-results")).ToContainTextAsync("Musterfrau");
    }

    [Theory]
    [InlineData("/verwaltung", "Was ist zu tun?")]
    [InlineData("/verwaltung/personen", "Personen")]
    public async Task Info_button_opens_a_dialog_that_closes_with_button_escape_and_backdrop(string path, string title)
    {
        var page = await SignedInAsAdminAsync(mobile: false); // ends on the (static) start page
        if (path != "/verwaltung")
        {
            await GotoInteractiveAsync(page, path);
        }

        var dialog = page.Locator("dialog.info-dialog");
        var info = page.GetByRole(AriaRole.Button, new() { Name = "Info zu dieser Seite" });

        await info.ClickAsync();
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        await Assertions.Expect(dialog.Locator("h2")).ToHaveTextAsync(title);
        await Assertions.Expect(dialog).ToContainTextAsync("keine Rechts- oder Steuerberatung");
        if (Environment.GetEnvironmentVariable("MIETHAHN_TOUR_DIR") is { } directory)
        {
            Directory.CreateDirectory(directory);
            await page.ScreenshotAsync(new() { Path = Path.Combine(directory, "info-" + path.Trim('/').Replace('/', '_') + ".png") });
        }

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Schließen" }).ClickAsync();
        await Assertions.Expect(dialog).ToBeHiddenAsync();

        await info.ClickAsync();
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(dialog).ToBeHiddenAsync();

        await info.ClickAsync();
        await page.Mouse.ClickAsync(5, 5);
        await Assertions.Expect(dialog).ToBeHiddenAsync();
    }

    [Fact]
    public async Task User_menu_offers_account_and_sign_out()
    {
        var page = await SignedInAsAdminAsync(mobile: false);

        await page.ClickAsync(".user-menu > summary");
        Assert.Equal("Andy Hahn", (await page.TextContentAsync(".user-name"))!.Trim());
        await page.ClickAsync(".user-menu >> text=Abmelden");

        await page.WaitForURLAsync("**/konto/anmelden?abgemeldet=true");
        Assert.Contains("Sie wurden abgemeldet.", await page.TextContentAsync("body"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Locking_requires_confirmation()
    {
        var page = await SignedInAsAdminAsync();
        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await GotoInteractiveAsync(page, "/verwaltung/benutzer");
            await page.GetByRole(AriaRole.Button, new() { Name = "Person einladen" }).ClickAsync();
            await page.GetByLabel("Name", new() { Exact = true }).FillAsync("Mieter Test");
            await page.GetByLabel("E-Mail-Adresse").FillAsync("m@example.de");
            await page.GetByRole(AriaRole.Button, new() { Name = "Einladung senden" }).ClickAsync();
            await ExpectSnackbarAsync(page, "m@example.de");

            await page.GetByRole(AriaRole.Link, new() { Name = "Mieter Test" }).ClickAsync();
            await page.WaitForSelectorAsync("h1:text-is('Mieter Test')");
            await Assertions.Expect(page.Locator("[data-interactive=true]")).ToHaveCountAsync(1);

            await page.GetByRole(AriaRole.Button, new() { Name = "Konto sperren" }).ClickAsync();
            var box = page.Locator(".mud-message-box");
            await box.GetByRole(AriaRole.Button, new() { Name = "Abbrechen" }).ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Konto entsperren" })).ToHaveCountAsync(0);

            await page.GetByRole(AriaRole.Button, new() { Name = "Konto sperren" }).ClickAsync();
            await box.GetByRole(AriaRole.Button, new() { Name = "Ja, Konto sperren" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Das Konto wurde gesperrt.");
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Konto entsperren" })).ToBeVisibleAsync();
        });
    }
}
