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

    private readonly List<IBrowserContext> contexts = [];

    protected MietHahnWebFactory App { get; private set; } = null!;

    /// <summary>Additional configuration for the app under test.</summary>
    protected virtual IDictionary<string, string?>? Configuration => null;

    /// <summary>Replaces services of the app under test (e.g. the PDF generator).</summary>
    protected virtual void ConfigureServices(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
    {
    }

    /// <summary>Whether the setup wizard counts as done; false lets a test walk through the first start.</summary>
    protected virtual bool SetupCompleted => true;

    protected IBrowser Browser { get; private set; } = null!;

    protected string BaseUrl { get; private set; } = "";

    public async ValueTask InitializeAsync()
    {
        App = new MietHahnWebFactory(await postgres.CreateMigratedDatabaseAsync(TestContext.Current.CancellationToken), Configuration, ConfigureServices, postgres);
        App.UseKestrel(0);
        App.StartServer();
        if (SetupCompleted)
        {
            await App.CompleteSetupAsync();
        }

        BaseUrl = App.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First().Replace("127.0.0.1", "localhost", StringComparison.Ordinal);

        // Many browser tests run in parallel, each with its own app and database; allow for the load.
        Assertions.SetDefaultExpectTimeout(15_000);
        try
        {
            Browser = await SharedBrowserAsync();
        }
        catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist", StringComparison.Ordinal))
        {
            Assert.Skip("Kein Chromium für Playwright installiert: " + ex.Message.Split((char)10)[0]);
        }
    }

    public async ValueTask DisposeAsync()
    {
        // The browser is shared by all tests of the run (starting Chromium per test is slow); only this test's contexts end here.
        foreach (var context in contexts)
        {
            await context.CloseAsync();
        }

        await App.DisposeAsync();
    }

    private static readonly SemaphoreSlim BrowserLock = new(1, 1);
    private static IBrowser? sharedBrowser;

    /// <summary>One Chromium for the whole test run; every test works in its own context (cookies and storage are isolated).</summary>
    private static async Task<IBrowser> SharedBrowserAsync()
    {
        await BrowserLock.WaitAsync();
        try
        {
            if (sharedBrowser is not { IsConnected: true })
            {
                var playwright = await Playwright.CreateAsync();
                // MIETHAHN_HEADED=1 shows the browser window (for watching a test while developing).
                sharedBrowser = await playwright.Chromium.LaunchAsync(new() { Headless = Environment.GetEnvironmentVariable("MIETHAHN_HEADED") is null });
            }

            return sharedBrowser;
        }
        finally
        {
            BrowserLock.Release();
        }
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
        contexts.Add(context);
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
        // Before the setup wizard was completed the sign-in leads to the wizard.
        await page.WaitForURLAsync(SetupCompleted ? "**/verwaltung" : "**/verwaltung/einrichtung");
        return page;
    }

    /// <summary>
    /// Chooses a file in the page's file upload. The file input only reacts once the interactive connection is fully up, so the file is
    /// offered again until the page shows its name.
    /// </summary>
    protected static async Task UploadFileAsync(IPage page, string name, byte[] content, string mimeType = "text/csv")
    {
        ArgumentNullException.ThrowIfNull(page);
        await Assertions.Expect(page.Locator("[data-interactive=true]")).ToHaveCountAsync(1);
        for (var attempt = 0; attempt < 10 && await page.Locator(".file-upload-name").CountAsync() == 0; attempt++)
        {
            await page.Locator("input[type=file]").SetInputFilesAsync(new FilePayload { Name = name, MimeType = mimeType, Buffer = content });
            await page.WaitForTimeoutAsync(500);
        }

        await Assertions.Expect(page.Locator(".file-upload-name")).ToHaveTextAsync(name);
    }

    /// <summary>A snackbar with this text is visible (the animation may briefly show two).</summary>
    protected static Task ExpectSnackbarAsync(IPage page, string text)
    {
        ArgumentNullException.ThrowIfNull(page);
        return Assertions.Expect(page.Locator(".mud-snackbar", new() { HasText = text }).First).ToBeVisibleAsync();
    }

    /// <summary>Switches to a tab of a detail page (section tabs).</summary>
    protected static Task OpenTabAsync(IPage page, string name)
    {
        ArgumentNullException.ThrowIfNull(page);
        return page.GetByRole(AriaRole.Tab, new() { Name = name }).ClickAsync();
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

    /// <summary>Adds a flat in the setup wizard: opens the dialog, fills it, confirms.</summary>
    protected static async Task AddSetupUnitAsync(IPage page, string name, string? area = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        await page.GetByRole(AriaRole.Button, new() { Name = "Wohnung hinzufügen" }).ClickAsync();
        var dialog = page.Locator(".mud-dialog");
        await dialog.GetByLabel("Bezeichnung", new() { Exact = true }).FillAsync(name);
        if (area is not null)
        {
            await dialog.GetByLabel("Wohnfläche").FillAsync(area);
        }

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Hinzufügen" }).ClickAsync();
        await Assertions.Expect(dialog).ToHaveCountAsync(0);
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
        var dimensions = await page.EvaluateAsync<int[]>("() => { const c = document.getElementById('content'); return [Math.max(document.documentElement.scrollWidth, c ? c.scrollWidth : 0), window.innerWidth]; }");
        Assert.True(dimensions[0] <= dimensions[1], $"{page.Url}: Inhalt ist {dimensions[0]}px breit, Fenster {dimensions[1]}px.");
    }
}
