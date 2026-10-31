using Elocaro.Application.Accounts;
using Elocaro.Application.Properties;
using Elocaro.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Elocaro.Web.Tests.Browser;

/// <summary>Operating the interactive user pages: roles, permissions, property assignment, own account.</summary>
public class UsersBrowserTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    private async Task<Guid> CreateAccountAsync(string role, string email) =>
        await App.WithScopeAsync(async sp =>
        {
            var manager = sp.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = role + " Test" };
            Assert.True((await manager.CreateAsync(user, "Test-Passwort-2026-lang")).Succeeded);
            await manager.AddToRoleAsync(user, role);
            return user.Id;
        });

    [Fact]
    public async Task Permissions_of_a_manager_are_saved_and_the_role_can_be_changed()
    {
        var managerId = await CreateAccountAsync(Roles.Manager, "verwalter@example.de");
        var page = await SignedInAsAdminAsync(mobile: false);

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await GotoInteractiveAsync(page, $"/verwaltung/benutzer/{managerId}");
            await page.GetByLabel("Benutzerverwaltung").CheckAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Rechte speichern" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Die Rechte wurden gespeichert.");

            await SelectAsync(page, "Rolle", "Mieter");
            await page.GetByRole(AriaRole.Button, new() { Name = "Rolle ändern" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Die Rolle wurde geändert.");
            await Assertions.Expect(page.Locator("h1 + p, .page-header .small").First).ToContainTextAsync("Mieter");
        });

        await App.WithScopeAsync(async sp =>
        {
            var user = await sp.GetRequiredService<IUserManagementService>().GetAsync(managerId, TestContext.Current.CancellationToken);
            Assert.Equal(Roles.Tenant, user!.Role);
            return 0;
        });
    }

    [Fact]
    public async Task Manager_is_offered_neither_the_admin_role_nor_permissions_they_lack()
    {
        var otherId = await CreateAccountAsync(Roles.Manager, "verwalter@example.de");
        var adminId = await CreateAccountAsync(Roles.Admin, "chef@example.de");
        var page = await SignedInAsManagerAsync([AccessRight.UserManagement, AccessRight.MasterData]);
        var roleChange = page.GetByRole(AriaRole.Button, new() { Name = "Rolle ändern" });

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await GotoInteractiveAsync(page, $"/verwaltung/benutzer/{otherId}");
            await Assertions.Expect(roleChange).ToHaveCountAsync(1);
            await Assertions.Expect(page.GetByLabel(Permissions.DisplayName(AccessRight.MasterData))).ToHaveCountAsync(1);
            await Assertions.Expect(page.GetByLabel(Permissions.DisplayName(AccessRight.Finance))).ToHaveCountAsync(0);
            await Assertions.Expect(page.GetByLabel(Permissions.DisplayName(AccessRight.System))).ToHaveCountAsync(0);
            await page.Locator(".mud-select", new() { Has = page.Locator("[aria-label='Rolle']") }).First.Locator(".mud-input").First.ClickAsync();
            await Assertions.Expect(page.Locator(".mud-popover-open .mud-list-item", new() { HasText = "Verwalter" })).ToHaveCountAsync(1);
            await Assertions.Expect(page.Locator(".mud-popover-open .mud-list-item", new() { HasText = "Administrator" })).ToHaveCountAsync(0);

            await GotoInteractiveAsync(page, $"/verwaltung/benutzer/{adminId}");
            await Assertions.Expect(page.GetByText("Dieses Konto kann nur ein Administrator ändern.")).ToHaveCountAsync(1);
            await Assertions.Expect(roleChange).ToHaveCountAsync(0);
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Konto sperren" })).ToHaveCountAsync(0);

            await GotoInteractiveAsync(page, "/verwaltung/benutzer");
            await page.GetByRole(AriaRole.Link, new() { Name = "Max Mustermann" }).ClickAsync();
            await Assertions.Expect(page.GetByText("Ihre eigene Rolle und Ihre eigenen Rechte kann nur ein Administrator ändern.")).ToHaveCountAsync(1);
            await Assertions.Expect(roleChange).ToHaveCountAsync(0);

            await GotoInteractiveAsync(page, "/verwaltung/benutzer");
            await page.GetByRole(AriaRole.Button, new() { Name = "Person einladen" }).ClickAsync();
            await page.Locator(".mud-dialog .mud-select .mud-input").First.ClickAsync();
            await Assertions.Expect(page.Locator(".mud-popover-open .mud-list-item", new() { HasText = "Mieter" })).ToHaveCountAsync(1);
            await Assertions.Expect(page.Locator(".mud-popover-open .mud-list-item", new() { HasText = "Administrator" })).ToHaveCountAsync(0);
        });
    }

    [Fact]
    public async Task Caretaker_gets_properties_assigned()
    {
        var caretakerId = await CreateAccountAsync(Roles.Caretaker, "hausmeister@example.de");
        var propertyId = await App.WithScopeAsync(async sp => (await sp.GetRequiredService<IPropertyService>().CreateAsync(
            new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, TestContext.Current.CancellationToken)).Id!.Value);
        var page = await SignedInAsAdminAsync(mobile: false);

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await GotoInteractiveAsync(page, $"/verwaltung/benutzer/{caretakerId}");
            await page.GetByLabel("Lindenweg 3").CheckAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Objekte speichern" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Die betreuten Objekte wurden gespeichert.");
        });

        var assigned = await App.WithScopeAsync(sp => sp.GetRequiredService<IPropertyAssignments>().PropertiesOfAsync(caretakerId, TestContext.Current.CancellationToken));
        Assert.Equal([propertyId], assigned);
    }

    [Fact]
    public async Task Own_account_offers_no_locking()
    {
        var page = await SignedInAsAdminAsync(mobile: false);
        await GotoInteractiveAsync(page, "/verwaltung/benutzer");
        await page.GetByRole(AriaRole.Link, new() { Name = "Max Mustermann" }).ClickAsync();
        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Max Mustermann");
        await Assertions.Expect(page.Locator("[data-interactive=true]")).ToHaveCountAsync(1);

        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Konto sperren" })).ToHaveCountAsync(0);
    }
}
