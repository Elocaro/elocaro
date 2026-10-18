using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Konten;
using MietHahn.Domain.Kommunikation;
using MietHahn.Infrastructure.Identitaet;

namespace MietHahn.Web.Tests.Konto;

/// <summary>Je Test eine frische App mit eigener Datenbank und Hilfen für typische Anmeldeschritte.</summary>
public abstract partial class KontoTestBasis(PostgresFixture postgres) : IAsyncLifetime
{
    protected const string AdminEmail = "vermieter@example.de";
    protected const string AdminPasswort = "Sehr-sicheres-Passwort-2026";

    protected MietHahnWebFactory App { get; private set; } = null!;

    protected virtual IDictionary<string, string?>? Konfiguration => null;

    protected virtual void DiensteAnpassen(IServiceCollection dienste)
    {
    }

    public async ValueTask InitializeAsync() => App = await MietHahnWebFactory.MitDatenbankAsync(postgres, Konfiguration, DiensteAnpassen);

    /// <summary>
    /// Legt ein Konto der Rolle direkt an (mit Passwort, bei 2FA-Pflicht mit aktivierter 2FA) und meldet es über
    /// die Oberfläche an. Liefert Browser und Benutzer-Id.
    /// </summary>
    protected async Task<(TestBrowser Browser, Guid Id)> AngemeldetAlsAsync(string rolle, IReadOnlyCollection<Recht>? rechte = null, string? email = null)
    {
        email ??= $"{rolle.ToLowerInvariant()}-{Guid.NewGuid():N}@example.de";
        const string passwort = "Test-Passwort-2026-lang";
        var (id, schluessel) = await App.MitScopeAsync(async sp =>
        {
            var manager = sp.GetRequiredService<UserManager<Benutzer>>();
            var benutzer = new Benutzer { UserName = email, Email = email, EmailConfirmed = true, Anzeigename = rolle + " Test" };
            Assert.True((await manager.CreateAsync(benutzer, passwort)).Succeeded);
            await manager.AddToRoleAsync(benutzer, rolle);
            if (rolle == Rollen.Verwalter)
            {
                await manager.AddClaimsAsync(benutzer, (rechte ?? Rechte.VerwalterStandard).Select(r => new System.Security.Claims.Claim(Rechte.ClaimTyp, r.ToString())));
            }

            string? key = null;
            if (Rollen.MitZweiFaktorPflicht.Contains(rolle))
            {
                await manager.ResetAuthenticatorKeyAsync(benutzer);
                key = await manager.GetAuthenticatorKeyAsync(benutzer);
                await manager.SetTwoFactorEnabledAsync(benutzer, true);
            }

            return (benutzer.Id, key);
        });

        var browser = App.Browser();
        var seite = await AnmeldenAsync(browser, email, passwort);
        if (schluessel is not null)
        {
            seite = await browser.AbsendenAsync(seite, "zwei-faktor", new Dictionary<string, string> { ["Eingabe.Code"] = Code(schluessel) });
        }

        Assert.DoesNotContain("/konto/", seite.Pfad, StringComparison.Ordinal);
        return (browser, id);
    }

    public async ValueTask DisposeAsync() => await App.DisposeAsync();

    protected static Task<Seite> EinrichtenAsync(TestBrowser browser) => EinrichtenAsync(browser, AdminEmail, AdminPasswort);

    protected static async Task<Seite> EinrichtenAsync(TestBrowser browser, string email, string passwort)
    {
        var seite = await browser.LadenAsync("/konto/einrichtung");
        return await browser.AbsendenAsync(seite, "einrichtung", new Dictionary<string, string>
        {
            ["Eingabe.Anzeigename"] = "Andy Hahn",
            ["Eingabe.Email"] = email,
            ["Eingabe.Passwort"] = passwort,
            ["Eingabe.PasswortWiederholung"] = passwort,
        });
    }

    /// <summary>Liest den Schlüssel von der Einrichtungsseite, aktiviert 2FA und liefert Schlüssel und Wiederherstellungscodes.</summary>
    protected async Task<(string Schluessel, IReadOnlyList<string> Codes, Seite Seite)> ZweiFaktorAktivierenAsync(TestBrowser browser)
    {
        var seite = await browser.LadenAsync("/konto/sicherheit/zwei-faktor");
        var schluessel = seite.Dokument.QuerySelector("code.schluessel")!.TextContent.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
        var ergebnis = await browser.AbsendenAsync(seite, "zwei-faktor-einrichten", new Dictionary<string, string> { ["Eingabe.Code"] = Code(schluessel) });
        var codes = ergebnis.Dokument.QuerySelectorAll("ul.codes code").Select(c => c.TextContent).ToList();
        return (schluessel, codes, ergebnis);
    }

    protected string Code(string schluessel) => Totp.Code(schluessel, App.Uhr.GetUtcNow());

    protected static async Task<Seite> AnmeldenAsync(TestBrowser browser, string email, string passwort, string? returnUrl = null)
    {
        var seite = await browser.LadenAsync("/konto/anmelden" + (returnUrl is null ? "" : "?ReturnUrl=" + Uri.EscapeDataString(returnUrl)));
        return await browser.AbsendenAsync(seite, "anmelden", new Dictionary<string, string>
        {
            ["Eingabe.Email"] = email,
            ["Eingabe.Passwort"] = passwort,
        });
    }

    protected static async Task AbmeldenAsync(TestBrowser browser)
    {
        var seite = await browser.LadenAsync("/konto/sicherheit");
        var token = seite.Dokument.QuerySelector("form[action='/konto/abmelden'] input[name='__RequestVerificationToken']")!.GetAttribute("value")!;
        using var inhalt = new FormUrlEncodedContent([new("__RequestVerificationToken", token)]);
        using var antwort = await browser.Http.PostAsync(new Uri("/konto/abmelden", UriKind.Relative), inhalt, TestContext.Current.CancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.Redirect, antwort.StatusCode);

        // Die Zielseite muss fehlerfrei laden und die Abmeldung bestätigen.
        var ziel = await browser.LadenAsync(antwort.Headers.Location!.ToString());
        Assert.Equal(System.Net.HttpStatusCode.OK, ziel.Status);
        Assert.Contains("Sie wurden abgemeldet.", ziel.Text, StringComparison.Ordinal);
    }

    /// <summary>Zuletzt an die Adresse eingereihte Mail (Jobs sind aus, daher aus der Outbox).</summary>
    protected async Task<AusgehendeMail?> LetzteMailAnAsync(string email)
    {
        AusgehendeMail? mail = null;
        await App.MitDbAsync(async db => mail = (await db.Set<AusgehendeMail>().AsNoTracking().ToListAsync(TestContext.Current.CancellationToken))
            .Where(m => m.An.Contains(email)).OrderBy(m => m.ErstelltAm).ThenBy(m => m.Id).LastOrDefault());
        return mail;
    }

    protected static string LinkAus(AusgehendeMail mail)
    {
        var treffer = LinkMuster().Match(mail.Html);
        Assert.True(treffer.Success, "Kein Link in der Mail gefunden.");
        return System.Net.WebUtility.HtmlDecode(treffer.Groups[1].Value);
    }

    protected Task<KontoErgebnis> EinladenAsync(string email, string rolle) =>
        App.MitScopeAsync(sp => sp.GetRequiredService<IKontoverwaltung>().EinladenAsync(email, "Testperson", rolle, TestContext.Current.CancellationToken));

    [GeneratedRegex("href=\"(https://localhost/konto/[^\"]+)\"")]
    private static partial Regex LinkMuster();
}
