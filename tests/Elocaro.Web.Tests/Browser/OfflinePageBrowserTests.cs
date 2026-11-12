using Microsoft.Playwright;

namespace Elocaro.Web.Tests.Browser;

/// <summary>The page that the installed web app shows without a connection (served by the service worker).</summary>
public class OfflinePageBrowserTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    [Fact]
    public async Task The_offline_page_is_self_contained_and_returns_to_the_app_when_the_server_answers()
    {
        var page = await NewPageAsync(mobile: false);

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            // Without the script: the page as it stands while the server is away
            await page.RouteAsync("**/offline.js", route => route.AbortAsync());
            await page.GotoAsync(BaseUrl + "/offline.html");
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Keine Verbindung" })).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Elocaro kann den Server gerade nicht erreichen");
            await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Jetzt erneut versuchen" })).ToBeVisibleAsync();

            // Nothing is loaded from the stylesheet of the app (there is no connection to fetch it): everything is inline
            Assert.Equal(0, await page.Locator("link[rel=stylesheet]").CountAsync());

            // With the script, "try again" checks the server and leaves for the app as soon as it answers
            await page.UnrouteAsync("**/offline.js");
            await page.GotoAsync(BaseUrl + "/offline.html");
            await page.GetByRole(AriaRole.Link, new() { Name = "Jetzt erneut versuchen" }).ClickAsync();
            await page.WaitForURLAsync(url => !url.Contains("offline.html", StringComparison.Ordinal), new() { Timeout = 15_000 });
        });
    }
}
