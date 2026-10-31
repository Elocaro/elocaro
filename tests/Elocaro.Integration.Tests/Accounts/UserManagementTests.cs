using System.Security.Claims;
using Elocaro.Application.Accounts;
using Elocaro.Infrastructure.Identity;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Accounts;

public class UserManagementTests(PostgresFixture postgres) : IAsyncLifetime
{
    private InfrastructureEnvironment environment = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => environment = await InfrastructureEnvironment.CreateAsync(postgres);

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Invited_manager_receives_default_permissions()
    {
        var id = await InviteAsync("v@example.de", Roles.Manager);

        var user = await WithAsync(v => v.GetAsync(id, Ct));

        Assert.Equal(Roles.Manager, user!.Role);
        Assert.Equal(Permissions.ManagerDefault, user.Permissions);
        Assert.False(user.InvitationAccepted);
        Assert.False(user.HasPassword);
    }

    [Fact]
    public async Task Admins_always_have_all_permissions()
    {
        await WithAccountsAsync(k => k.CreateFirstAdminAsync(environment.Services.GetRequiredService<ISetupCode>().Value, "admin@example.de", "Admin", "Langes-Passwort-2026", Ct));

        var admin = (await WithAsync(v => v.ListAsync(Ct))).Single();

        Assert.Equal(Permissions.All, admin.Permissions);
    }

    [Fact]
    public async Task Permissions_exist_only_for_managers()
    {
        var id = await InviteAsync("m@example.de", Roles.Tenant);
        var admin = await AdminAsync();

        var result = await WithAsync(v => v.SetPermissionsAsync(admin, id, [AccessRight.Finance], Ct));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Role_change_sets_matching_permissions()
    {
        var id = await InviteAsync("x@example.de", Roles.Manager);
        var admin = await AdminAsync();
        await WithAsync(v => v.SetPermissionsAsync(admin, id, [AccessRight.System], Ct));

        await WithAsync(v => v.ChangeRoleAsync(admin, id, Roles.Caretaker, Ct));
        Assert.Empty((await WithAsync(v => v.GetAsync(id, Ct)))!.Permissions);

        await WithAsync(v => v.ChangeRoleAsync(admin, id, Roles.Manager, Ct));
        Assert.Equal(Permissions.ManagerDefault, (await WithAsync(v => v.GetAsync(id, Ct)))!.Permissions);
    }

    [Fact]
    public async Task Locking_invalidates_open_magic_links()
    {
        var id = await InviteAsync("mieter@example.de", Roles.Tenant);

        await WithAsync(async v => await v.LockAsync(await AdminAsync(), id, Ct));

        await using var scope = environment.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<ElocaroDbContext>().Set<MagicLink>().AnyAsync(l => l.UserId == id, Ct));
    }

    [Fact]
    public async Task Unknown_role_is_rejected()
    {
        var id = await InviteAsync("y@example.de", Roles.Tenant);

        var admin = await AdminAsync();
        Assert.False((await WithAsync(v => v.ChangeRoleAsync(admin, id, "Superuser", Ct))).Succeeded);
        Assert.False((await WithAccountsAsync(k => k.InviteAsync("z@example.de", "Z", "Superuser", Ct))).Succeeded);
    }

    [Fact]
    public async Task Non_admin_can_neither_grant_nor_invite_the_admin_role()
    {
        var actor = await ManagerAsync([AccessRight.UserManagement]);
        var tenant = await InviteAsync("t@example.de", Roles.Tenant);

        var changed = await WithAsync(v => v.ChangeRoleAsync(actor, tenant, Roles.Admin, Ct));
        var invited = await WithAsync(v => v.InviteAsync(actor, "neu@example.de", "Neu", Roles.Admin, Ct));

        Assert.Contains("Nur Administratoren", Assert.Single(changed.Errors), StringComparison.Ordinal);
        Assert.Contains("Nur Administratoren", Assert.Single(invited.Errors), StringComparison.Ordinal);
        Assert.Equal(Roles.Tenant, (await WithAsync(v => v.GetAsync(tenant, Ct)))!.Role);
        Assert.DoesNotContain(await WithAsync(v => v.ListAsync(Ct)), u => u.Email == "neu@example.de");
    }

