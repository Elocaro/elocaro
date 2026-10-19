using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Domain.Communication;
using MietHahn.Infrastructure.Identity;

namespace MietHahn.Web.Tests.Account;

/// <summary>A fresh app with its own database per test, plus helpers for typical sign-in steps.</summary>
public abstract partial class AccountTestBase(PostgresFixture postgres) : IAsyncLifetime
{
    protected const string AdminEmail = "vermieter@example.de";
    protected const string AdminPassword = "Sehr-sicheres-Passwort-2026";

    protected MietHahnWebFactory App { get; private set; } = null!;

    protected virtual IDictionary<string, string?>? Configuration => null;

    protected virtual void ConfigureServices(IServiceCollection services)
    {
    }

    public async ValueTask InitializeAsync() => App = await MietHahnWebFactory.WithDatabaseAsync(postgres, Configuration, ConfigureServices);

    /// <summary>
    /// Creates an account with the given role directly (with password, with 2FA enabled if 2FA is mandatory) and signs it in via
    /// the UI. Returns the browser and user id.
    /// </summary>
    protected async Task<(TestBrowser Browser, Guid Id)> SignedInAsAsync(string role, IReadOnlyCollection<AccessRight>? permissions = null, string? email = null)
    {
        email ??= $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@example.de";
        const string password = "Test-Passwort-2026-lang";
        var (id, key) = await App.WithScopeAsync(async sp =>
        {
            var manager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = role + " Test" };
            Assert.True((await manager.CreateAsync(user, password)).Succeeded);
            await manager.AddToRoleAsync(user, role);
            if (role == Roles.Manager)
            {
                await manager.AddClaimsAsync(user, (permissions ?? Permissions.ManagerDefault).Select(r => new System.Security.Claims.Claim(Permissions.ClaimType, r.ToString())));
            }

            string? key = null;
            if (Roles.RequiringTwoFactor.Contains(role))
            {
                await manager.ResetAuthenticatorKeyAsync(user);
                key = await manager.GetAuthenticatorKeyAsync(user);
                await manager.SetTwoFactorEnabledAsync(user, true);
            }

            return (user.Id, key);
        });

        var browser = App.Browser();
        var page = await SignInAsync(browser, email, password);
        if (key is not null)
        {
            page = await browser.SubmitAsync(page, "two-factor", new Dictionary<string, string> { ["Input.Code"] = Code(key) });
        }

        Assert.DoesNotContain("/konto/", page.Path, StringComparison.Ordinal);
        return (browser, id);
    }

    public virtual async ValueTask DisposeAsync() => await App.DisposeAsync();

    protected static Task<Page> SetUpAsync(TestBrowser browser) => SetUpAsync(browser, AdminEmail, AdminPassword);

    protected static async Task<Page> SetUpAsync(TestBrowser browser, string email, string password)
    {
        var page = await browser.GetAsync("/konto/einrichtung");
        return await browser.SubmitAsync(page, "setup", new Dictionary<string, string>
        {
            ["Input.DisplayName"] = "Andy Hahn",
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["Input.PasswordConfirmation"] = password,
        });
    }

    /// <summary>Reads the key from the setup page, enables 2FA and returns the key and recovery codes.</summary>
    protected async Task<(string Key, IReadOnlyList<string> Codes, Page Page)> EnableTwoFactorAsync(TestBrowser browser)
    {
        var page = await browser.GetAsync("/konto/sicherheit/zwei-faktor");
        var key = page.Document.QuerySelector("code.key")!.TextContent.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
        var result = await browser.SubmitAsync(page, "two-factor-setup", new Dictionary<string, string> { ["Input.Code"] = Code(key) });
        var codes = result.Document.QuerySelectorAll("ul.codes code").Select(c => c.TextContent).ToList();
        return (key, codes, result);
    }

    protected string Code(string key) => Totp.Code(key, App.Clock.GetUtcNow());

    protected static async Task<Page> SignInAsync(TestBrowser browser, string email, string password, string? returnUrl = null)
    {
        var page = await browser.GetAsync("/konto/anmelden" + (returnUrl is null ? "" : "?ReturnUrl=" + Uri.EscapeDataString(returnUrl)));
        return await browser.SubmitAsync(page, "login", new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
        });
    }

    protected static async Task SignOutAsync(TestBrowser browser)
    {
        var page = await browser.GetAsync("/konto/sicherheit");
        var token = page.Document.QuerySelector("form[action='/konto/abmelden'] input[name='__RequestVerificationToken']")!.GetAttribute("value")!;
        using var content = new FormUrlEncodedContent([new("__RequestVerificationToken", token)]);
        using var response = await browser.Http.PostAsync(new Uri("/konto/abmelden", UriKind.Relative), content, TestContext.Current.CancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);

        // The target page must load without errors and confirm the sign-out.
        var destination = await browser.GetAsync(response.Headers.Location!.ToString());
        Assert.Equal(System.Net.HttpStatusCode.OK, destination.Status);
        Assert.Contains("Sie wurden abgemeldet.", destination.Text, StringComparison.Ordinal);
    }

    /// <summary>Most recent mail queued for the address (jobs are off, hence taken from the outbox).</summary>
    protected async Task<OutgoingMail?> LastMailToAsync(string email)
    {
        OutgoingMail? mail = null;
        await App.WithDbAsync(async db => mail = (await db.Set<OutgoingMail>().AsNoTracking().ToListAsync(TestContext.Current.CancellationToken))
            .Where(m => m.To.Contains(email)).OrderBy(m => m.CreatedAt).ThenBy(m => m.Id).LastOrDefault());
        return mail;
    }

    protected static string ExtractLink(OutgoingMail mail)
    {
        var match = LinkPattern().Match(mail.Html);
        Assert.True(match.Success, "Kein Link in der Mail gefunden.");
        return System.Net.WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    protected Task<AccountResult> InviteAsync(string email, string role) =>
        App.WithScopeAsync(sp => sp.GetRequiredService<IAccountService>().InviteAsync(email, "Testperson", role, TestContext.Current.CancellationToken));

    [GeneratedRegex("href=\"(https://localhost/konto/[^\"]+)\"")]
    private static partial Regex LinkPattern();
}
