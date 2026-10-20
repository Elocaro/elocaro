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
}
