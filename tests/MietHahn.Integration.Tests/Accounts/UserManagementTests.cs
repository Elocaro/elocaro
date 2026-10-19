using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Infrastructure.Identity;
using MietHahn.Infrastructure.Persistence;
using MietHahn.Integration.Tests.Infrastructure;

namespace MietHahn.Integration.Tests.Accounts;

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
        await WithAccountsAsync(k => k.CreateFirstAdminAsync("admin@example.de", "Admin", "Langes-Passwort-2026", Ct));

        var admin = (await WithAsync(v => v.ListAsync(Ct))).Single();

        Assert.Equal(Permissions.All, admin.Permissions);
    }

    [Fact]
    public async Task Permissions_exist_only_for_managers()
    {
        var id = await InviteAsync("m@example.de", Roles.Tenant);

        var result = await WithAsync(v => v.SetPermissionsAsync(id, [AccessRight.Finance], Ct));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Role_change_sets_matching_permissions()
    {
        var id = await InviteAsync("x@example.de", Roles.Manager);
        await WithAsync(v => v.SetPermissionsAsync(id, [AccessRight.System], Ct));

        await WithAsync(v => v.ChangeRoleAsync(id, Roles.Caretaker, Ct));
        Assert.Empty((await WithAsync(v => v.GetAsync(id, Ct)))!.Permissions);

        await WithAsync(v => v.ChangeRoleAsync(id, Roles.Manager, Ct));
        Assert.Equal(Permissions.ManagerDefault, (await WithAsync(v => v.GetAsync(id, Ct)))!.Permissions);
    }

    [Fact]
    public async Task Locking_invalidates_open_magic_links()
    {
        var id = await InviteAsync("mieter@example.de", Roles.Tenant);

        await WithAsync(v => v.LockAsync(id, Ct));

        await using var scope = environment.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<MietHahnDbContext>().Set<MagicLink>().AnyAsync(l => l.UserId == id, Ct));
    }

    [Fact]
    public async Task Unknown_role_is_rejected()
    {
        var id = await InviteAsync("y@example.de", Roles.Tenant);

        Assert.False((await WithAsync(v => v.ChangeRoleAsync(id, "Superuser", Ct))).Succeeded);
        Assert.False((await WithAccountsAsync(k => k.InviteAsync("z@example.de", "Z", "Superuser", Ct))).Succeeded);
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
