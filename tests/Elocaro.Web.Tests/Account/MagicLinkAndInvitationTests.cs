using System.Net;
using Elocaro.Application.Accounts;
using Elocaro.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Web.Tests.Account;

public class MagicLinkAndInvitationTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private const string TenantEmail = "mieterin@example.de";

    [Fact]
    public async Task Invited_tenant_signs_in_via_link_and_the_link_is_used_up_afterwards()
    {
        Assert.True((await InviteAsync(TenantEmail, Roles.Tenant)).Succeeded);
        var mail = (await LastMailToAsync(TenantEmail))!;
        Assert.Equal("Ihr Zugang zu Elocaro", mail.Subject);
        var link = ExtractLink(mail);
        Assert.Matches("^https://localhost/konto/link/[A-Za-z0-9_-]{43}$", link);

        using var browser = App.Browser();
        var page = await browser.GetAsync(link);
        Assert.Contains("Klicken Sie auf „Anmelden“", page.Text, StringComparison.Ordinal);

        // Merely opening it (e.g. by a mail scanner) does not consume the link.
        var openedAgain = await browser.GetAsync(link);
        var signedIn = await browser.SubmitAsync(openedAgain, "link-redeem");
        Assert.Equal("/portal", signedIn.Path);
        Assert.Equal(HttpStatusCode.OK, signedIn.Status);

        using var secondBrowser = App.Browser();
        var again = await secondBrowser.SubmitAsync(await secondBrowser.GetAsync(link), "link-redeem");
        Assert.Contains("ungültig, abgelaufen oder wurde bereits verwendet", again.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Requested_magic_link_expires_after_15_minutes()
    {
        await InviteAsync(TenantEmail, Roles.Tenant);
        using var browser = App.Browser();
        var request = await browser.GetAsync("/konto/link");
        await browser.SubmitAsync(request, "magic-link", new Dictionary<string, string> { ["Input.Email"] = TenantEmail });
        var mail = (await LastMailToAsync(TenantEmail))!;
        Assert.Equal("Ihr Anmeldelink für Elocaro", mail.Subject);

        App.Clock.Advance(TimeSpan.FromMinutes(16));
        var page = await browser.SubmitAsync(await browser.GetAsync(ExtractLink(mail)), "link-redeem");

        Assert.Contains("ungültig, abgelaufen", page.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("unbekannt@example.de")]
    [InlineData(AdminEmail)]
    public async Task Magic_link_request_reveals_nothing_and_sends_admins_no_link(string email)
    {
        using var browser = App.Browser();
        await SetUpAsync(browser);

        var request = await browser.GetAsync("/konto/link");
        var result = await browser.SubmitAsync(request, "magic-link", new Dictionary<string, string> { ["Input.Email"] = email });

        Assert.Contains("Falls ein passendes Konto existiert", result.Text, StringComparison.Ordinal);
        Assert.Null(await LastMailToAsync(email));
    }

    [Fact]
    public async Task Invited_manager_sets_password_and_must_set_up_two_factor()
    {
        const string email = "partnerin@example.de";
        await InviteAsync(email, Roles.Manager);
        var mail = (await LastMailToAsync(email))!;
        Assert.Equal("Einladung zu Elocaro", mail.Subject);
        Assert.Contains("Verwalter", mail.Html, StringComparison.Ordinal);

        using var browser = App.Browser();
        var page = await browser.GetAsync(ExtractLink(mail));
        var result = await browser.SubmitAsync(page, "invitation", new Dictionary<string, string>
        {
            ["Input.Password"] = "Partnerin-Passwort-2026",
            ["Input.PasswordConfirmation"] = "Partnerin-Passwort-2026",
        });

        Assert.Equal("/konto/sicherheit/zwei-faktor", result.Path);

        // The invitation cannot be used again afterwards.
        using var otherBrowser = App.Browser();
        Assert.Contains("ungültig oder abgelaufen", (await otherBrowser.GetAsync(ExtractLink(mail))).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tampered_invitation_is_rejected()
    {
        const string email = "hausmeister@example.de";
        await InviteAsync(email, Roles.Caretaker);
        var link = ExtractLink((await LastMailToAsync(email))!);
        using var browser = App.Browser();

        Assert.Contains("ungültig oder abgelaufen", (await browser.GetAsync(link[..^4] + "AAAA")).Text, StringComparison.Ordinal);
        Assert.Contains("ungültig oder abgelaufen", (await browser.GetAsync("/konto/einladung")).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Duplicate_invitation_is_rejected()
    {
        await InviteAsync(TenantEmail, Roles.Tenant);

        var second = await InviteAsync(TenantEmail, Roles.Tenant);

        Assert.False(second.Succeeded);
        Assert.Contains("existiert bereits", second.Errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Locked_tenant_cannot_use_the_link()
    {
        await InviteAsync(TenantEmail, Roles.Tenant);
        var link = ExtractLink((await LastMailToAsync(TenantEmail))!);
        await App.WithScopeAsync(async sp =>
        {
            var manager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await manager.FindByEmailAsync(TenantEmail);
            user!.Locked = true;
            return await manager.UpdateAsync(user);
        });

        using var browser = App.Browser();
        var page = await browser.SubmitAsync(await browser.GetAsync(link), "link-redeem");

        Assert.Contains("ungültig", page.Text, StringComparison.Ordinal);
    }
}