    [Fact]
    public async Task Non_admin_cannot_change_lock_or_unlock_admin_accounts()
    {
        var actor = await ManagerAsync([AccessRight.UserManagement]);
        var admin = await InviteAsync("a1@example.de", Roles.Admin);
        var lockedAdmin = await InviteAsync("a2@example.de", Roles.Admin);
        await InviteAsync("a3@example.de", Roles.Admin);
        var byAdmin = Principal(await InviteAsync("a4@example.de", Roles.Admin));
        Assert.True((await WithAsync(v => v.LockAsync(byAdmin, lockedAdmin, Ct))).Succeeded);

        Assert.False((await WithAsync(v => v.ChangeRoleAsync(actor, admin, Roles.Tenant, Ct))).Succeeded);
        Assert.False((await WithAsync(v => v.SetPermissionsAsync(actor, admin, [], Ct))).Succeeded);
        Assert.False((await WithAsync(v => v.LockAsync(actor, admin, Ct))).Succeeded);
        Assert.False((await WithAsync(v => v.UnlockAsync(actor, lockedAdmin, Ct))).Succeeded);

        Assert.Equal(Roles.Admin, (await WithAsync(v => v.GetAsync(admin, Ct)))!.Role);
        Assert.False((await WithAsync(v => v.GetAsync(admin, Ct)))!.Locked);
        Assert.True((await WithAsync(v => v.GetAsync(lockedAdmin, Ct)))!.Locked);
    }

    [Fact]
    public async Task Non_admin_cannot_change_their_own_role_or_permissions()
    {
        var actor = await ManagerAsync([AccessRight.UserManagement, AccessRight.Finance]);
        var id = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);

        Assert.False((await WithAsync(v => v.ChangeRoleAsync(actor, id, Roles.Caretaker, Ct))).Succeeded);
        Assert.False((await WithAsync(v => v.SetPermissionsAsync(actor, id, [AccessRight.UserManagement], Ct))).Succeeded);

