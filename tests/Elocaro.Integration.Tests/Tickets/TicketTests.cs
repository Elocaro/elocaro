using System.Security.Claims;
using Elocaro.Application.Accounts;
using Elocaro.Application.Common;
using Elocaro.Application.Files;
using Elocaro.Application.Leases;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Application.Tickets;
using Elocaro.Domain.Communication;
using Elocaro.Domain.People;
using Elocaro.Domain.Tickets;
using Elocaro.Infrastructure.Identity;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Tickets;

public class TicketTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateOnly Today = new(2026, 3, 1);

    private InfrastructureEnvironment environment = null!;
    private Guid house;
    private ClaimsPrincipal anna = null!;
    private ClaimsPrincipal ben = null!;
    private ClaimsPrincipal admin = null!;
    private ClaimsPrincipal caretaker = null!;
    private ClaimsPrincipal otherCaretaker = null!;
    private Guid caretakerPerson;

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
        caretakerPerson = Validate(await people.CreateAsync(new PersonInput { FirstName = "Hans", LastName = "Meier", Email = "hans@example.de", ContactType = ContactType.Caretaker }, null, Ct));
        var otherPerson = Validate(await people.CreateAsync(new PersonInput { FirstName = "Udo", LastName = "Klein", Email = "udo@example.de", ContactType = ContactType.Tradesperson }, null, Ct));

        anna = await LinkedAccountAsync(annaPerson, "anna@example.de", Roles.Tenant);
        ben = await LinkedAccountAsync(benPerson, "ben@example.de", Roles.Tenant);
        caretaker = await LinkedAccountAsync(caretakerPerson, "hans@example.de", Roles.Caretaker);
        otherCaretaker = await LinkedAccountAsync(otherPerson, "udo@example.de", Roles.Tradesperson);
        admin = Account(await AccountAsync("chef@example.de", Roles.Admin), Roles.Admin);
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Tenant_reports_a_problem_with_photos_and_sees_only_their_own_tickets()
    {
        var files = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            files.Add(await FileAsync($"foto{i}"));
        }

        var id = Validate(await With<ITickets, Result>(t => t.ReportAsync(anna, Report("Heizkörper kalt"), files, Ct)));

        var view = (await With<ITickets, TicketView?>(t => t.GetAsync(anna, id, Ct)))!;
        Assert.Equal(3, view.FileIds.Count);
        Assert.Equal(TicketStatus.New, view.Row.Status);
        Assert.Equal("Wohnung 1", view.Row.Unit);
        Assert.Single(await With<ITickets, IReadOnlyList<TicketRow>>(t => t.ListAsync(anna, true, Ct)));
        Assert.Empty(await With<ITickets, IReadOnlyList<TicketRow>>(t => t.ListAsync(ben, true, Ct)));
        Assert.Null(await With<ITickets, TicketView?>(t => t.GetAsync(ben, id, Ct)));
        Assert.Single(await With<ITickets, IReadOnlyList<TicketRow>>(t => t.ListAsync(admin, true, Ct)));
    }

    [Fact]
    public async Task Common_area_reports_have_no_unit_and_too_many_photos_are_refused()
    {
        var common = Report("Licht im Treppenhaus");
        common.CommonArea = true;
        var id = Validate(await With<ITickets, Result>(t => t.ReportAsync(anna, common, [], Ct)));
        Assert.Null((await With<ITickets, TicketView?>(t => t.GetAsync(anna, id, Ct)))!.Row.Unit);

        var many = new List<Guid>();
        for (var i = 0; i < 6; i++)
        {
            many.Add(await FileAsync($"foto{i}"));
        }

        Assert.False((await With<ITickets, Result>(t => t.ReportAsync(anna, Report("Zu viele Fotos"), many, Ct))).Succeeded);
    }

    [Fact]
    public async Task Internal_comments_are_invisible_to_tenant_and_assignee_and_so_are_their_photos()
    {
        var id = Validate(await With<ITickets, Result>(t => t.ReportAsync(anna, Report("Wasserhahn tropft"), [], Ct)));
        Validate(await With<ITickets, Result>(t => t.AssignAsync(admin, id, caretakerPerson, null, Ct)));
        var secret = await FileAsync("intern");
        var open = await FileAsync("oeffentlich");
        Validate(await With<ITickets, Result>(t => t.CommentAsync(admin, id, "Kostenvoranschlag liegt vor", true, [secret], Ct)));
        Validate(await With<ITickets, Result>(t => t.CommentAsync(admin, id, "Der Hausmeister kommt Dienstag", false, [open], Ct)));

        foreach (var viewer in new[] { anna, caretaker })
        {
            var view = (await With<ITickets, TicketView?>(t => t.GetAsync(viewer, id, Ct)))!;
            Assert.Equal(["Der Hausmeister kommt Dienstag"], view.Comments.Select(c => c.Text));
            Assert.True(await IsSharedAsync(open, viewer));
            Assert.False(await IsSharedAsync(secret, viewer));
        }

        var adminView = (await With<ITickets, TicketView?>(t => t.GetAsync(admin, id, Ct)))!;
        Assert.Equal(2, adminView.Comments.Count);
        Assert.False((await With<ITickets, Result>(t => t.CommentAsync(anna, id, "heimlich", true, [], Ct))).Succeeded);
    }

    [Fact]
    public async Task Assignee_sees_only_assigned_tickets_and_may_only_set_progress_and_done()
    {
        var mine = Validate(await With<ITickets, Result>(t => t.ReportAsync(anna, Report("Fenster klemmt"), [], Ct)));
        var other = Validate(await With<ITickets, Result>(t => t.ReportAsync(ben, Report("Tür quietscht"), [], Ct)));
        Validate(await With<ITickets, Result>(t => t.AssignAsync(admin, mine, caretakerPerson, new DateOnly(2026, 3, 5), Ct)));

        Assert.Equal(TicketStatus.Commissioned, (await With<ITickets, TicketView?>(t => t.GetAsync(admin, mine, Ct)))!.Row.Status);
        Assert.Equal([mine], (await With<ITickets, IReadOnlyList<TicketRow>>(t => t.ListAsync(caretaker, true, Ct))).Select(r => r.Id));
        Assert.Null(await With<ITickets, TicketView?>(t => t.GetAsync(caretaker, other, Ct)));
        Assert.Null(await With<ITickets, TicketView?>(t => t.GetAsync(otherCaretaker, mine, Ct)));

        Validate(await With<ITickets, Result>(t => t.SetStatusAsync(caretaker, mine, TicketStatus.InProgress, Ct)));
        Assert.False((await With<ITickets, Result>(t => t.SetStatusAsync(caretaker, mine, TicketStatus.Closed, Ct))).Succeeded);
        Assert.False((await With<ITickets, Result>(t => t.SetStatusAsync(anna, mine, TicketStatus.Done, Ct))).Succeeded);
        Assert.False((await With<ITickets, Result>(t => t.SetStatusAsync(caretaker, other, TicketStatus.Done, Ct))).Succeeded);
    }

    [Fact]
    public async Task Only_caretakers_and_tradespeople_with_an_account_can_be_assigned()
    {
        var id = Validate(await With<ITickets, Result>(t => t.ReportAsync(anna, Report("Klingel defekt"), [], Ct)));
        var tenant = await With<ITickets, IReadOnlyList<Elocaro.Application.Documents.ReferenceOption>>(t => t.AssigneesAsync(Ct));

        Assert.Equal(["Hans Meier", "Udo Klein"], tenant.Select(o => o.Name).Order(StringComparer.Ordinal));
        var tenantPerson = await WithDbAsync(db => db.Set<Person>().Where(p => p.LastName == "Weber").Select(p => p.Id).SingleAsync(Ct));
        Assert.False((await With<ITickets, Result>(t => t.AssignAsync(admin, id, tenantPerson, null, Ct))).Succeeded);
        Assert.False((await With<ITickets, Result>(t => t.AssignAsync(anna, id, caretakerPerson, null, Ct))).Succeeded);
    }

    [Fact]
    public async Task Status_changes_notify_tenant_and_assignee_but_not_the_actor()
    {
        var id = Validate(await With<ITickets, Result>(t => t.ReportAsync(anna, Report("Rauchmelder piept"), [], Ct)));
        Validate(await With<ITickets, Result>(t => t.AssignAsync(admin, id, caretakerPerson, null, Ct)));
        Validate(await With<ITickets, Result>(t => t.SetStatusAsync(caretaker, id, TicketStatus.Done, Ct)));

        var mails = await WithDbAsync(db => db.Set<OutgoingMail>().Where(m => m.Template == "ticket-update").OrderBy(m => m.CreatedAt).ToListAsync(Ct));
        Assert.Contains(mails, m => m.To.Contains("anna@example.de") && m.Html.Contains("erledigt", StringComparison.Ordinal));
        Assert.DoesNotContain(mails, m => m.To.Contains("hans@example.de") && m.Html.Contains("erledigt", StringComparison.Ordinal));
        Assert.Contains(mails, m => m.To.Contains("hans@example.de") && m.Html.Contains("Auftrag", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Closed_tickets_cannot_be_commented_or_reopened()
    {
        var id = Validate(await With<ITickets, Result>(t => t.ReportAsync(anna, Report("Dichtung kaputt"), [], Ct)));
        Validate(await With<ITickets, Result>(t => t.SetStatusAsync(admin, id, TicketStatus.Closed, Ct)));

        Assert.False((await With<ITickets, Result>(t => t.CommentAsync(anna, id, "noch etwas", false, [], Ct))).Succeeded);
        Assert.False((await With<ITickets, Result>(t => t.SetStatusAsync(admin, id, TicketStatus.InProgress, Ct))).Succeeded);
        Assert.Empty(await With<ITickets, IReadOnlyList<TicketCost>>(t => t.CostsAsync(Ct)));
    }

    [Fact]
    public async Task Admin_creates_a_ticket_without_reporter()
    {
        var input = new AdminTicketInput { Title = "Rauchmelder prüfen", Description = "Jährliche Prüfung", PropertyId = house, AssigneeId = caretakerPerson };
        var id = Validate(await With<ITickets, Result>(t => t.CreateAsync(admin, input, [], Ct)));

        var view = (await With<ITickets, TicketView?>(t => t.GetAsync(admin, id, Ct)))!;
        Assert.Null(view.Row.Reporter);
        Assert.Equal("Hans Meier", view.Row.Assignee);
        Assert.False((await With<ITickets, Result>(t => t.CreateAsync(anna, input, [], Ct))).Succeeded);
    }

    private static TicketReport Report(string title) => new() { Title = title, Description = "Bitte ansehen.", Category = TicketCategory.Heating };

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
        var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();
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

    private async Task<TResult> WithDbAsync<TResult>(Func<ElocaroDbContext, Task<TResult>> action)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ElocaroDbContext>());
    }
}
