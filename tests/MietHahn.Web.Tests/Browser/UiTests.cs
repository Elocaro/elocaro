using Microsoft.Playwright;

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
        Assert.False(await page.IsVisibleAsync(".mobile-navigation > summary"));
        Assert.Equal("Benutzer", await page.TextContentAsync(".sidebar a.active"));
    }

    [Fact]
    public async Task Interactive_form_invites_a_user_and_updates_the_list()
    {
        var page = await SignedInAsAdminAsync();
        await GotoInteractiveAsync(page, "/verwaltung/benutzer");

        await page.GetByLabel("Name", new() { Exact = true }).FillAsync("Erika Mustermann");
        await page.GetByLabel("E-Mail-Adresse").FillAsync("erika@example.de");
        await SelectAsync(page, "Rolle", "Mieter");
        await page.GetByRole(AriaRole.Button, new() { Name = "Einladung senden" }).ClickAsync();

        await ExpectSnackbarAsync(page, "erika@example.de");
        await Assertions.Expect(page.Locator("tbody")).ToContainTextAsync("Erika Mustermann");
    }

    [Fact]
    public async Task User_menu_offers_account_and_sign_out()
    {
        var page = await SignedInAsAdminAsync();

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