        var own = await WithAsync(v => v.GetAsync(id, Ct));
        Assert.Equal(Roles.Manager, own!.Role);
        Assert.Contains(AccessRight.Finance, own.Permissions);
    }

    [Fact]
    public async Task Non_admin_grants_only_permissions_they_have_themselves()
    {
        var actor = await ManagerAsync([AccessRight.UserManagement, AccessRight.MasterData]);
        var other = await InviteAsync("o@example.de", Roles.Manager); // default: MasterData, Finance

        var refused = await WithAsync(v => v.SetPermissionsAsync(actor, other, [AccessRight.System], Ct));
        Assert.Contains("selbst haben", Assert.Single(refused.Errors), StringComparison.Ordinal);

        // Finance stays although the actor lacks it (it is not granted); UserManagement is theirs to hand on.
        var allowed = await WithAsync(v => v.SetPermissionsAsync(actor, other, [AccessRight.Finance, AccessRight.UserManagement], Ct));
        Assert.True(allowed.Succeeded, string.Join(", ", allowed.Errors));
        Assert.Equal([AccessRight.Finance, AccessRight.UserManagement], (await WithAsync(v => v.GetAsync(other, Ct)))!.Permissions.Order());
    }

    [Fact]
    public async Task Manager_created_by_a_non_admin_gets_no_default_permission_the_actor_lacks()
    {
        var actor = await ManagerAsync([AccessRight.UserManagement, AccessRight.MasterData]);
        var tenant = await InviteAsync("t@example.de", Roles.Tenant);

        Assert.True((await WithAsync(v => v.ChangeRoleAsync(actor, tenant, Roles.Manager, Ct))).Succeeded);
        var invited = await WithAsync(v => v.InviteAsync(actor, "neu@example.de", "Neu", Roles.Manager, Ct));

        Assert.Equal([AccessRight.MasterData], (await WithAsync(v => v.GetAsync(tenant, Ct)))!.Permissions);
        Assert.Equal([AccessRight.MasterData], (await WithAsync(v => v.GetAsync(invited.UserId!.Value, Ct)))!.Permissions);
    }

    [Fact]
    public async Task Accounts_without_the_permission_or_locked_ones_may_change_nothing()
    {
        var withoutRight = await ManagerAsync([AccessRight.Finance]);
        var tenantActor = Principal(await InviteAsync("t1@example.de", Roles.Tenant));
        var lockedActor = await ManagerAsync([AccessRight.UserManagement]);
        var target = await InviteAsync("t2@example.de", Roles.Tenant);
        var admin = await AdminAsync();
        Assert.True((await WithAsync(v => v.LockAsync(admin, Guid.Parse(lockedActor.FindFirstValue(ClaimTypes.NameIdentifier)!), Ct))).Succeeded);

        foreach (var actor in new[] { withoutRight, tenantActor, lockedActor, new ClaimsPrincipal() })
        {
            Assert.False((await WithAsync(v => v.LockAsync(actor, target, Ct))).Succeeded);
            Assert.False((await WithAsync(v => v.ChangeRoleAsync(actor, target, Roles.Caretaker, Ct))).Succeeded);
            Assert.False((await WithAsync(v => v.InviteAsync(actor, "x@example.de", "X", Roles.Tenant, Ct))).Succeeded);
        }
    }

    [Fact]
    public async Task Manager_with_the_permission_manages_other_accounts()
    {
        var actor = await ManagerAsync([AccessRight.UserManagement]);
        var other = await InviteAsync("o@example.de", Roles.Tenant);

        Assert.True((await WithAsync(v => v.ChangeRoleAsync(actor, other, Roles.Caretaker, Ct))).Succeeded);
        Assert.True((await WithAsync(v => v.LockAsync(actor, other, Ct))).Succeeded);
        Assert.True((await WithAsync(v => v.UnlockAsync(actor, other, Ct))).Succeeded);
        Assert.True((await WithAsync(v => v.InviteAsync(actor, "neu@example.de", "Neu", Roles.Tenant, Ct))).Succeeded);
    }

    [Fact]
    public async Task Admin_may_do_everything_incl_own_role_and_other_admins()
    {
        var admin = await AdminAsync();
        var adminId = Guid.Parse(admin.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var second = await InviteAsync("zweiter@example.de", Roles.Admin);
        var tenant = await InviteAsync("t@example.de", Roles.Tenant);

        var invited = await WithAsync(v => v.InviteAsync(admin, "dritter@example.de", "Dritter", Roles.Admin, Ct));
        Assert.Equal(Roles.Admin, (await WithAsync(v => v.GetAsync(invited.UserId!.Value, Ct)))!.Role);
        Assert.True((await WithAsync(v => v.ChangeRoleAsync(admin, tenant, Roles.Admin, Ct))).Succeeded);
        Assert.True((await WithAsync(v => v.LockAsync(admin, second, Ct))).Succeeded);
        Assert.True((await WithAsync(v => v.UnlockAsync(admin, second, Ct))).Succeeded);
        Assert.True((await WithAsync(v => v.ChangeRoleAsync(admin, second, Roles.Manager, Ct))).Succeeded);
        Assert.True((await WithAsync(v => v.SetPermissionsAsync(admin, second, Permissions.All, Ct))).Succeeded);
        Assert.True((await WithAsync(v => v.ChangeRoleAsync(admin, adminId, Roles.Caretaker, Ct))).Succeeded);
    }

    [Fact]
    public async Task Role_and_permission_changes_renew_the_security_stamp_of_an_account_that_has_signed_in()
    {
        var admin = await AdminAsync();
        var id = await InviteAsync("v@example.de", Roles.Manager);
        await WithUsersAsync(async users =>
        {
            var user = (await users.FindByIdAsync(id.ToString()))!;
            user.LastSignInAt = DateTimeOffset.UtcNow;
            await users.UpdateAsync(user);
        });

        var initial = await StampAsync(id);
        Assert.True((await WithAsync(v => v.SetPermissionsAsync(admin, id, [AccessRight.Finance], Ct))).Succeeded);
        var afterPermissions = await StampAsync(id);
        Assert.True((await WithAsync(v => v.ChangeRoleAsync(admin, id, Roles.Caretaker, Ct))).Succeeded);

        Assert.NotEqual(initial, afterPermissions);
        Assert.NotEqual(afterPermissions, await StampAsync(id));
    }

    [Fact]
    public async Task Invitation_link_survives_adjusting_permissions_before_the_first_sign_in()
    {
        var admin = await AdminAsync();
        var id = await InviteAsync("v@example.de", Roles.Manager);
        var initial = await StampAsync(id);

        Assert.True((await WithAsync(v => v.SetPermissionsAsync(admin, id, [AccessRight.Finance], Ct))).Succeeded);

        Assert.Equal(initial, await StampAsync(id));
    }

    private async Task<ClaimsPrincipal> AdminAsync() => Principal(await InviteAsync($"admin-{Guid.NewGuid():N}@example.de", Roles.Admin));

    private async Task<ClaimsPrincipal> ManagerAsync(IReadOnlyCollection<AccessRight> permissions)
    {
        var id = await InviteAsync($"verwalter-{Guid.NewGuid():N}@example.de", Roles.Manager);
        var admin = await AdminAsync();
        var result = await WithAsync(v => v.SetPermissionsAsync(admin, id, permissions, Ct));
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return Principal(id);
    }

    private static ClaimsPrincipal Principal(Guid id) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], "Test"));

    private async Task<string?> StampAsync(Guid id)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await users.GetSecurityStampAsync((await users.FindByIdAsync(id.ToString()))!);
    }

    private async Task WithUsersAsync(Func<UserManager<ApplicationUser>, Task> action)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>());
    }

    private async Task<Guid> InviteAsync(string email, string role)
    {
        var result = await WithAccountsAsync(k => k.InviteAsync(email, "Test", role, Ct));
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.UserId!.Value;
    }

    private async Task<T> WithAsync<T>(Func<IUserManagementService, Task<T>> action)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IUserManagementService>());
    }

    private async Task<T> WithAccountsAsync<T>(Func<IAccountService, Task<T>> action)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IAccountService>());
    }
}
