using System.Security.Claims;
using Elocaro.Application.Accounts;
using Elocaro.Application.Common;
using Elocaro.Application.Files;
using Elocaro.Application.Leases;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Application.Tickets;
using Elocaro.Application.Todos;
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

public class ServiceWorkTests(PostgresFixture postgres) : IAsyncLifetime
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
    public async Task Contractor_link_shows_one_ticket_without_internal_data_and_lets_the_holder_work()
    {
        var file = await FileAsync("foto");
        var id = Validate(await With<ITickets, Result>(t => t.ReportAsync(anna, new TicketReport { Title = "Wasserhahn", Description = "Tropft", AccessTimes = "nur abends" }, [file], Ct)));
        Validate(await With<ITickets, Result>(t => t.CommentAsync(admin, id, "Intern: teuer", true, [], Ct)));
        Validate(await With<ITickets, Result>(t => t.CommentAsync(admin, id, "Bitte Montag", false, [], Ct)));
        var other = Validate(await With<ITickets, Result>(t => t.ReportAsync(ben, new TicketReport { Title = "Anderes", Description = "x" }, [], Ct)));

        var created = await With<IContractorLinks, LinkCreation>(l => l.CreateAsync(admin, id, "Firma Müller", 7, Ct));
        Validate(created.Result);
        var token = created.Url![(created.Url!.LastIndexOf('/') + 1)..];

        var view = (await With<IContractorLinks, ContractorTicket?>(l => l.GetAsync(token, Ct)))!;
        Assert.Equal("Wasserhahn", view.Title);
        Assert.Equal(["Bitte Montag"], view.Comments.Select(c => c.Text));
        Assert.Equal([file], view.FileIds);
        Assert.Equal(file, await With<IContractorLinks, Guid?>(l => l.FileOfAsync(token, file, Ct)));
        Assert.Null(await With<IContractorLinks, Guid?>(l => l.FileOfAsync(token, Guid.NewGuid(), Ct)));
        Assert.Null(await With<IContractorLinks, ContractorTicket?>(l => l.GetAsync(token + "x", Ct)));

        var photo = await FileAsync("nachher");
        Validate(await With<IContractorLinks, Result>(l => l.CommentAsync(token, "Fertig repariert", [photo], Ct)));
        Validate(await With<IContractorLinks, Result>(l => l.SetStatusAsync(token, TicketStatus.InProgress, Ct)));
        Validate(await With<IContractorLinks, Result>(l => l.AddWorkAsync(token, new WorkInput { Minutes = 90, Material = "Dichtung" }, Ct)));
        Assert.False((await With<IContractorLinks, Result>(l => l.AddWorkAsync(token, new WorkInput(), Ct))).Succeeded);
        Assert.False((await With<IContractorLinks, Result>(l => l.SetStatusAsync(token, TicketStatus.Closed, Ct))).Succeeded);
        var invoice = await FileAsync("rechnung");
        Validate(await With<IContractorLinks, Result>(l => l.AddInvoiceAsync(token, invoice, Ct)));
        Assert.Equal(invoice, await With<IContractorLinks, Guid?>(l => l.FileOfAsync(token, invoice, Ct)));

        var after = (await With<IContractorLinks, ContractorTicket?>(l => l.GetAsync(token, Ct)))!;
        Assert.Equal(TicketStatus.InProgress, after.Status);
        Assert.Equal(90, after.Work.Work.Single().Minutes);
        Assert.Single(after.Work.Invoices);
        Assert.Equal(2, after.Comments.Count);

        var adminWork = (await With<ITicketWork, TicketWorkView?>(w => w.GetAsync(admin, id, Ct)))!;
        Assert.Equal("Firma Müller", adminWork.Work.Single().Author);
        Assert.Null(await With<ITicketWork, TicketWorkView?>(w => w.GetAsync(anna, id, Ct)));
        Assert.Equal(TicketStatus.InProgress, (await With<ITickets, TicketView?>(t => t.GetAsync(admin, id, Ct)))!.Row.Status);
        Assert.Equal(TicketStatus.New, (await With<ITickets, TicketView?>(t => t.GetAsync(admin, other, Ct)))!.Row.Status);
        Assert.False((await With<IContractorLinks, LinkCreation>(l => l.CreateAsync(anna, id, "Mieter", 7, Ct))).Result.Succeeded);
    }

    [Fact]
    public async Task Contractor_link_stops_working_when_expired_revoked_or_the_ticket_is_closed()
    {
        var id = Validate(await With<ITickets, Result>(t => t.ReportAsync(anna, new TicketReport { Title = "A", Description = "a" }, [], Ct)));
        var expiring = await LinkAsync(id, "kurz", 3);
        var revoked = await LinkAsync(id, "weg", 30);
        var closing = await LinkAsync(id, "zu", 30);

        var row = (await With<IContractorLinks, IReadOnlyList<LinkRow>>(l => l.ListAsync(admin, id, Ct))).Single(r => r.Recipient == "weg");
        Validate(await With<IContractorLinks, Result>(l => l.RevokeAsync(admin, row.Id, Ct)));
        Assert.Null(await With<IContractorLinks, ContractorTicket?>(l => l.GetAsync(revoked, Ct)));
        Assert.False((await With<IContractorLinks, Result>(l => l.CommentAsync(revoked, "hallo", [], Ct))).Succeeded);

        environment.Clock.Advance(TimeSpan.FromDays(4));
        Assert.Null(await With<IContractorLinks, ContractorTicket?>(l => l.GetAsync(expiring, Ct)));
        Assert.False(await With<IContractorLinks, bool>(l => l.IsValidAsync(expiring, Ct)));
        Assert.NotNull(await With<IContractorLinks, ContractorTicket?>(l => l.GetAsync(closing, Ct)));

        Validate(await With<ITickets, Result>(t => t.SetStatusAsync(admin, id, TicketStatus.Closed, Ct)));
        Assert.Null(await With<IContractorLinks, ContractorTicket?>(l => l.GetAsync(closing, Ct)));
        Assert.False((await With<IContractorLinks, LinkCreation>(l => l.CreateAsync(admin, id, "neu", 7, Ct))).Result.Succeeded);
    }

    [Fact]
    public async Task Assignee_with_an_account_books_work_and_invoices_on_their_own_tickets_only()
    {
        var mine = Validate(await With<ITickets, Result>(t => t.ReportAsync(anna, new TicketReport { Title = "Meins", Description = "a" }, [], Ct)));
        var foreign = Validate(await With<ITickets, Result>(t => t.ReportAsync(ben, new TicketReport { Title = "Fremd", Description = "b" }, [], Ct)));
        Validate(await With<ITickets, Result>(t => t.AssignAsync(admin, mine, caretakerPerson, null, Ct)));

        Validate(await With<ITicketWork, Result>(w => w.AddWorkAsync(caretaker, mine, new WorkInput { Minutes = 30, Note = "Geprüft" }, Ct)));
        var invoice = await FileAsync("rg");
        Validate(await With<ITicketWork, Result>(w => w.AddInvoiceAsync(caretaker, mine, invoice, Ct)));
        Assert.True(await IsSharedAsync(invoice, caretaker));
        Assert.False(await IsSharedAsync(invoice, anna));
        Assert.False((await With<ITicketWork, Result>(w => w.AddWorkAsync(caretaker, foreign, new WorkInput { Minutes = 5 }, Ct))).Succeeded);
        Assert.False((await With<ITicketWork, Result>(w => w.AddWorkAsync(otherCaretaker, mine, new WorkInput { Minutes = 5 }, Ct))).Succeeded);
        Assert.False((await With<ITicketWork, Result>(w => w.AddWorkAsync(anna, mine, new WorkInput { Minutes = 5 }, Ct))).Succeeded);
        Assert.False((await With<ITicketWork, Result>(w => w.AddWorkAsync(caretaker, mine, new WorkInput { Minutes = 5, WorkedOn = Today.AddYears(10) }, Ct))).Succeeded);

        var view = (await With<ITicketWork, TicketWorkView?>(w => w.GetAsync(admin, mine, Ct)))!;
        Assert.Single(view.Work);
        Assert.Single(view.Invoices);
        Assert.False(view.CanAdd);
    }

    [Fact]
    public async Task Care_routines_are_ticked_off_by_the_caretaker_of_the_property_as_proof()
    {
        var routine = Validate(await With<ICareRoutines, Result>(r => r.CreateAsync(admin, new RoutineInput { PropertyId = house, Title = "Treppenhaus reinigen", Interval = CareInterval.Weekly }, Ct)));
        Assert.False((await With<ICareRoutines, Result>(r => r.CreateAsync(caretaker, new RoutineInput { PropertyId = house, Title = "x" }, Ct))).Succeeded);

        Assert.Empty(await With<ICareRoutines, IReadOnlyList<RoutineRow>>(r => r.ListAsync(caretaker, false, Ct)));
        Validate(await With<IPropertyAssignments, Result>(a => a.SetAsync(AccountId(caretaker), [house], Ct)));
        var due = Assert.Single(await With<ICareRoutines, IReadOnlyList<RoutineRow>>(r => r.ListAsync(caretaker, false, Ct)));
        Assert.True(due.Due);
        Assert.Null(due.LastDone);

        Validate(await With<ICareRoutines, Result>(r => r.CheckAsync(caretaker, routine, null, "Alles sauber", null, Ct)));
        var done = Assert.Single(await With<ICareRoutines, IReadOnlyList<RoutineRow>>(r => r.ListAsync(caretaker, false, Ct)));
        Assert.False(done.Due);
        Assert.Equal(1, done.ChecksThisYear);
        Assert.NotNull(done.NextDue);
        Assert.Equal("Alles sauber", (await With<ICareRoutines, IReadOnlyList<CheckRow>>(r => r.ChecksAsync(admin, routine, null, Ct))).Single().Note);
        Assert.False((await With<ICareRoutines, Result>(r => r.CheckAsync(caretaker, routine, DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1), null, null, Ct))).Succeeded);

        environment.Clock.Advance(TimeSpan.FromDays(8));
        Assert.True((await With<ICareRoutines, IReadOnlyList<RoutineRow>>(r => r.ListAsync(caretaker, false, Ct))).Single().Due);

        Assert.False((await With<ICareRoutines, Result>(r => r.CheckAsync(otherCaretaker, routine, null, null, null, Ct))).Succeeded);
        Assert.Empty(await With<ICareRoutines, IReadOnlyList<CheckRow>>(r => r.ChecksAsync(otherCaretaker, routine, null, Ct)));
        Validate(await With<ICareRoutines, Result>(r => r.DeactivateAsync(admin, routine, Ct)));
        Assert.Empty(await With<ICareRoutines, IReadOnlyList<RoutineRow>>(r => r.ListAsync(caretaker, false, Ct)));
        Assert.Single(await With<ICareRoutines, IReadOnlyList<RoutineRow>>(r => r.ListAsync(admin, true, Ct)));
    }

    [Fact]
    public async Task Receipt_shows_the_work_reported_on_its_ticket_to_the_administration_only()
    {
        var id = Validate(await With<ITickets, Result>(t => t.ReportAsync(anna, new TicketReport { Title = "Wasserhahn", Description = "Tropft" }, [], Ct)));
        Validate(await With<ITickets, Result>(t => t.AssignAsync(admin, id, caretakerPerson, null, Ct)));
        Validate(await With<ITicketWork, Result>(w => w.AddWorkAsync(caretaker, id, new WorkInput { Minutes = 90, Material = "Dichtung" }, Ct)));
        Validate(await With<ITicketWork, Result>(w => w.AddWorkAsync(caretaker, id, new WorkInput { Minutes = 30, Note = "Probe" }, Ct)));
        var receipt = Validate(await With<Elocaro.Application.Receipts.IReceiptService, Result>(r => r.CreateAsync(
            new Elocaro.Application.Receipts.ReceiptInput { SupplierName = "Firma Müller", InvoiceDate = new DateOnly(2026, 4, 30), Gross = 119m }, [], null, Ct)));
        Assert.Empty(await With<ITicketWork, IReadOnlyList<ReceiptTicket>>(w => w.ForReceiptAsync(admin, receipt, Ct)));

        Validate(await With<ITickets, Result>(t => t.LinkReceiptAsync(admin, id, receipt, Ct)));

        var linked = Assert.Single(await With<ITicketWork, IReadOnlyList<ReceiptTicket>>(w => w.ForReceiptAsync(admin, receipt, Ct)));
        Assert.Equal(("Wasserhahn", 120), (linked.Title, linked.Minutes));
        Assert.Equal(2, linked.Work.Count);
        Assert.Empty(await With<ITicketWork, IReadOnlyList<ReceiptTicket>>(w => w.ForReceiptAsync(anna, receipt, Ct)));
    }

    [Fact]
    public async Task A_tick_can_carry_a_photo_as_proof()
    {
        var routine = Validate(await With<ICareRoutines, Result>(r => r.CreateAsync(admin, new RoutineInput { PropertyId = house, Title = "Hof kehren", Interval = CareInterval.Weekly }, Ct)));
        var photo = await FileAsync("hof");

        Validate(await With<ICareRoutines, Result>(r => r.CheckAsync(admin, routine, null, null, photo, Ct)));
        Validate(await With<ICareRoutines, Result>(r => r.CheckAsync(admin, routine, null, null, null, Ct)));

        var checks = await With<ICareRoutines, IReadOnlyList<CheckRow>>(r => r.ChecksAsync(admin, routine, null, Ct));
        Assert.Equal(2, checks.Count);
        Assert.Equal(photo, Assert.Single(checks, c => c.FileId is not null).FileId);
    }

    [Fact]
    public async Task Overdue_care_routines_show_up_on_the_dashboard_of_the_administration()
    {
        var routine = Validate(await With<ICareRoutines, Result>(r => r.CreateAsync(admin, new RoutineInput { PropertyId = house, Title = "Treppenhaus reinigen", Interval = CareInterval.Weekly }, Ct)));
        Assert.Empty(await TodosAsync(admin));

        environment.Clock.Advance(TimeSpan.FromDays(9));
        var todo = Assert.Single(await TodosAsync(admin));
        Assert.Contains("Treppenhaus reinigen", todo.Title);
        Assert.Empty(await TodosAsync(caretaker));

        Validate(await With<ICareRoutines, Result>(r => r.CheckAsync(admin, routine, null, null, null, Ct)));
        Assert.Empty(await TodosAsync(admin));
    }

    [Fact]
    public void Maintenance_task_prefills_the_ticket_form()
    {
        var task = new Elocaro.Application.Deadlines.TaskListItem(Guid.NewGuid(), "Heizungswartung", Elocaro.Domain.Deadlines.TaskKind.HeatingService, house, "Haus", new DateOnly(2027, 3, 1), 12, null, true);
        var input = new AdminTicketInput();

        MaintenanceTicket.Prefill(input, task);

        Assert.Equal("Heizungswartung", input.Title);
        Assert.Equal(TicketCategory.Heating, input.Category);
        Assert.Equal(house, input.PropertyId);
        Assert.Contains("01.03.2027", input.Description);
    }

    private async Task<IReadOnlyList<TodoItem>> TodosAsync(ClaimsPrincipal user)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var items = new List<TodoItem>();
        foreach (var source in scope.ServiceProvider.GetServices<ITodoSource>())
        {
            items.AddRange(await source.TodosAsync(user, Ct));
        }

        return items.Where(t => t.Title.StartsWith("Hausmeisteraufgabe", StringComparison.Ordinal)).ToList();
    }

    private async Task<string> LinkAsync(Guid ticket, string recipient, int days)
    {
        var created = await With<IContractorLinks, LinkCreation>(l => l.CreateAsync(admin, ticket, recipient, days, Ct));
        Validate(created.Result);
        return created.Url![(created.Url!.LastIndexOf('/') + 1)..];
    }

    private static Guid AccountId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

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
