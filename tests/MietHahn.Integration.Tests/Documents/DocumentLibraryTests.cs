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
using MietHahn.Application.Properties;
using MietHahn.Application.Todos;
using MietHahn.Domain.Documents;
using MietHahn.Domain.People;
using MietHahn.Infrastructure.Identity;
using MietHahn.Infrastructure.Persistence;
using MietHahn.Integration.Tests.Infrastructure;

namespace MietHahn.Integration.Tests.Documents;

public class DocumentLibraryTests(PostgresFixture postgres) : IAsyncLifetime
{
    // Startzeit der Testumgebung: 01.03.2026
    private static readonly DateOnly Today = new(2026, 3, 1);

    private InfrastructureEnvironment environment = null!;
    private Guid house;
    private Guid otherHouse;
    private Guid apartment1;
    private Guid apartment2;
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
        otherHouse = Validate(await properties.CreateAsync(new PropertyInput { Name = "Birkenallee 9", Street = "Birkenallee 9", PostalCode = "50668", City = "Köln" }, Ct));
        apartment1 = Validate(await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 1" }, Ct));
        apartment2 = Validate(await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 2" }, Ct));

        var people = scope.ServiceProvider.GetRequiredService<IPersonService>();
        anna = Validate(await people.CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt" }, null, Ct));
        ben = Validate(await people.CreateAsync(new PersonInput { FirstName = "Ben", LastName = "Weber" }, null, Ct));

        var leases = scope.ServiceProvider.GetRequiredService<ILeaseService>();
        leaseAnna = Validate(await leases.CreateAsync(new LeaseCreation { RentalUnitId = apartment1, Start = Today, TenantIds = [anna], BaseRent = 650m }, Ct));
        leaseBen = Validate(await leases.CreateAsync(new LeaseCreation { RentalUnitId = apartment2, Start = Today, TenantIds = [ben], BaseRent = 700m }, Ct));

        annaAccount = await TenantAccountAsync(anna, "anna@example.de");
        benAccount = await TenantAccountAsync(ben, "ben@example.de");
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Documents_shared_with_tenants_are_visible_only_to_tenants_of_the_lease_or_property()
    {
        var lease = await DocumentAsync("Mietvertrag Anna", new DocumentInput { LeaseId = leaseAnna, SharedWithTenants = true });
        var houseRules = await DocumentAsync("Hausordnung", new DocumentInput { PropertyId = house, SharedWithTenants = true });
        var log = await DocumentAsync("Übergabeprotokoll Wohnung 1", new DocumentInput { UnitId = apartment1, SharedWithTenants = true });
        await DocumentAsync("Interne Notiz", new DocumentInput { LeaseId = leaseAnna });
        await DocumentAsync("Hausordnung Birkenallee", new DocumentInput { PropertyId = otherHouse, SharedWithTenants = true });

        Assert.Equal(
            ["Hausordnung", "Mietvertrag Anna", "Übergabeprotokoll Wohnung 1"],
            (await WithAsync(a => a.SharedAsync(annaAccount, null, Ct))).Select(d => d.Title).Order(StringComparer.Ordinal));
        Assert.Equal(["Hausordnung"], (await WithAsync(a => a.SharedAsync(benAccount, null, Ct))).Select(d => d.Title));

        // Gleiche Regel für den Dateiabruf über /dateien/{id}
        Assert.True(await IsSharedAsync(lease.FileId, annaAccount));
        Assert.False(await IsSharedAsync(lease.FileId, benAccount));
        Assert.True(await IsSharedAsync(houseRules.FileId, benAccount));
        Assert.False(await IsSharedAsync(log.FileId, benAccount));
    }

    [Fact]
    public async Task After_move_out_the_tenant_only_sees_documents_of_their_lease()
    {
        await DocumentAsync("Hausordnung", new DocumentInput { PropertyId = house, SharedWithTenants = true });
        await DocumentAsync("Mietvertrag Anna", new DocumentInput { LeaseId = leaseAnna, SharedWithTenants = true });
        Validate(await With<ILeaseService, Result>(v => v.MoveOutAsync(leaseAnna, new MoveOutInput { End = new DateOnly(2026, 6, 30) }, Ct)));

        environment.Clock.SetUtcNow(new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero));

        Assert.Equal(["Mietvertrag Anna"], (await WithAsync(a => a.SharedAsync(annaAccount, null, Ct))).Select(d => d.Title));
    }

    [Fact]
    public async Task Caretaker_sees_only_shared_documents_of_assigned_properties()
    {
        var caretakerId = await AccountAsync("hm@example.de", Roles.Caretaker);
        Validate(await With<IPropertyAssignments, Result>(z => z.SetAsync(caretakerId, [house], Ct)));
        var caretaker = Account(caretakerId, Roles.Caretaker);
        var maintenance = await DocumentAsync("Wartungsvertrag Heizung", new DocumentInput { PropertyId = house, SharedWithCaretakers = true });
        await DocumentAsync("Grundriss Wohnung 2", new DocumentInput { UnitId = apartment2, SharedWithCaretakers = true });
        await DocumentAsync("Hausordnung", new DocumentInput { PropertyId = house, SharedWithTenants = true });
        await DocumentAsync("Wartung Birkenallee", new DocumentInput { PropertyId = otherHouse, SharedWithCaretakers = true });

        Assert.Equal(
            ["Grundriss Wohnung 2", "Wartungsvertrag Heizung"],
            (await WithAsync(a => a.SharedAsync(caretaker, house, Ct))).Select(d => d.Title).Order(StringComparer.Ordinal));
        Assert.Empty(await WithAsync(a => a.SharedAsync(caretaker, otherHouse, Ct)));
        Assert.True(await IsSharedAsync(maintenance.FileId, caretaker));
        Assert.Equal(["Hausordnung"], (await WithAsync(a => a.SharedAsync(annaAccount, null, Ct))).Select(d => d.Title));
    }

    [Fact]
    public async Task Search_by_title_tag_file_name_and_reference()
    {
        await DocumentAsync("Gasrechnung 2025", new DocumentInput { PropertyId = house, Category = DocumentCategory.Invoice, Tags = "Heizung, Gas" }, "stadtwerke_2025.pdf");
        await DocumentAsync("Mietvertrag", new DocumentInput { LeaseId = leaseAnna, Category = DocumentCategory.Lease });
        await DocumentAsync("Personalausweis Kopie", new DocumentInput { PersonId = ben, Note = "Nach Einzug vernichten" });

        Assert.Equal(["Gasrechnung 2025"], await TitlesAsync(new DocumentQuery("heizung")));
        Assert.Equal(["Gasrechnung 2025"], await TitlesAsync(new DocumentQuery("STADTWERKE")));
        Assert.Equal(["Personalausweis Kopie"], await TitlesAsync(new DocumentQuery("vernichten")));
        Assert.Equal(["Mietvertrag"], await TitlesAsync(new DocumentQuery(Category: DocumentCategory.Lease)));
        Assert.Empty(await TitlesAsync(new DocumentQuery("100%")));

        // Dokumente eines Vertrags gehören auch zur Einheit und zum Objekt; Personen sehen die ihrer Verträge.
        Assert.Equal(2, (await TitlesAsync(new DocumentQuery(PropertyId: house))).Count);
        Assert.Equal(["Mietvertrag"], await TitlesAsync(new DocumentQuery(UnitId: apartment1)));
        Assert.Equal(["Mietvertrag"], await TitlesAsync(new DocumentQuery(PersonId: anna)));
        Assert.Equal(["Personalausweis Kopie"], await TitlesAsync(new DocumentQuery(PersonId: ben)));
    }

    [Fact]
    public async Task Reference_is_required_and_tags_are_cleaned()
    {
        var withoutReference = await CreateAsync("Lose Datei", new DocumentInput());
        Assert.False(withoutReference.Succeeded);
        Assert.Contains("mindestens einen Bezug", withoutReference.Errors.Single(), StringComparison.Ordinal);

        var unknown = await CreateAsync("Falsch", new DocumentInput { PropertyId = Guid.CreateVersion7() });
        Assert.False(unknown.Succeeded);

        var document = await DocumentAsync("Versicherungspolice", new DocumentInput { PropertyId = house, Tags = " Gebäude ; gebäude, Sturm,, " });
        Assert.Equal(["Gebäude", "Sturm"], document.Tags);

        var details = (await WithAsync(a => a.GetAsync(document.Id, Ct)))!;
        details.Data.Title = "Gebäudeversicherung";
        details.Data.LeaseId = leaseBen;
        Validate(await WithAsync(a => a.UpdateAsync(document.Id, details.Data, Ct)));
        var changed = (await WithAsync(a => a.GetAsync(document.Id, Ct)))!;
        Assert.Equal("Gebäudeversicherung", changed.Document.Title);
        Assert.Equal("Gebäude, Sturm", changed.Data.Tags);
        Assert.Contains("Lindenweg 3", changed.Document.Reference, StringComparison.Ordinal);
        Assert.Contains("Vertrag Wohnung 2", changed.Document.Reference, StringComparison.Ordinal);

        Validate(await WithAsync(a => a.DeleteAsync(document.Id, Ct)));
        Assert.Null(await WithAsync(a => a.GetAsync(document.Id, Ct)));
    }

    [Fact]
    public async Task Expiring_documents_appear_on_the_dashboard()
    {
        await DocumentAsync("Energieausweis", new DocumentInput { PropertyId = house, Category = DocumentCategory.EnergyCertificate, ExpiresOn = Today.AddDays(30) });
        await DocumentAsync("Alte Police", new DocumentInput { PropertyId = house, ExpiresOn = Today.AddDays(-1) });
        await DocumentAsync("Neue Police", new DocumentInput { PropertyId = house, ExpiresOn = Today.AddYears(1) });

        await using var scope = environment.Services.CreateAsyncScope();
        var source = scope.ServiceProvider.GetServices<ITodoSource>().Single(q => q.GetType().Name == "DocumentExpiryTodos");
        var todos = await source.TodosAsync(Account(Guid.CreateVersion7(), Roles.Admin), Ct);

        Assert.Equal(["Abgelaufen: Alte Police", "Läuft ab: Energieausweis"], todos.Select(a => a.Title));
        Assert.Equal(Urgency.Urgent, todos[0].Urgency);
        Assert.Empty(await source.TodosAsync(benAccount, Ct));
    }

    private async Task<List<string>> TitlesAsync(DocumentQuery query) => (await WithAsync(a => a.SearchAsync(query, Ct))).Select(d => d.Title).ToList();

    private async Task<DocumentRow> DocumentAsync(string title, DocumentInput data, string fileName = "document.pdf")
    {
        var id = Validate(await CreateAsync(title, data, fileName));
        return (await WithAsync(a => a.GetAsync(id, Ct)))!.Document;
    }

    private async Task<Result> CreateAsync(string title, DocumentInput data, string fileName = "document.pdf")
    {
        data.Title = title;
        await using var scope = environment.Services.CreateAsyncScope();
        var file = await scope.ServiceProvider.GetRequiredService<IFileStore>().SaveAsync(new MemoryStream(TestFiles.Pdf(title)), fileName, Ct);
        return await scope.ServiceProvider.GetRequiredService<IDocumentLibrary>().CreateAsync(file.Id, data, Ct);
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

    private Task<T> WithAsync<T>(Func<IDocumentLibrary, Task<T>> action) => With<IDocumentLibrary, T>(action);

    private async Task<T> With<TService, T>(Func<TService, Task<T>> action)
        where TService : notnull
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
