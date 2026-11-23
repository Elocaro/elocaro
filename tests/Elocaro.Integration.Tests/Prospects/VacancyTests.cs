using Elocaro.Application.Common;
using Elocaro.Application.Deadlines;
using Elocaro.Application.Leases;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Application.Prospects;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Prospects;

public class VacancyTests(PostgresFixture postgres) : IAsyncLifetime
{
    private InfrastructureEnvironment environment = null!;
    private Guid property;
    private Guid ended;
    private Guid never;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres);
        environment.Clock.SetUtcNow(new DateTimeOffset(2026, 11, 16, 8, 0, 0, TimeSpan.Zero));
        property = Validate(await With<IPropertyService, Result>(s => s.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct)));
        ended = Validate(await With<IPropertyService, Result>(s => s.CreateUnitAsync(property, new UnitInput { Name = "Wohnung 1" }, Ct)));
        never = Validate(await With<IPropertyService, Result>(s => s.CreateUnitAsync(property, new UnitInput { Name = "Wohnung 2" }, Ct)));
        var running = Validate(await With<IPropertyService, Result>(s => s.CreateUnitAsync(property, new UnitInput { Name = "Wohnung 3" }, Ct)));
        var tenant = Validate(await With<IPersonService, Result>(s => s.CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schütz" }, null, Ct)));
        Validate(await With<ILeaseService, Result>(s => s.CreateAsync(new LeaseCreation { RentalUnitId = ended, Start = new DateOnly(2026, 1, 1), FixedTermUntil = new DateOnly(2026, 9, 30), TenantIds = [tenant], BaseRent = 500m, People = 1 }, Ct)));
        Validate(await With<ILeaseService, Result>(s => s.CreateAsync(new LeaseCreation { RentalUnitId = running, Start = new DateOnly(2026, 1, 1), TenantIds = [tenant], BaseRent = 600m, People = 1 }, Ct)));
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Vacant_units_show_duration_and_lost_rent_and_rented_ones_are_left_out()
    {
        var rows = await With<IVacancies, IReadOnlyList<VacancyRow>>(s => s.ListAsync(Ct));

        Assert.Equal(["Wohnung 2", "Wohnung 1"], rows.Select(r => r.RentalUnit)); // never rented first
        var first = rows.Single(r => r.RentalUnitId == ended);
        Assert.Equal((new DateOnly(2026, 10, 1), 46, 500m, 755.65m), (first.VacantSince, first.Days, first.LastBaseRent, first.LostRent));
        Assert.Contains("kein Interessent", first.Hint, StringComparison.Ordinal);
        var other = rows.Single(r => r.RentalUnitId == never);
        Assert.Null(other.VacantSince);
        Assert.Null(other.LostRent);
    }

    [Fact]
    public async Task A_prospect_with_a_viewing_removes_the_hint_and_the_reminder()
    {
        Validate(await With<IProspects, Result>(s => s.CreateAsync(new ProspectInput { RentalUnitId = ended, Name = "Ben Weber", ViewingOn = new DateOnly(2026, 11, 20) }, Ct)));

        var row = (await With<IVacancies, IReadOnlyList<VacancyRow>>(s => s.ListAsync(Ct))).Single(r => r.RentalUnitId == ended);

        Assert.Equal((1, new DateOnly(2026, 11, 20), null), (row.OpenProspects, row.NextViewing, row.Hint));
    }

    [Fact]
    public async Task A_unit_empty_for_a_month_without_prospect_gets_a_deadline()
    {
        var entries = await With<IEnumerable<IDeadlineSource>, List<DeadlineEntry>>(async sources =>
        {
            var all = new List<DeadlineEntry>();
            foreach (var source in sources)
            {
                all.AddRange(await source.DeadlinesAsync(new DateOnly(2027, 1, 1), Ct));
            }

            return all;
        });

        var entry = Assert.Single(entries, e => e.Key == $"vacancy:{ended}");
        Assert.Equal(new DateOnly(2026, 10, 31), entry.Due);
        Assert.DoesNotContain(entries, e => e.Key == $"vacancy:{never}"); // never rented: nothing to count from
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
