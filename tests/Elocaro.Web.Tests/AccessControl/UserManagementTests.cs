using System.Net;
using Elocaro.Application.Accounts;
using Elocaro.Application.Audit;
using Elocaro.Web.Tests.Account;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Web.Tests.AccessControl;

public class UserManagementTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    // Account changes go through the service; operating the interactive pages is covered by the browser tests (UiTests).
    private Task<AccountResult> UsersAsync(Func<IUserManagementService, CancellationToken, Task<AccountResult>> action) =>
        App.WithScopeAsync(sp => action(sp.GetRequiredService<IUserManagementService>(), TestContext.Current.CancellationToken));

    [Fact]
    public async Task Invited_account_is_listed_as_invited_and_a_mail_is_queued()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        var invited = await App.WithScopeAsync(sp => sp.GetRequiredService<IAccountService>().InviteAsync("erika@example.de", "Erika Mustermann", Roles.Tenant, TestContext.Current.CancellationToken));
        Assert.True(invited.Succeeded);

        var list = await admin.GetAsync("/verwaltung/benutzer");
        var line = list.Document.QuerySelectorAll("tbody tr").Single(z => z.TextContent.Contains("erika@example.de", StringComparison.Ordinal));
        Assert.Contains("Mieter", line.TextContent, StringComparison.Ordinal);
        Assert.Contains("eingeladen", line.TextContent, StringComparison.Ordinal);
        Assert.NotNull(await LastMailToAsync("erika@example.de"));
    }

    [Fact]
    public async Task Locked_account_is_signed_out_immediately()
    {
        var (admin, adminId) = await SignedInAsAsync(Roles.Admin);
        var (tenant, tenantId) = await SignedInAsAsync(Roles.Tenant);
        using var a = admin;
        using var m = tenant;
        Assert.Equal(HttpStatusCode.OK, (await tenant.GetAsync("/portal")).Status);

        Assert.True((await UsersAsync((users, ct) => users.LockAsync(Principal(adminId), tenantId, ct))).Succeeded);

        var after = await tenant.GetAsync("/portal", followRedirects: false);
        Assert.Equal("/konto/anmelden", after.RedirectLocation!.AbsolutePath);
        Assert.Contains("gesperrt", (await admin.GetAsync($"/verwaltung/benutzer/{tenantId}")).Text, StringComparison.Ordinal);

        Assert.True((await UsersAsync((users, ct) => users.UnlockAsync(Principal(adminId), tenantId, ct))).Succeeded);
    }

    [Fact]
    public async Task Changing_role_and_permissions_ends_the_session_applies_on_next_sign_in_and_is_logged()
    {
        var (admin, adminId) = await SignedInAsAsync(Roles.Admin);
        var (manager, managerId) = await SignedInAsAsync(Roles.Manager);
        using var a = admin;
        using var v = manager;
        Assert.Equal(HttpStatusCode.Redirect, (await manager.GetAsync("/verwaltung/benutzer", followRedirects: false)).Status);

        Assert.True((await UsersAsync((users, ct) => users.SetPermissionsAsync(Principal(adminId), managerId, [AccessRight.MasterData, AccessRight.UserManagement], ct))).Succeeded);

        Assert.Equal("/konto/anmelden", (await manager.GetAsync("/verwaltung/benutzer", followRedirects: false)).RedirectLocation!.AbsolutePath);
        using var withNewRights = await SignedInAgainAsync(managerId);
        Assert.Equal(HttpStatusCode.OK, (await withNewRights.GetAsync("/verwaltung/benutzer", followRedirects: false)).Status);

        var history = await App.WithScopeAsync(sp => sp.GetRequiredService<IChangeHistory>().GetAsync("ApplicationUser", managerId, TestContext.Current.CancellationToken));
        Assert.Contains(history.SelectMany(h => h.Changes), f => f is { Field: "Permissions", Old: "MasterData, Finance", New: "MasterData, UserManagement" });

        // Role change to tenant: no more access to the administration.
        Assert.True((await UsersAsync((users, ct) => users.ChangeRoleAsync(Principal(adminId), managerId, Roles.Tenant, ct))).Succeeded);
        Assert.Equal("/konto/anmelden", (await withNewRights.GetAsync("/verwaltung", followRedirects: false)).RedirectLocation!.AbsolutePath);
        using var asTenant = await SignedInAgainAsync(managerId);
        Assert.Equal("/konto/zugriff-verweigert", (await asTenant.GetAsync("/verwaltung", followRedirects: false)).RedirectLocation!.AbsolutePath);
    }

    [Fact]
    public async Task Last_admin_can_neither_be_locked_nor_demoted()
    {
        var (admin, adminId) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        var demote = await UsersAsync((users, ct) => users.ChangeRoleAsync(Principal(adminId), adminId, Roles.Manager, ct));
        Assert.False(demote.Succeeded);
        Assert.Contains("Der letzte aktive Administrator kann seine Rolle nicht abgeben.", string.Join(" ", demote.Errors), StringComparison.Ordinal);
        Assert.False((await UsersAsync((users, ct) => users.LockAsync(Principal(adminId), adminId, ct))).Succeeded);
    }

    [Fact]
    public async Task Nobody_can_lock_their_own_account()
    {
        var (_, otherAdminId) = await SignedInAsAsync(Roles.Admin);
        var (_, adminId) = await SignedInAsAsync(Roles.Admin);
        var (_, managerId) = await SignedInAsAsync(Roles.Manager, [AccessRight.UserManagement]);

        foreach (var id in new[] { adminId, managerId })
        {
            var result = await UsersAsync((users, ct) => users.LockAsync(Principal(id), id, ct));

            Assert.False(result.Succeeded);
            Assert.Contains("Das eigene Konto kann nicht gesperrt werden.", string.Join(" ", result.Errors), StringComparison.Ordinal);
        }

        Assert.NotEqual(otherAdminId, adminId);
    }

    [Fact]
    public async Task Second_admin_may_lock_the_first()
    {
        var (_, firstId) = await SignedInAsAsync(Roles.Admin);
        var (second, secondId) = await SignedInAsAsync(Roles.Admin);
        using var _ = second;

        Assert.True((await UsersAsync((users, ct) => users.LockAsync(Principal(secondId), firstId, ct))).Succeeded);
    }
}
