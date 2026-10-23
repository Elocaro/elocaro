using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MietHahn.Application.Admin;

namespace MietHahn.Web.Tests.Browser;

/// <summary>
/// The setup wizard as a user operates it: step by step with progress, back, and finish. With <c>MIETHAHN_TOUR_DIR</c> set the test also
/// saves screenshots of the steps (light and dark), to look at the design.
/// </summary>
public class SetupWizardBrowserTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Wizard_leads_through_the_steps_with_progress_and_saves_at_the_end(bool mobile)
    {
        var page = await SignedInAsAdminAsync(mobile);
        await page.GotoAsync(BaseUrl + "/verwaltung/einrichtung");
        await Assertions.Expect(page.Locator("[data-interactive=true]")).ToHaveCountAsync(1);

        var heading = page.Locator("#wizard-heading");
        var count = page.Locator(".wizard-count");
        await Assertions.Expect(heading).ToHaveTextAsync("Willkommen");
        await Assertions.Expect(count).ToContainTextAsync("Schritt 1 von");
        await Shot(page, "01-welcome");

        await page.GetByRole(AriaRole.Button, new() { Name = "Weiter" }).ClickAsync();
        await Assertions.Expect(count).ToContainTextAsync("Schritt 2 von");
        await Assertions.Expect(heading).ToHaveTextAsync("Bank & Zahlungsabgleich");
        await Shot(page, "02-first-module");

        await page.GetByRole(AriaRole.Button, new() { Name = "Zurück" }).ClickAsync();
        await Assertions.Expect(heading).ToHaveTextAsync("Willkommen");

        // Turn off the first module, then walk to the end.
        await page.GetByRole(AriaRole.Button, new() { Name = "Weiter" }).ClickAsync();
        await page.GetByLabel("Nein, nicht benötigt").CheckAsync();
        var steps = 2;
        var finish = page.GetByRole(AriaRole.Button, new() { Name = "Einrichtung abschließen" });
        while (!await finish.IsVisibleAsync())
        {
            await page.GetByRole(AriaRole.Button, new() { Name = "Weiter" }).ClickAsync();
            steps++;
            await Assertions.Expect(count).ToContainTextAsync($"Schritt {steps} von");
            if (steps == 7)
            {
                await Shot(page, "03-middle");
            }
        }

        await Assertions.Expect(heading).ToHaveTextAsync("Zusammenfassung");
        await Assertions.Expect(page.Locator(".wizard-progress")).ToHaveAttributeAsync("value", steps.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await Shot(page, "04-summary");

        await finish.ClickAsync();
        await page.WaitForURLAsync("**/verwaltung");
        Assert.False(await App.WithScopeAsync(sp => Task.FromResult(sp.GetRequiredService<IModuleState>().IsEnabled(AppModule.Bank))));
        Assert.True(await App.WithScopeAsync(sp => Task.FromResult(sp.GetRequiredService<IModuleState>().IsEnabled(AppModule.Tickets))));
    }

    private static async Task Shot(IPage page, string name)
    {
        if (Environment.GetEnvironmentVariable("MIETHAHN_TOUR_DIR") is not { } directory)
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
