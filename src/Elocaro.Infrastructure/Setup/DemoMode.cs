using System.Security.Claims;
using Elocaro.Application.Accounts;
using Elocaro.Application.Admin;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Application.Setup;
using Elocaro.Domain.People;
using Elocaro.Domain.Properties;
using Elocaro.Infrastructure.Identity;
using Elocaro.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elocaro.Infrastructure.Setup;

public static partial class DemoMode
{
    /// <summary>On an empty demo instance: creates the demo administrator and loads the example data.</summary>
    public static async Task PrepareDemoAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<DemoOptions>>().Value;
        if (!options.Enabled)
        {
            return;
        }

        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DemoMode));
        if (string.IsNullOrWhiteSpace(options.AdminPassword))
        {
            throw new InvalidOperationException("Demo:AdminPassword fehlt (Demo__AdminPassword).");
        }

        var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
        if (!await accounts.InitialSetupPossibleAsync(cancellationToken))
        {
            return;
        }

        var created = await accounts.CreateFirstAdminAsync(scope.ServiceProvider.GetRequiredService<ISetupCode>().Value, options.AdminEmail, "Max Mustermann", options.AdminPassword, cancellationToken);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException("Der Demo-Benutzer konnte nicht angelegt werden: " + string.Join(" ", created.Errors));
        }

        var loaded = await scope.ServiceProvider.GetRequiredService<IDemoData>().LoadAsync(cancellationToken);

        // Show everything and skip the setup wizard: visitors want to see the whole range of functions.
        await scope.ServiceProvider.GetRequiredService<IModuleState>().SaveAsync(Enum.GetValues<AppModule>(), true, Enum.GetValues<AppOption>(), cancellationToken);
        await CreateUsersAsync(scope.ServiceProvider, options.AdminPassword, cancellationToken);
        await DemoStatements.CreateAsync(scope.ServiceProvider, logger, cancellationToken);
        DemoIsReady(logger, loaded.Succeeded);
    }

    /// <summary>One account per role, linked to the example data so that every role has something to look at.</summary>
    private static async Task CreateUsersAsync(IServiceProvider services, string password, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<ElocaroDbContext>();
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var people = services.GetRequiredService<IPersonService>();
        var propertyId = await db.Set<Property>().OrderBy(p => p.CreatedAt).Select(p => p.Id).FirstAsync(cancellationToken);

        async Task<Guid> AddAsync(DemoUser demo, string role)
        {
            var user = new ApplicationUser { UserName = demo.Email, Email = demo.Email, EmailConfirmed = true, DisplayName = demo.DisplayName };
            var created = await users.CreateAsync(user, password);
            if (!created.Succeeded)
            {
                throw new InvalidOperationException($"Der Demo-Benutzer {demo.Email} konnte nicht angelegt werden: " + string.Join(" ", created.Errors.Select(e => e.Description)));
            }

            await users.AddToRoleAsync(user, role);
            return user.Id;
        }

        async Task LinkPersonAsync(Guid userId, DemoUser demo, ContactType type, bool owner = false)
        {
            var name = demo.DisplayName.Split(' ');
            var person = (await people.CreateAsync(new PersonInput { FirstName = name[0], LastName = name[1], Email = demo.Email, ContactType = type }, null, cancellationToken)).Id!.Value;
            (await db.Set<Person>().SingleAsync(p => p.Id == person, cancellationToken)).UserId = userId;
            await db.SaveChangesAsync(cancellationToken);
            if (owner)
            {
                db.Add(new PropertyOwner(propertyId, person, 100m, null, null));
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        var byRole = DemoOptions.Users.ToDictionary(u => u.Role);

        var manager = await AddAsync(byRole["Verwalter"], Roles.Manager);
        await users.AddClaimsAsync((await users.FindByIdAsync(manager.ToString()))!, Permissions.ManagerDefault.Select(r => new Claim(Permissions.ClaimType, r.ToString())));

        var caretaker = await AddAsync(byRole["Hausmeister"], Roles.Caretaker);
        await LinkPersonAsync(caretaker, byRole["Hausmeister"], ContactType.Caretaker);
        await services.GetRequiredService<IPropertyAssignments>().SetAsync(caretaker, [propertyId], cancellationToken);

        await LinkPersonAsync(await AddAsync(byRole["Handwerker"], Roles.Tradesperson), byRole["Handwerker"], ContactType.Tradesperson);
        await LinkPersonAsync(await AddAsync(byRole["Eigentümer"], Roles.Owner), byRole["Eigentümer"], ContactType.None, owner: true);

        var tenant = await AddAsync(byRole["Mieter"], Roles.Tenant);
        (await db.Set<Person>().FirstAsync(p => p.LastName == "Musterfrau", cancellationToken)).UserId = tenant;
        await db.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Demo-Instanz eingerichtet (Beispieldaten geladen: {Loaded})")]
    private static partial void DemoIsReady(ILogger logger, bool loaded);
}
