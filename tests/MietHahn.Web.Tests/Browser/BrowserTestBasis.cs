using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MietHahn.Application.Konten;
using MietHahn.Infrastructure.Identitaet;

namespace MietHahn.Web.Tests.Browser;

/// <summary>
/// Echte Browser-Tests (Chromium über Playwright) gegen die per Kestrel gestartete App.
/// Ohne installierten Browser werden die Tests übersprungen (lokal: PLAYWRIGHT_BROWSERS_PATH, CI: playwright install).
/// </summary>
[Trait("Kategorie", "Browser")]
public abstract class BrowserTestBasis(PostgresFixture postgres) : IAsyncLifetime
{
    protected const string Passwort = "Browser-Test-Passwort-2026";

    private IPlaywright? playwright;

    protected MietHahnWebFactory App { get; private set; } = null!;

    protected IBrowser Browser { get; private set; } = null!;

    protected string BasisUrl { get; private set; } = "";

    public async ValueTask InitializeAsync()
    {
        App = new MietHahnWebFactory(await postgres.NeueDatenbankAsync(TestContext.Current.CancellationToken));
        App.UseKestrel(0);
        App.StartServer();
        BasisUrl = App.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First().Replace("127.0.0.1", "localhost", StringComparison.Ordinal);

        playwright = await Playwright.CreateAsync();
        try
        {
            Browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
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

    /// <summary>Neue Seite in Handy-Größe (360 × 780) oder Desktop.</summary>
    protected async Task<IPage> NeueSeiteAsync(bool mobil = true)
    {
        var kontext = await Browser.NewContextAsync(new()
        {
            ViewportSize = mobil ? new() { Width = 360, Height = 780 } : new() { Width = 1280, Height = 900 },
            Locale = "de-DE",
            TimezoneId = "Europe/Berlin",
        });
        return await kontext.NewPageAsync();
    }

    /// <summary>Legt einen Admin mit aktiver 2FA an und meldet ihn über die Oberfläche an.</summary>
    protected async Task<IPage> AlsAdminAngemeldetAsync(bool mobil = true)
    {
        const string email = "admin@example.de";
        var schluessel = await App.MitScopeAsync(async sp =>
        {
            var manager = sp.GetRequiredService<UserManager<Benutzer>>();
            var benutzer = new Benutzer { UserName = email, Email = email, EmailConfirmed = true, Anzeigename = "Andy Hahn" };
            await manager.CreateAsync(benutzer, Passwort);
            await manager.AddToRoleAsync(benutzer, Rollen.Admin);
            await manager.ResetAuthenticatorKeyAsync(benutzer);
            await manager.SetTwoFactorEnabledAsync(benutzer, true);
            return await manager.GetAuthenticatorKeyAsync(benutzer);
        });

        var seite = await NeueSeiteAsync(mobil);
        await seite.GotoAsync(BasisUrl + "/konto/anmelden");
        await seite.FillAsync("#email", email);
        await seite.FillAsync("#passwort", Passwort);
        await seite.ClickAsync("button[type=submit]");
        await seite.WaitForURLAsync("**/konto/anmelden/zwei-faktor**");
        await seite.FillAsync("#code", Totp.Code(schluessel!, DateTimeOffset.UtcNow));
        await seite.ClickAsync("button[type=submit]");
        await seite.WaitForURLAsync("**/verwaltung");
        return seite;
    }

    /// <summary>Speichert bei einem Fehlschlag einen Screenshot (Pfad in der Fehlermeldung) zur Diagnose.</summary>
    protected static async Task MitScreenshotBeiFehlerAsync(IPage seite, Func<Task> schritte)
    {
        ArgumentNullException.ThrowIfNull(seite);
        ArgumentNullException.ThrowIfNull(schritte);
        try
        {
            await schritte();
        }
        catch (Exception ex) when (ex is TimeoutException or PlaywrightException)
        {
            var pfad = Path.Combine(Path.GetTempPath(), $"miethahn-browsertest-{Guid.NewGuid():N}.png");
            await seite.ScreenshotAsync(new() { Path = pfad, FullPage = true });
            throw new InvalidOperationException($"Browser-Schritt fehlgeschlagen auf {seite.Url}. Screenshot: {pfad}", ex);
        }
    }

    /// <summary>Breite des Dokuments ≤ Breite des Fensters, also kein horizontales Scrollen.</summary>
    protected static async Task KeinHorizontalesScrollenAsync(IPage seite)
    {
        var masse = await seite.EvaluateAsync<int[]>("() => [document.documentElement.scrollWidth, window.innerWidth]");
        Assert.True(masse[0] <= masse[1], $"{seite.Url}: Inhalt ist {masse[0]}px breit, Fenster {masse[1]}px.");
    }
}
