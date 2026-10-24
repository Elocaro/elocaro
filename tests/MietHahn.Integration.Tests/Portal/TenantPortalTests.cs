using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.Common;
using MietHahn.Application.Documents;
using MietHahn.Application.Files;
using MietHahn.Application.Leases;
using MietHahn.Application.People;
using MietHahn.Application.Portal;
using MietHahn.Application.Properties;
using MietHahn.Domain.Communication;
using MietHahn.Domain.Documents;
using MietHahn.Domain.Leases;
using MietHahn.Domain.People;
using MietHahn.Infrastructure.Identity;
using MietHahn.Infrastructure.Persistence;
using MietHahn.Infrastructure.Portal;
using MietHahn.Integration.Tests.Infrastructure;

namespace MietHahn.Integration.Tests.Portal;

public class TenantPortalTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateOnly Today = new(2026, 3, 1);

    private InfrastructureEnvironment environment = null!;
    private Guid house;
    private Guid anna;
    private Guid ben;
    private Guid leaseAnna;
    private Guid leaseBen;
    private ClaimsPrincipal annaAccount = null!;
    private ClaimsPrincipal benAccount = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres);
        await using var scope = environment.Services.CreateAsyncScope();
        var properties = scope.ServiceProvider.GetRequiredService<IPropertyService>();
        house = Validate(await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct));
        var apartment1 = Validate(await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 1" }, Ct));
        var apartment2 = Validate(await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 2" }, Ct));

        var people = scope.ServiceProvider.GetRequiredService<IPersonService>();
        anna = Validate(await people.CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt", Email = "anna@example.de", Salutation = Salutation.Mrs }, null, Ct));
        ben = Validate(await people.CreateAsync(new PersonInput { FirstName = "Ben", LastName = "Weber", Email = "ben@example.de" }, null, Ct));

        var leases = scope.ServiceProvider.GetRequiredService<ILeaseService>();
        leaseAnna = Validate(await leases.CreateAsync(new LeaseCreation { RentalUnitId = apartment1, Start = Today, TenantIds = [anna], BaseRent = 650m, OperatingCostPrepayment = 120m }, Ct));
        leaseBen = Validate(await leases.CreateAsync(new LeaseCreation { RentalUnitId = apartment2, Start = Today, TenantIds = [ben], BaseRent = 700m }, Ct));

        annaAccount = await TenantAccountAsync(anna, "anna@example.de");
        benAccount = await TenantAccountAsync(ben, "ben@example.de");
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Tenant_sees_only_their_own_leases()
    {
        var own = await WithAsync<ITenantPortal, IReadOnlyList<PortalLeaseRow>>(p => p.LeasesAsync(annaAccount, Ct));

        var row = Assert.Single(own);
        Assert.Equal(leaseAnna, row.Id);
        Assert.Equal(770m, row.MonthlyRent);

        var lease = await WithAsync<ITenantPortal, PortalLease?>(p => p.GetLeaseAsync(annaAccount, leaseAnna, Ct));
        Assert.NotNull(lease);
        Assert.Equal(["Anna Schmidt"], lease.Tenants);
        Assert.Equal(2, lease.Components.Count);
    }

    [Fact]
    public async Task A_lease_that_has_not_begun_shows_the_rent_from_its_start()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var properties = scope.ServiceProvider.GetRequiredService<IPropertyService>();
        var unit = Validate(await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 3" }, Ct));
        var people = scope.ServiceProvider.GetRequiredService<IPersonService>();
        var carla = Validate(await people.CreateAsync(new PersonInput { FirstName = "Carla", LastName = "Neu", Email = "carla@example.de" }, null, Ct));
        var start = Today.AddMonths(1);
        var lease = Validate(await scope.ServiceProvider.GetRequiredService<ILeaseService>().CreateAsync(
            new LeaseCreation { RentalUnitId = unit, Start = start, TenantIds = [carla], BaseRent = 800m, OperatingCostPrepayment = 100m }, Ct));
        var account = await TenantAccountAsync(carla, "carla@example.de");

        var row = Assert.Single(await WithAsync<ITenantPortal, IReadOnlyList<PortalLeaseRow>>(p => p.LeasesAsync(account, Ct)));
        Assert.Equal(LeaseStatus.Future, row.Status);
        Assert.Equal(900m, row.MonthlyRent);
        Assert.Equal(start, row.RentDate);

        var detail = await WithAsync<ITenantPortal, PortalLease?>(p => p.GetLeaseAsync(account, lease, Ct));
        Assert.NotNull(detail);
        Assert.Equal(900m, detail.MonthlyRent);
        Assert.Equal(2, detail.Components.Count);
    }

    [Fact]
    public async Task Tenant_sees_the_account_of_their_own_lease_with_charges_and_payments_only()
    {
        await using (var scope = environment.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<MietHahn.Application.Finance.IFinanceSettings>().SaveAsync(
                new MietHahn.Application.Finance.FinanceSettingsInput { AccountingStart = Today }, Ct);
            var accounts = scope.ServiceProvider.GetRequiredService<MietHahn.Application.Finance.ITenantAccounts>();
            await accounts.GenerateChargesAsync(null, Ct);
            Validate(await accounts.RecordPaymentAsync(leaseAnna, new MietHahn.Application.Finance.PaymentInput { Date = Today.AddDays(2), Amount = 300m, Purpose = "Teilzahlung" }, Ct));
        }

        var own = await WithAsync<ITenantPortal, IReadOnlyList<PortalAccount>>(p => p.AccountsAsync(annaAccount, Ct));

        var account = Assert.Single(own);
        Assert.Equal(leaseAnna, account.LeaseId);
        Assert.Equal("Wohnung 1", account.RentalUnit);
        Assert.Equal(Today, account.AccountingStart);
        Assert.Equal(770m - 300m, account.Balance);
        Assert.Contains(account.Entries, e => !e.IsPayment && e.Debit > 0);
        var payment = Assert.Single(account.Entries, e => e.IsPayment);
        Assert.Equal(300m, payment.Credit);
        Assert.Equal(account.Entries.OrderByDescending(e => e.Date).Select(e => e.Date), account.Entries.Select(e => e.Date));

        // The other tenant sees only their own (not yet booked) account, never Anna's payment.
        var other = Assert.Single(await WithAsync<ITenantPortal, IReadOnlyList<PortalAccount>>(p => p.AccountsAsync(benAccount, Ct)));
        Assert.Equal(leaseBen, other.LeaseId);
        Assert.DoesNotContain(other.Entries, e => e.IsPayment);
    }

    [Fact]
    public async Task The_account_is_empty_for_unlinked_accounts_and_other_roles()
    {
        var unlinked = Account(await AccountAsync("frei2@example.de", Roles.Tenant), Roles.Tenant);
        var admin = Account(await AccountAsync("chef2@example.de", Roles.Admin), Roles.Admin);

        Assert.Empty(await WithAsync<ITenantPortal, IReadOnlyList<PortalAccount>>(p => p.AccountsAsync(unlinked, Ct)));
        Assert.Empty(await WithAsync<ITenantPortal, IReadOnlyList<PortalAccount>>(p => p.AccountsAsync(admin, Ct)));
    }

    [Fact]
    public async Task A_foreign_lease_id_is_not_found()
    {
        Assert.Null(await WithAsync<ITenantPortal, PortalLease?>(p => p.GetLeaseAsync(annaAccount, leaseBen, Ct)));
        Assert.Null(await WithAsync<ITenantPortal, PortalLease?>(p => p.GetLeaseAsync(annaAccount, Guid.NewGuid(), Ct)));
    }

    [Fact]
    public async Task Other_roles_and_unlinked_accounts_see_nothing()
    {
        var unlinked = Account(await AccountAsync("frei@example.de", Roles.Tenant), Roles.Tenant);
        var admin = Account(await AccountAsync("chef@example.de", Roles.Admin), Roles.Admin);

        Assert.Empty(await WithAsync<ITenantPortal, IReadOnlyList<PortalLeaseRow>>(p => p.LeasesAsync(unlinked, Ct)));
        Assert.Empty(await WithAsync<ITenantPortal, IReadOnlyList<PortalLeaseRow>>(p => p.LeasesAsync(admin, Ct)));
        Assert.Null(await WithAsync<ITenantPortal, PortalContacts?>(p => p.ContactsAsync(unlinked, Ct)));
        Assert.False((await WithAsync<ITenantPortal, Result>(p => p.RequestContactChangeAsync(unlinked, new ContactChangeInput { Phone = "0221 123" }, Ct))).Succeeded);
    }

    [Fact]
    public async Task Opening_a_document_is_remembered_once_per_account()
    {
        var document = await DocumentAsync("Mietvertrag Anna", new DocumentInput { LeaseId = leaseAnna, SharedWithTenants = true });

        Assert.Empty(await WithAsync<ITenantPortal, IReadOnlySet<Guid>>(p => p.ReadDocumentsAsync(annaAccount, Ct)));
        await With<ITenantPortal>(p => p.MarkReadAsync(annaAccount, document.FileId, Ct));
        await With<ITenantPortal>(p => p.MarkReadAsync(annaAccount, document.FileId, Ct));
        await With<ITenantPortal>(p => p.MarkReadAsync(benAccount, document.FileId, Ct));

        Assert.Equal([document.Id], await WithAsync<ITenantPortal, IReadOnlySet<Guid>>(p => p.ReadDocumentsAsync(annaAccount, Ct)));
        var reads = await WithAsync<IDocumentLibrary, IReadOnlyList<DocumentReadView>>(d => d.ReadsAsync(document.Id, Ct));
        Assert.Equal("Anna Schmidt", Assert.Single(reads).Name);
    }

    [Fact]
    public async Task Contact_change_takes_effect_only_after_approval()
    {
        var input = new ContactChangeInput { Email = "anna.neu@example.de", Phone = "0221 5555", Mobile = null };
        Validate(await WithAsync<ITenantPortal, Result>(p => p.RequestContactChangeAsync(annaAccount, input, Ct)));

        var contacts = await WithAsync<ITenantPortal, PortalContacts?>(p => p.ContactsAsync(annaAccount, Ct));
        Assert.Equal("anna@example.de", contacts!.Current.Email);
        Assert.Equal("anna.neu@example.de", contacts.Pending!.Email);

        var open = await WithAsync<IContactChangeRequests, IReadOnlyList<ContactChangeView>>(r => r.OpenAsync(Ct));
        var request = Assert.Single(open);
        Validate(await WithAsync<IContactChangeRequests, Result>(r => r.ApproveAsync(request.Id, Ct)));

        contacts = await WithAsync<ITenantPortal, PortalContacts?>(p => p.ContactsAsync(annaAccount, Ct));
        Assert.Equal("anna.neu@example.de", contacts!.Current.Email);
        Assert.Equal("0221 5555", contacts.Current.Phone);
        Assert.Null(contacts.Pending);
        Assert.False((await WithAsync<IContactChangeRequests, Result>(r => r.ApproveAsync(request.Id, Ct))).Succeeded);
    }

    [Fact]
    public async Task A_new_contact_request_replaces_the_open_one_and_rejection_changes_nothing()
    {
        Validate(await WithAsync<ITenantPortal, Result>(p => p.RequestContactChangeAsync(annaAccount, new ContactChangeInput { Phone = "1" }, Ct)));
        Validate(await WithAsync<ITenantPortal, Result>(p => p.RequestContactChangeAsync(annaAccount, new ContactChangeInput { Phone = "2" }, Ct)));

        var request = Assert.Single(await WithAsync<IContactChangeRequests, IReadOnlyList<ContactChangeView>>(r => r.OpenAsync(Ct)));
        Assert.Equal("2", request.Phone);
        Validate(await WithAsync<IContactChangeRequests, Result>(r => r.RejectAsync(request.Id, "Nummer unklar", Ct)));

        var contacts = await WithAsync<ITenantPortal, PortalContacts?>(p => p.ContactsAsync(annaAccount, Ct));
        Assert.Null(contacts!.Current.Phone);
        Assert.Empty(await WithAsync<IContactChangeRequests, IReadOnlyList<ContactChangeView>>(r => r.OpenAsync(Ct)));
    }

    [Fact]
    public async Task Newly_shared_document_is_announced_by_mail_exactly_once()
    {
        await DocumentAsync("Hausordnung", new DocumentInput { PropertyId = house, SharedWithTenants = true });
        await DocumentAsync("Interne Notiz", new DocumentInput { PropertyId = house });

        Assert.Equal(2, await With(n => n.SendAsync(Ct)));
        Assert.Equal(0, await With(n => n.SendAsync(Ct)));

        var mails = await WithDbAsync(db => db.Set<OutgoingMail>().Where(m => m.Template == "document-shared").OrderBy(m => m.CreatedAt).ToListAsync(Ct));
        Assert.Equal(2, mails.Count);
        Assert.Equal(["anna@example.de", "ben@example.de"], mails.SelectMany(m => m.To).Order(StringComparer.Ordinal));
        Assert.All(mails, m => Assert.Contains("Hausordnung", m.Html, StringComparison.Ordinal));
        Assert.All(mails, m => Assert.DoesNotContain("Interne Notiz", m.Html, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Documents_that_reach_the_tenant_by_mail_are_not_announced_again()
    {
        await DocumentAsync("Schreiben", new DocumentInput { LeaseId = leaseAnna, SharedWithTenants = true, TenantsInformedElsewhere = true });

        Assert.Equal(0, await With(n => n.SendAsync(Ct)));
        Assert.Empty(await WithDbAsync(db => db.Set<OutgoingMail>().Where(m => m.Template == "document-shared").ToListAsync(Ct)));
    }

    private async Task<DocumentRow> DocumentAsync(string title, DocumentInput data)
    {
        data.Title = title;
        await using var scope = environment.Services.CreateAsyncScope();
        var file = await scope.ServiceProvider.GetRequiredService<IFileStore>().SaveAsync(new MemoryStream(TestFiles.Pdf(title)), "document.pdf", Ct);
        var library = scope.ServiceProvider.GetRequiredService<IDocumentLibrary>();
        var id = Validate(await library.CreateAsync(file.Id, data, Ct));
        return (await library.GetAsync(id, Ct))!.Document;
    }

    private async Task<ClaimsPrincipal> TenantAccountAsync(Guid personId, string email)
    {
        var id = await AccountAsync(email, Roles.Tenant);
        await using var scope = environment.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
        var person = await db.Set<Person>().SingleAsync(p => p.Id == personId, Ct);
        person.UserId = id;
        await db.SaveChangesAsync(Ct);
        return Account(id, Roles.Tenant);
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

    private async Task<TResult> WithAsync<TService, TResult>(Func<TService, Task<TResult>> action)
        where TService : notnull
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private async Task With<TService>(Func<TService, Task> action)
        where TService : notnull
    {
        await using var scope = environment.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private async Task<int> With(Func<DocumentNotifications, Task<int>> action)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<DocumentNotifications>());
    }

    private async Task<TResult> WithDbAsync<TResult>(Func<MietHahnDbContext, Task<TResult>> action)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<MietHahnDbContext>());
    }
}
