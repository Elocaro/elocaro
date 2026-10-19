using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.Audit;
using MietHahn.Web.Tests.Account;

namespace MietHahn.Web.Tests.AccessControl;

public class UserManagementTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    [Fact]
    public async Task Admin_invites_via_the_UI()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        var list = await admin.GetAsync("/verwaltung/benutzer");
        var result = await admin.SubmitAsync(list, "invite", new Dictionary<string, string>
        {
            ["Invitation.DisplayName"] = "Erika Mustermann",
            ["Invitation.Email"] = "erika@example.de",
            ["Invitation.Role"] = Roles.Tenant,
        });

        Assert.Contains("Die Einladung an erika@example.de wurde verschickt.", result.Text, StringComparison.Ordinal);
        var line = result.Document.QuerySelectorAll("tbody tr").Single(z => z.TextContent.Contains("erika@example.de", StringComparison.Ordinal));
        Assert.Contains("Mieter", line.TextContent, StringComparison.Ordinal);
        Assert.Contains("eingeladen", line.TextContent, StringComparison.Ordinal);
        Assert.NotNull(await LastMailToAsync("erika@example.de"));
    }

    [Fact]
    public async Task Locked_account_is_signed_out_immediately()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        var (tenant, tenantId) = await SignedInAsAsync(Roles.Tenant);
        using var a = admin;
        using var m = tenant;
        Assert.Equal(HttpStatusCode.OK, (await tenant.GetAsync("/portal")).Status);

        var details = await admin.GetAsync($"/verwaltung/benutzer/{tenantId}");
        var locked = await admin.SubmitAsync(details, "lock");
        Assert.Contains("Das Konto wurde gesperrt.", locked.Text, StringComparison.Ordinal);

        var after = await tenant.GetAsync("/portal", followRedirects: false);
        Assert.Equal("/konto/anmelden", after.RedirectLocation!.AbsolutePath);

        var unlocked = await admin.SubmitAsync(await admin.GetAsync($"/verwaltung/benutzer/{tenantId}"), "lock");
        Assert.Contains("Das Konto wurde entsperrt.", unlocked.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Changing_role_and_permissions_takes_effect_immediately_and_is_logged()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        var (manager, managerId) = await SignedInAsAsync(Roles.Manager);
        using var a = admin;
        using var v = manager;
        Assert.Equal(HttpStatusCode.Redirect, (await manager.GetAsync("/verwaltung/benutzer", followRedirects: false)).Status);

        var details = await admin.GetAsync($"/verwaltung/benutzer/{managerId}");
        var form = details.Form("permissions");
        using var content = new FormUrlEncodedContent(
        [
            new("_handler", "permissions"),
            new("__RequestVerificationToken", form.QuerySelector("input[name=__RequestVerificationToken]")!.GetAttribute("value")!),
            new("PermissionsInput.Options", nameof(AccessRight.MasterData)),
            new("PermissionsInput.Options", nameof(AccessRight.UserManagement)),
        ]);
        using var response = await admin.Http.PostAsync(details.Url, content, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync("/verwaltung/benutzer", followRedirects: false)).Status);

        var history = await App.WithScopeAsync(sp => sp.GetRequiredService<IChangeHistory>().GetAsync("ApplicationUser", managerId, TestContext.Current.CancellationToken));
        Assert.Contains(history.SelectMany(h => h.Changes), f => f is { Field: "Permissions", Old: "MasterData, Finance", New: "MasterData, UserManagement" });

        // Role change to tenant: no more access to the administration.
        var byRole = await admin.SubmitAsync(await admin.GetAsync($"/verwaltung/benutzer/{managerId}"), "role", new Dictionary<string, string> { ["RoleInput.Role"] = Roles.Tenant });
        Assert.Contains("Die Rolle wurde geändert.", byRole.Text, StringComparison.Ordinal);
        Assert.Equal("/konto/zugriff-verweigert", (await manager.GetAsync("/verwaltung", followRedirects: false)).RedirectLocation!.AbsolutePath);
    }

    [Fact]
    public async Task Last_admin_can_neither_be_locked_nor_demoted()
    {
        var (admin, adminId) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        var result = await admin.SubmitAsync(await admin.GetAsync($"/verwaltung/benutzer/{adminId}"), "role", new Dictionary<string, string> { ["RoleInput.Role"] = Roles.Manager });

        Assert.Contains("Der letzte aktive Administrator kann seine Rolle nicht abgeben.", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Konto sperren", result.Text, StringComparison.Ordinal); // own account: do not offer locking

        var lockout = await App.WithScopeAsync(sp => sp.GetRequiredService<IUserManagementService>().LockAsync(adminId, TestContext.Current.CancellationToken));
        Assert.False(lockout.Succeeded);
    }

    [Fact]
    public async Task Second_admin_may_lock_the_first()
    {
        var (_, firstId) = await SignedInAsAsync(Roles.Admin);
        var (second, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = second;

        var result = await second.SubmitAsync(await second.GetAsync($"/verwaltung/benutzer/{firstId}"), "lock");

        Assert.Contains("Das Konto wurde gesperrt.", result.Text, StringComparison.Ordinal);
    }
}
