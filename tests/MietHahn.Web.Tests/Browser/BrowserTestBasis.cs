using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MietHahn.Application.Accounts;
using MietHahn.Infrastructure.Identity;

namespace MietHahn.Web.Tests.Browser;

/// <summary>
/// Real browser tests (Chromium via Playwright) against the app started via Kestrel.
/// Without an installed browser the tests are skipped (locally: PLAYWRIGHT_BROWSERS_PATH, CI: playwright install).
/// </summary>
[Trait("Kategorie", "Browser")]
public abstract class BrowserTestBasis(PostgresFixture postgres) : IAsyncLifetime
{
    protected const string Password = "Browser-Test-Passwort-2026";

    private IPlaywright? playwright;

    protected MietHahnWebFactory App { get; private set; } = null!;

    /// <summary>Additional configuration for the app under test.</summary>
    protected virtual IDictionary<string, string?>? Configuration => null;

    /// <summary>Replaces services of the app under test (e.g. the PDF generator).</summary>
    protected virtual void ConfigureServices(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
    {
    }

    protected IBrowser Browser { get; private set; } = null!;

    protected string BaseUrl { get; private set; } = "";

    public async ValueTask InitializeAsync()
    {
        App = new MietHahnWebFactory(await postgres.CreateDatabaseAsync(TestContext.Current.CancellationToken), Configuration, ConfigureServices);
        App.UseKestrel(0);
        App.StartServer();
        BaseUrl = App.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First().Replace("127.0.0.1", "localhost", StringComparison.Ordinal);

        // Many browser tests run in parallel, each with its own app and database; allow for the load.
        Assertions.SetDefaultExpectTimeout(15_000);
        playwright = await Playwright.CreateAsync();
        try
        {
            // MIETHAHN_HEADED=1 shows the browser window (for watching a test while developing).
            Browser = await playwright.Chromium.LaunchAsync(new() { Headless = Environment.GetEnvironmentVariable("MIETHAHN_HEADED") is null });
        }
        catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist", StringComparison.Ordinal))
        {
            Assert.Skip("Kein Chromium für Playwright installiert: " + ex.Message.Split('\n')[0]);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.DisposeAsync();
        }

        playwright?.Dispose();
        await App.DisposeAsync();
    }

    /// <summary>New page at phone size (360 × 780) or desktop.</summary>
    protected async Task<IPage> NewPageAsync(bool mobile = true)
    {
        var context = await Browser.NewContextAsync(new()
        {
            ViewportSize = mobile ? new() { Width = 360, Height = 780 } : new() { Width = 1280, Height = 900 },
            Locale = "de-DE",
            TimezoneId = "Europe/Berlin",
        });
        return await context.NewPageAsync();
    }

    /// <summary>Creates an admin with active 2FA and signs in via the UI.</summary>
    protected async Task<IPage> SignedInAsAdminAsync(bool mobile = true)
    {
        const string email = "admin@example.de";
        var key = await App.WithScopeAsync(async sp =>
        {
            var manager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = "Andy Hahn" };
            await manager.CreateAsync(user, Password);
            await manager.AddToRoleAsync(user, Roles.Admin);
            await manager.ResetAuthenticatorKeyAsync(user);
            await manager.SetTwoFactorEnabledAsync(user, true);
            return await manager.GetAuthenticatorKeyAsync(user);
        });

        var page = await NewPageAsync(mobile);
        await page.GotoAsync(BaseUrl + "/konto/anmelden");
        await page.FillAsync("#email", email);
        await page.FillAsync("#password", Password);
        await page.ClickAsync("button[type=submit]");
        await page.WaitForURLAsync("**/konto/anmelden/zwei-faktor**");
        await page.FillAsync("#code", Totp.Code(key!, DateTimeOffset.UtcNow));
        await page.ClickAsync("button[type=submit]");
        await page.WaitForURLAsync("**/verwaltung");
        return page;
    }

    /// <summary>A snackbar with this text is visible (the animation may briefly show two).</summary>
    protected static Task ExpectSnackbarAsync(IPage page, string text)
    {
        ArgumentNullException.ThrowIfNull(page);
        return Assertions.Expect(page.Locator(".mud-snackbar", new() { HasText = text }).First).ToBeVisibleAsync();
    }

    /// <summary>Opens a page and waits until it is interactive (before that, the prerendered page ignores input).</summary>
    protected async Task GotoInteractiveAsync(IPage page, string path)
    {
        ArgumentNullException.ThrowIfNull(page);
        await page.GotoAsync(BaseUrl + path);
        await Assertions.Expect(page.Locator("[data-interactive=true]")).ToHaveCountAsync(1);
    }

    /// <summary>Chooses an entry in a selection field of an interactive page (opens the list, clicks the entry).</summary>
    protected static async Task SelectAsync(IPage page, string label, string option, ILocator? scope = null, bool exact = true)
    {
        ArgumentNullException.ThrowIfNull(page);
        // The labelled input of a MudSelect is hidden; the visible control around it takes the click.
        var control = (scope ?? page.Locator("body")).Locator(".mud-select", new() { Has = page.Locator($"[aria-label='{label}']") }).First;
        await control.Locator(".mud-input").First.ClickAsync();
        var entry = exact
            ? new PageLocatorOptions { HasTextRegex = new System.Text.RegularExpressions.Regex("^\\s*" + System.Text.RegularExpressions.Regex.Escape(option) + "\\s*$") }
            : new PageLocatorOptions { HasText = option };
        await page.Locator(".mud-popover-open .mud-list-item", entry).First.ClickAsync();
    }

    /// <summary>Saves a screenshot on failure (path in the error message) for diagnosis.</summary>
    protected static async Task WithScreenshotOnFailureAsync(IPage page, Func<Task> steps)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(steps);
        try
        {
            await steps();
        }
        catch (Exception ex) when (ex is TimeoutException or PlaywrightException)
        {
            var path = Path.Combine(Path.GetTempPath(), $"miethahn-browsertest-{Guid.NewGuid():N}.png");
            await page.ScreenshotAsync(new() { Path = path, FullPage = true });
            throw new InvalidOperationException($"Browser-Schritt fehlgeschlagen auf {page.Url}. Screenshot: {path}", ex);
        }
    }

    /// <summary>Document width ≤ window width, i.e. no horizontal scrolling.</summary>
    protected static async Task NoHorizontalScrollAsync(IPage page)
    {
        var dimensions = await page.EvaluateAsync<int[]>("() => [document.documentElement.scrollWidth, window.innerWidth]");
        Assert.True(dimensions[0] <= dimensions[1], $"{page.Url}: Inhalt ist {dimensions[0]}px breit, Fenster {dimensions[1]}px.");
    }
}
