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
    public async Task Forms_work_with_enhanced_forms_in_the_browser()
    {
        var page = await SignedInAsAdminAsync();
        await page.GotoAsync(BaseUrl + "/verwaltung/benutzer");

        await page.FillAsync("#name", "Erika Mustermann");
        await page.FillAsync("#email", "erika@example.de");
        await page.SelectOptionAsync("#role", "Tenant");
        await page.ClickAsync("text=Einladung senden");

        await page.WaitForSelectorAsync(".message-success:has-text('erika@example.de')");
        Assert.Contains("Erika Mustermann", await page.TextContentAsync("table.data-table"), StringComparison.Ordinal);
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
            await page.GotoAsync(BaseUrl + "/verwaltung/benutzer");
            await page.FillAsync("#name", "Mieter Test");
            await page.FillAsync("#email", "m@example.de");
            await page.ClickAsync("text=Einladung senden");
            await page.WaitForSelectorAsync(".message-success:has-text('m@example.de')");

            await page.ClickAsync("table.data-table a:text-is('Mieter Test')");
            await page.WaitForSelectorAsync("h1:text-is('Mieter Test')");

            Assert.False(await page.IsVisibleAsync("text=Ja, Konto sperren"));
            await page.ClickAsync("summary:has-text('Konto sperren')");
            await page.ClickAsync("button:has-text('Ja, Konto sperren')");

            await page.WaitForSelectorAsync(".message-success:has-text('Das Konto wurde gesperrt.')");
        });
    }
}
