using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.Common;
using MietHahn.Application.Files;
using MietHahn.Application.Leases;
using MietHahn.Application.Notices;
using MietHahn.Application.People;
using MietHahn.Application.Properties;
using MietHahn.Domain.Communication;
using MietHahn.Domain.Notices;
using MietHahn.Domain.People;
using MietHahn.Infrastructure.Identity;
using MietHahn.Infrastructure.Notices;
using MietHahn.Infrastructure.Persistence;
using MietHahn.Integration.Tests.Infrastructure;

namespace MietHahn.Integration.Tests.Notices;

public class NoticeTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateOnly Today = new(2026, 3, 1);

    private InfrastructureEnvironment environment = null!;
    private Guid house;
    private ClaimsPrincipal anna = null!;
    private ClaimsPrincipal ben = null!;
    private ClaimsPrincipal admin = null!;
    private ClaimsPrincipal carla = null!;
    private Guid otherHouse;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres);
        await using var scope = environment.Services.CreateAsyncScope();
        var properties = scope.ServiceProvider.GetRequiredService<IPropertyService>();
        house = Validate(await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct));
        var unit1 = Validate(await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 1" }, Ct));
        var unit2 = Validate(await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 2" }, Ct));
        var people = scope.ServiceProvider.GetRequiredService<IPersonService>();
        var annaPerson = Validate(await people.CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt", Email = "anna@example.de" }, null, Ct));
        var benPerson = Validate(await people.CreateAsync(new PersonInput { FirstName = "Ben", LastName = "Weber", Email = "ben@example.de" }, null, Ct));
        var leases = scope.ServiceProvider.GetRequiredService<ILeaseService>();
        Validate(await leases.CreateAsync(new LeaseCreation { RentalUnitId = unit1, Start = Today, TenantIds = [annaPerson], BaseRent = 650m }, Ct));
        Validate(await leases.CreateAsync(new LeaseCreation { RentalUnitId = unit2, Start = Today, TenantIds = [benPerson], BaseRent = 700m }, Ct));

        var carlaPerson = Validate(await people.CreateAsync(new PersonInput { FirstName = "Carla", LastName = "Neu", Email = "carla@example.de" }, null, Ct));
        otherHouse = Validate(await properties.CreateAsync(new PropertyInput { Name = "Buchenweg 1", Street = "Buchenweg 1", PostalCode = "50667", City = "Köln" }, Ct));
        var unit3 = Validate(await properties.CreateUnitAsync(otherHouse, new UnitInput { Name = "Wohnung 3" }, Ct));
        Validate(await leases.CreateAsync(new LeaseCreation { RentalUnitId = unit3, Start = Today, TenantIds = [carlaPerson], BaseRent = 500m }, Ct));
        carla = await LinkedAccountAsync(carlaPerson, "carla@example.de", Roles.Tenant);
        anna = await LinkedAccountAsync(annaPerson, "anna@example.de", Roles.Tenant);
        ben = await LinkedAccountAsync(benPerson, "ben@example.de", Roles.Tenant);
        admin = Account(await AccountAsync("chef@example.de", Roles.Admin), Roles.Admin);
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Notice_reaches_all_tenants_of_the_property_and_nobody_else()
    {
        var file = await FileAsync("hausordnung");
        Validate(await With<INotices, Result>(n => n.CreateAsync(admin, Input("Wasser wird abgestellt"), [file], Ct)));

        foreach (var tenant in new[] { anna, ben })
        {
            var current = await With<INotices, IReadOnlyList<TenantNotice>>(n => n.CurrentAsync(tenant, Ct));
            Assert.Equal(["Wasser wird abgestellt"], current.Select(n => n.Title));
            Assert.Equal([file], current[0].FileIds);
            Assert.True(await IsSharedAsync(file, tenant));
        }

        Assert.Empty(await With<INotices, IReadOnlyList<TenantNotice>>(n => n.CurrentAsync(carla, Ct)));
        Assert.False(await IsSharedAsync(file, carla));
        Assert.Empty(await With<INotices, IReadOnlyList<TenantNotice>>(n => n.CurrentAsync(admin, Ct)));
        Assert.Single(await With<INotices, IReadOnlyList<NoticeRow>>(n => n.ListAsync(admin, Ct)));
        Assert.False((await With<INotices, Result>(n => n.CreateAsync(anna, Input("Fremd"), [], Ct))).Succeeded);
    }

    [Fact]
    public async Task Expired_notice_is_gone_from_the_start_page_but_stays_in_the_archive()
    {
        var input = Input("Ablesetermin");
        input.ValidUntil = DateOnly.FromDateTime(environment.Clock.GetUtcNow().Date).AddDays(5);
        var file = await FileAsync("abgelaufen");
        var id = Validate(await With<INotices, Result>(n => n.CreateAsync(admin, input, [file], Ct)));
        Assert.True(await IsSharedAsync(file, anna));
        Assert.Single(await With<INotices, IReadOnlyList<TenantNotice>>(n => n.CurrentAsync(anna, Ct)));

        environment.Clock.Advance(TimeSpan.FromDays(7));

        Assert.Empty(await With<INotices, IReadOnlyList<TenantNotice>>(n => n.CurrentAsync(anna, Ct)));
        Assert.False(await IsSharedAsync(file, anna));
        var row = Assert.Single(await With<INotices, IReadOnlyList<NoticeRow>>(n => n.ListAsync(admin, Ct)));
        Assert.False(row.IsCurrent);
        Assert.NotNull(await With<INotices, NoticeDetails?>(n => n.GetAsync(admin, id, Ct)));
    }

    [Fact]
    public async Task Acknowledging_is_recorded_once_and_only_for_the_own_property()
    {
        var id = Validate(await With<INotices, Result>(n => n.CreateAsync(admin, Input("Hausordnung", acknowledge: true), [], Ct)));

        Assert.True((await With<INotices, Result>(n => n.AcknowledgeAsync(anna, id, Ct))).Succeeded);
        Assert.True((await With<INotices, Result>(n => n.AcknowledgeAsync(anna, id, Ct))).Succeeded);
        Assert.False((await With<INotices, Result>(n => n.AcknowledgeAsync(carla, id, Ct))).Succeeded);

        var details = (await With<INotices, NoticeDetails?>(n => n.GetAsync(admin, id, Ct)))!;
        Assert.Equal(1, details.Row.Read);
        Assert.Equal(2, details.Row.Recipients);
        Assert.NotNull(details.Readers.Single(r => r.Name.Contains("Schmidt", StringComparison.Ordinal)).ReadAt);
        Assert.Null(details.Readers.Single(r => r.Name.Contains("Weber", StringComparison.Ordinal)).ReadAt);
        Assert.True((await With<INotices, IReadOnlyList<TenantNotice>>(n => n.CurrentAsync(anna, Ct)))[0].Read);
        Assert.False((await With<INotices, IReadOnlyList<TenantNotice>>(n => n.CurrentAsync(ben, Ct)))[0].Read);
    }

    [Fact]
    public async Task Notice_mail_goes_to_the_tenants_of_the_property_exactly_once()
    {
        Validate(await With<INotices, Result>(n => n.CreateAsync(admin, Input("Müllabfuhr"), [], Ct)));

        Assert.Equal(2, await Notify());
        Assert.Equal(0, await Notify());

        var mails = await WithDbAsync(db => db.Set<OutgoingMail>().Where(m => m.Template == "notice").ToListAsync(Ct));
        Assert.Equal(["anna@example.de", "ben@example.de"], mails.SelectMany(m => m.To).Order().ToList());
    }

    [Fact]
    public async Task Messages_are_visible_only_to_the_tenants_of_the_lease_and_mails_contain_no_text()
    {
        var file = await FileAsync("anhang");
        var id = Validate(await With<IMessages, Result>(m => m.StartAsync(anna, new NewThread { Subject = "Schlüssel", Text = "Streng geheimer Text" }, [file], Ct)));

        Assert.Single(await With<IMessages, IReadOnlyList<ThreadRow>>(m => m.ListAsync(anna, Ct)));
        Assert.Empty(await With<IMessages, IReadOnlyList<ThreadRow>>(m => m.ListAsync(ben, Ct)));
        Assert.Null(await With<IMessages, ThreadView?>(m => m.GetAsync(ben, id, Ct)));
        Assert.True(await IsSharedAsync(file, anna));
        Assert.False(await IsSharedAsync(file, ben));

        var unread = Assert.Single(await With<IMessages, IReadOnlyList<ThreadRow>>(m => m.ListAsync(admin, Ct)));
        Assert.True(unread.Unread);
        Assert.False((await With<IMessages, ThreadView?>(m => m.GetAsync(admin, id, Ct)))!.Row.Unread);
        Assert.False((await With<IMessages, IReadOnlyList<ThreadRow>>(m => m.ListAsync(admin, Ct))).Single().Unread);

        Validate(await With<IMessages, Result>(m => m.ReplyAsync(admin, id, "Antwort der Verwaltung", [], Ct)));
        Assert.True((await With<IMessages, IReadOnlyList<ThreadRow>>(m => m.ListAsync(anna, Ct))).Single().Unread);
        Assert.False((await With<IMessages, Result>(m => m.ReplyAsync(ben, id, "Fremd", [], Ct))).Succeeded);

        var view = (await With<IMessages, ThreadView?>(m => m.GetAsync(anna, id, Ct)))!;
        Assert.Equal(["Streng geheimer Text", "Antwort der Verwaltung"], view.Messages.Select(m => m.Text));
        Assert.Equal([true, false], view.Messages.Select(m => m.FromTenant));
        Assert.Equal(2, (await With<IMessages, IReadOnlyList<ThreadView>>(m => m.ForLeaseAsync(admin, view.Row.LeaseId, Ct))).Single().Messages.Count);

        var mails = await WithDbAsync(db => db.Set<OutgoingMail>().Where(m => m.Template == "message-update").ToListAsync(Ct));
        Assert.Equal(2, mails.Count);
        Assert.Contains(mails, m => m.To.Contains("anna@example.de"));
        Assert.DoesNotContain(mails, m => m.Html.Contains("geheim", StringComparison.OrdinalIgnoreCase) || m.Text.Contains("geheim", StringComparison.OrdinalIgnoreCase) || m.Html.Contains("Antwort der Verwaltung", StringComparison.Ordinal));
    }

    private Guid HouseId => house;

    private NoticeInput Input(string title, bool acknowledge = false) =>
        new() { Title = title, Body = "Bitte beachten.", PropertyId = HouseId, RequiresAcknowledgement = acknowledge };

    private Task<int> Notify() => With<NoticeNotifications, int>(n => n.SendAsync(Ct));

    private async Task<Guid> FileAsync(string name)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IFileStore>().SaveAsync(new MemoryStream(TestFiles.Pdf(name)), name + ".pdf", Ct)).Id;
    }

    private async Task<bool> IsSharedAsync(Guid fileId, ClaimsPrincipal user)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        foreach (var share in scope.ServiceProvider.GetServices<IFileShare>())
        {
            if (await share.IsSharedAsync(fileId, user, Ct))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<ClaimsPrincipal> LinkedAccountAsync(Guid personId, string email, string role)
    {
        var id = await AccountAsync(email, role);
        await using var scope = environment.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
        (await db.Set<Person>().SingleAsync(p => p.Id == personId, Ct)).UserId = id;
        await db.SaveChangesAsync(Ct);
        return Account(id, role);
    }

    private async Task<Guid> AccountAsync(string email, string role)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = email, Email = email, DisplayName = email };
        Assert.True((await manager.CreateAsync(user)).Succeeded);
        Assert.True((await manager.AddToRoleAsync(user, role)).Succeeded);
        return user.Id;
    }

    private static ClaimsPrincipal Account(Guid id, string role) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Role, role)], "test"));

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }

    private async Task<TResult> With<TService, TResult>(Func<TService, Task<TResult>> action)
        where TService : notnull
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private async Task<TResult> WithDbAsync<TResult>(Func<MietHahnDbContext, Task<TResult>> action)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<MietHahnDbContext>());
    }
}
