using Elocaro.Application.Audit;
using Elocaro.Application.Common;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Application.Prospects;
using Elocaro.Domain.Prospects;
using Elocaro.Infrastructure.Audit;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Infrastructure.Prospects;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Prospects;

public class ProspectTests(PostgresFixture postgres) : IAsyncLifetime
{
    private InfrastructureEnvironment environment = null!;
    private Guid unit;
    private Guid otherUnit;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres);
        environment.Clock.SetUtcNow(new DateTimeOffset(2026, 11, 16, 8, 0, 0, TimeSpan.Zero));
        var property = Validate(await With<IPropertyService, Result>(s => s.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct)));
        unit = Validate(await With<IPropertyService, Result>(s => s.CreateUnitAsync(property, new UnitInput { Name = "Wohnung 1" }, Ct)));
        otherUnit = Validate(await With<IPropertyService, Result>(s => s.CreateUnitAsync(property, new UnitInput { Name = "Wohnung 2" }, Ct)));
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    private ProspectInput Input(string name = "Anna Schütz", Guid? unitId = null) => new() { RentalUnitId = unitId ?? unit, Name = name, Email = "anna@example.org", Phone = "0221 123" };

    [Fact]
    public async Task A_prospect_is_listed_with_unit_and_status_and_can_be_changed()
    {
        var id = Validate(await With<IProspects, Result>(s => s.CreateAsync(Input(), Ct)));

        var row = Assert.Single(await With<IProspects, IReadOnlyList<ProspectRow>>(s => s.ListAsync(null, Ct)));
        Assert.Equal(("Wohnung 1", "Lindenweg 3", ProspectStatus.Inquiry), (row.RentalUnit, row.Property, row.Status));

        var input = (await With<IProspects, ProspectInput?>(s => s.GetAsync(id, Ct)))!;
        input.ViewingOn = new DateOnly(2026, 11, 20);
        Validate(await With<IProspects, Result>(s => s.UpdateAsync(id, input, Ct)));
        Validate(await With<IProspects, Result>(s => s.SetStatusAsync(id, ProspectStatus.Viewing, Ct)));

        row = Assert.Single(await With<IProspects, IReadOnlyList<ProspectRow>>(s => s.ListAsync(null, Ct)));
        Assert.Equal((ProspectStatus.Viewing, new DateOnly(2026, 11, 20), new DateOnly(2027, 5, 16)), (row.Status, row.ViewingOn, row.DeleteOn));
        Assert.False((await With<IProspects, Result>(s => s.CreateAsync(Input(" "), Ct))).Succeeded);
    }

    [Fact]
    public async Task A_rejection_sets_the_deletion_day_six_months_later()
    {
        var id = Validate(await With<IProspects, Result>(s => s.CreateAsync(Input(), Ct)));

        Validate(await With<IProspects, Result>(s => s.SetStatusAsync(id, ProspectStatus.Declined, Ct)));

        var row = Assert.Single(await With<IProspects, IReadOnlyList<ProspectRow>>(s => s.ListAsync(null, Ct)));
        Assert.Equal(new DateOnly(2027, 5, 16), row.DeleteOn);

        Validate(await With<IProspects, Result>(s => s.SetStatusAsync(id, ProspectStatus.Inquiry, Ct)));
        // Without a decision the day is six months after the last change.
        Assert.Equal(new DateOnly(2027, 5, 16), Assert.Single(await With<IProspects, IReadOnlyList<ProspectRow>>(s => s.ListAsync(null, Ct))).DeleteOn);
    }

    [Fact]
    public async Task Adopting_a_prospect_creates_the_person_and_accepts_the_prospect()
    {
        var id = Validate(await With<IProspects, Result>(s => s.CreateAsync(Input("Anna Maria Schütz"), Ct)));

        var personId = Validate(await With<IProspects, Result>(s => s.AdoptAsync(id, Ct)));

        var person = (await With<IPersonService, PersonDetails?>(s => s.GetAsync(personId, Ct)))!;
        Assert.Equal(("Anna Maria", "Schütz", "anna@example.org"), (person.Data.FirstName, person.Data.LastName, person.Data.Email));
        var row = Assert.Single(await With<IProspects, IReadOnlyList<ProspectRow>>(s => s.ListAsync(null, Ct)));
        Assert.Equal((ProspectStatus.Accepted, personId), (row.Status, row.PersonId));

        // A second call does not create a second person.
        Assert.Equal(personId, Validate(await With<IProspects, Result>(s => s.AdoptAsync(id, Ct))));
        Assert.False((await With<IProspects, Result>(s => s.SetStatusAsync(id, ProspectStatus.Declined, Ct))).Succeeded);
    }

    [Fact]
    public async Task After_the_deletion_day_the_data_is_gone_for_good_and_only_a_note_without_personal_data_stays()
    {
        var rejected = Validate(await With<IProspects, Result>(s => s.CreateAsync(Input("Anna Schütz"), Ct)));
        var open = Validate(await With<IProspects, Result>(s => s.CreateAsync(Input("Bernd Beispiel", otherUnit), Ct)));
        var adopted = Validate(await With<IProspects, Result>(s => s.CreateAsync(Input("Clara Muster"), Ct)));
        Validate(await With<IProspects, Result>(s => s.SetStatusAsync(rejected, ProspectStatus.Declined, Ct)));
        Validate(await With<IProspects, Result>(s => s.AdoptAsync(adopted, Ct)));

        // The open prospect is still in play (changed after the others), so its six months start later.
        environment.Clock.SetUtcNow(new DateTimeOffset(2027, 5, 1, 8, 0, 0, TimeSpan.Zero));
        Validate(await With<IProspects, Result>(s => s.SetStatusAsync(open, ProspectStatus.Viewing, Ct)));

        // The day before the deletion day nothing happens.
        environment.Clock.SetUtcNow(new DateTimeOffset(2027, 5, 15, 8, 0, 0, TimeSpan.Zero));
        Assert.Equal(0, await With<ProspectRetention, int>(s => s.CleanUpAsync(Ct)));
        Assert.Equal(3, (await With<IProspects, IReadOnlyList<ProspectRow>>(s => s.ListAsync(null, Ct))).Count);

        environment.Clock.SetUtcNow(new DateTimeOffset(2027, 5, 16, 8, 0, 0, TimeSpan.Zero));
        Assert.Equal(2, await With<ProspectRetention, int>(s => s.CleanUpAsync(Ct)));

        // Only the open prospect is left; the rejected and the adopted one are physically gone, not just marked.
        Assert.Equal(open, Assert.Single(await With<IProspects, IReadOnlyList<ProspectRow>>(s => s.ListAsync(null, Ct))).Id);
        await using var scope = environment.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();
        Assert.Equal(1, await db.Set<Prospect>().IgnoreQueryFilters().CountAsync(Ct));

        // The change log of the deleted ones holds no name, no email: one entry each, with the reason.
        var entries = await db.Set<AuditEntry>().Where(a => a.EntityId == rejected || a.EntityId == adopted).ToListAsync(Ct);
        Assert.All(entries, e =>
        {
            Assert.Equal((AuditAction.Deleted, ProspectRetention.Reason, "[]"), (e.Action, e.Purpose, e.ChangesJson));
            Assert.DoesNotContain("Schütz", e.ChangesJson, StringComparison.Ordinal);
        });
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public async Task A_prospect_without_a_decision_is_deleted_six_months_after_the_last_change()
    {
        var id = Validate(await With<IProspects, Result>(s => s.CreateAsync(Input("Dora Dauer"), Ct)));
        Assert.Equal(new DateOnly(2027, 5, 16), Assert.Single(await With<IProspects, IReadOnlyList<ProspectRow>>(s => s.ListAsync(null, Ct))).DeleteOn);

        environment.Clock.SetUtcNow(new DateTimeOffset(2027, 5, 15, 8, 0, 0, TimeSpan.Zero));
        Assert.Equal(0, await With<ProspectRetention, int>(s => s.CleanUpAsync(Ct)));

        environment.Clock.SetUtcNow(new DateTimeOffset(2027, 5, 16, 8, 0, 0, TimeSpan.Zero));
        Assert.Equal(1, await With<ProspectRetention, int>(s => s.CleanUpAsync(Ct)));
        Assert.Empty(await With<IProspects, IReadOnlyList<ProspectRow>>(s => s.ListAsync(null, Ct)));
        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task When_the_unit_goes_to_one_prospect_the_other_open_ones_of_that_unit_get_the_deletion_day()
    {
        var chosen = Validate(await With<IProspects, Result>(s => s.CreateAsync(Input("Eva Erst"), Ct)));
        var other = Validate(await With<IProspects, Result>(s => s.CreateAsync(Input("Finn Zweit"), Ct)));
        var elsewhere = Validate(await With<IProspects, Result>(s => s.CreateAsync(Input("Gero Anders", otherUnit), Ct)));

        environment.Clock.SetUtcNow(new DateTimeOffset(2026, 12, 20, 8, 0, 0, TimeSpan.Zero));
        Validate(await With<IProspects, Result>(s => s.AdoptAsync(chosen, Ct)));

        var rows = (await With<IProspects, IReadOnlyList<ProspectRow>>(s => s.ListAsync(null, Ct))).ToDictionary(r => r.Id);
        Assert.Equal(new DateOnly(2027, 6, 20), rows[other].DeleteOn);
        Assert.Equal(ProspectStatus.Inquiry, rows[other].Status);
        Assert.Equal(new DateOnly(2027, 5, 16), rows[elsewhere].DeleteOn);
    }

    private static string TokenOf(DisclosureLink link) => link.Url![(link.Url!.LastIndexOf('/') + 1)..];

    [Fact]
    public async Task A_self_disclosure_link_works_once_asks_no_consent_and_moves_the_prospect_on()
    {
        var id = Validate(await With<IProspects, Result>(s => s.CreateAsync(Input(), Ct)));
        var link = await With<ISelfDisclosures, DisclosureLink>(s => s.CreateLinkAsync(id, 14, Ct));
        Validate(link.Result);
        var token = TokenOf(link);

        Assert.Equal("Wohnung 1, Lindenweg 3", (await With<ISelfDisclosures, DisclosureForm?>(s => s.ForFormAsync(token, Ct)))!.Unit);
        Assert.Equal(Prospect.RetentionMonthsAfterRejection, (await With<ISelfDisclosures, DisclosureForm?>(s => s.ForFormAsync(token, Ct)))!.RetentionMonths);
        Assert.Null(await With<ISelfDisclosures, DisclosureForm?>(s => s.ForFormAsync("falsch", Ct)));

        Assert.False((await With<ISelfDisclosures, Result>(s => s.SubmitAsync(token, new DisclosureInput { Persons = 0 }, Ct))).Succeeded);
        Validate(await With<ISelfDisclosures, Result>(s => s.SubmitAsync(token, new DisclosureInput { Persons = 2, Occupation = "Lehrerin", NetIncome = 3200m }, Ct)));

        var view = (await With<ISelfDisclosures, DisclosureView?>(s => s.GetAsync(id, Ct)))!;
        Assert.Equal((2, "Lehrerin", 3200m, false), (view.Persons, view.Occupation, view.NetIncome, view.Open));
        Assert.Equal(ProspectStatus.SelfDisclosure, Assert.Single(await With<IProspects, IReadOnlyList<ProspectRow>>(s => s.ListAsync(null, Ct))).Status);

        // Used links are dead.
        Assert.Null(await With<ISelfDisclosures, DisclosureForm?>(s => s.ForFormAsync(token, Ct)));
        Assert.False((await With<ISelfDisclosures, Result>(s => s.SubmitAsync(token, new DisclosureInput { Persons = 1 }, Ct))).Succeeded);
    }

    [Fact]
    public async Task An_expired_or_replaced_link_is_dead()
    {
        var id = Validate(await With<IProspects, Result>(s => s.CreateAsync(Input(), Ct)));
        var first = TokenOf(await With<ISelfDisclosures, DisclosureLink>(s => s.CreateLinkAsync(id, 14, Ct)));
        var second = TokenOf(await With<ISelfDisclosures, DisclosureLink>(s => s.CreateLinkAsync(id, 14, Ct)));

        Assert.Null(await With<ISelfDisclosures, DisclosureForm?>(s => s.ForFormAsync(first, Ct)));
        Assert.NotNull(await With<ISelfDisclosures, DisclosureForm?>(s => s.ForFormAsync(second, Ct)));

        environment.Clock.SetUtcNow(new DateTimeOffset(2026, 12, 1, 8, 0, 0, TimeSpan.Zero));
        Assert.Null(await With<ISelfDisclosures, DisclosureForm?>(s => s.ForFormAsync(second, Ct)));
    }

    [Fact]
    public async Task The_deletion_takes_the_self_disclosure_and_its_change_log_with_it()
    {
        var id = Validate(await With<IProspects, Result>(s => s.CreateAsync(Input("Anna Schütz"), Ct)));
        var token = TokenOf(await With<ISelfDisclosures, DisclosureLink>(s => s.CreateLinkAsync(id, 14, Ct)));
        Validate(await With<ISelfDisclosures, Result>(s => s.SubmitAsync(token, new DisclosureInput { Persons = 1, Occupation = "Lehrerin" }, Ct)));
        Validate(await With<IProspects, Result>(s => s.SetStatusAsync(id, ProspectStatus.Declined, Ct)));

        environment.Clock.SetUtcNow(new DateTimeOffset(2027, 5, 16, 8, 0, 0, TimeSpan.Zero));
        Assert.Equal(1, await With<ProspectRetention, int>(s => s.CleanUpAsync(Ct)));

        await using var scope = environment.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();
        Assert.Equal(0, await db.Set<SelfDisclosure>().IgnoreQueryFilters().CountAsync(Ct));
        var entries = await db.Set<AuditEntry>().Where(a => a.EntityType == nameof(SelfDisclosure) || a.EntityType == nameof(Prospect)).ToListAsync(Ct);
        Assert.All(entries, e => Assert.DoesNotContain("Lehrerin", e.ChangesJson, StringComparison.Ordinal));
        Assert.DoesNotContain(entries, e => e.EntityType == nameof(SelfDisclosure));
    }

    private async Task<T> With<TService, T>(Func<TService, Task<T>> action)
        where TService : notnull
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }
}
