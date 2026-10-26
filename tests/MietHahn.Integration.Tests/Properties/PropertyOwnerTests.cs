using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Common;
using MietHahn.Application.People;
using MietHahn.Application.Properties;
using MietHahn.Integration.Tests.Infrastructure;

namespace MietHahn.Integration.Tests.Properties;

public class PropertyOwnerTests(PostgresFixture postgres) : IAsyncLifetime
{
    private InfrastructureEnvironment environment = null!;
    private Guid house;
    private Guid anna;
    private Guid ben;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres);
        environment.Clock.SetUtcNow(new DateTimeOffset(2026, 6, 10, 10, 0, 0, TimeSpan.Zero));
        await using var scope = environment.Services.CreateAsyncScope();
        house = Validate(await scope.ServiceProvider.GetRequiredService<IPropertyService>().CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct));
        var people = scope.ServiceProvider.GetRequiredService<IPersonService>();
        anna = Validate(await people.CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt" }, null, Ct));
        ben = Validate(await people.CreateAsync(new PersonInput { FirstName = "Ben", LastName = "Weber" }, null, Ct));
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Shares_are_listed_with_a_hint_until_they_add_up_to_100()
    {
        Assert.Empty((await WithAsync(o => o.GetAsync(house, Ct))).Hints);

        Validate(await WithAsync(o => o.AddAsync(house, new OwnerInput { PersonId = anna, SharePercent = 60m }, Ct)));
        var partial = await WithAsync(o => o.GetAsync(house, Ct));
        Assert.Contains("60 %", Assert.Single(partial.Hints), StringComparison.Ordinal);

        Validate(await WithAsync(o => o.AddAsync(house, new OwnerInput { PersonId = ben, SharePercent = 40m }, Ct)));
        var full = await WithAsync(o => o.GetAsync(house, Ct));
        Assert.Empty(full.Hints);
        Assert.Equal(2, full.Owners.Count);
        Assert.Equal(60m, Assert.Single(await WithAsync(o => o.OwnedByAsync(anna, Ct))).SharePercent);
        Assert.Equal(40m, Assert.Single(await WithAsync(o => o.OwnedByAsync(ben, Ct))).SharePercent);
    }

    [Fact]
    public async Task More_than_100_percent_and_double_entries_are_refused()
    {
        Validate(await WithAsync(o => o.AddAsync(house, new OwnerInput { PersonId = anna, SharePercent = 60m }, Ct)));

        Assert.Contains("100 %", (await WithAsync(o => o.AddAsync(house, new OwnerInput { PersonId = ben, SharePercent = 50m }, Ct))).Errors.Single(), StringComparison.Ordinal);
        Assert.Contains("schon als Eigentümer", (await WithAsync(o => o.AddAsync(house, new OwnerInput { PersonId = anna, SharePercent = 10m }, Ct))).Errors.Single(), StringComparison.Ordinal);
        Assert.False((await WithAsync(o => o.AddAsync(house, new OwnerInput { PersonId = Guid.NewGuid(), SharePercent = 10m }, Ct))).Succeeded);
        Assert.Single((await WithAsync(o => o.GetAsync(house, Ct))).Owners);
    }

    [Fact]
    public async Task An_owner_entry_can_be_corrected_but_not_beyond_100_percent()
    {
        var first = Validate(await WithAsync(o => o.AddAsync(house, new OwnerInput { PersonId = anna, SharePercent = 60m }, Ct)));
        Validate(await WithAsync(o => o.AddAsync(house, new OwnerInput { PersonId = ben, SharePercent = 40m }, Ct)));

        Assert.False((await WithAsync(o => o.UpdateAsync(house, first, new OwnerInput { PersonId = anna, SharePercent = 70m }, Ct))).Succeeded);
        Validate(await WithAsync(o => o.UpdateAsync(house, first, new OwnerInput { PersonId = anna, SharePercent = 50m, ValidFrom = new DateOnly(2020, 1, 1) }, Ct)));

        var row = (await WithAsync(o => o.GetAsync(house, Ct))).Owners.Single(r => r.Id == first);
        Assert.Equal((50m, new DateOnly(2020, 1, 1)), (row.SharePercent, row.ValidFrom));
    }

    [Fact]
    public async Task Ending_an_ownership_keeps_the_history_and_frees_the_share()
    {
        var id = Validate(await WithAsync(o => o.AddAsync(house, new OwnerInput { PersonId = anna, SharePercent = 100m }, Ct)));
        Validate(await WithAsync(o => o.EndAsync(house, id, new DateOnly(2026, 6, 30), Ct)));

        environment.Clock.Advance(TimeSpan.FromDays(30));
        var ownership = await WithAsync(o => o.GetAsync(house, Ct));
        Assert.False(Assert.Single(ownership.Owners).Current);
        Assert.Contains("kein Eigentümer", Assert.Single(ownership.Hints), StringComparison.Ordinal);
        Assert.Empty(await WithAsync(o => o.OwnedByAsync(anna, Ct)));

        Validate(await WithAsync(o => o.AddAsync(house, new OwnerInput { PersonId = ben, SharePercent = 100m, ValidFrom = new DateOnly(2026, 7, 1) }, Ct)));
        Assert.Empty((await WithAsync(o => o.GetAsync(house, Ct))).Hints);
    }

    [Fact]
    public async Task An_owner_cannot_be_deleted_as_a_person()
    {
        Validate(await WithAsync(o => o.AddAsync(house, new OwnerInput { PersonId = anna, SharePercent = 100m }, Ct)));

        await using var scope = environment.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IPersonService>().DeleteAsync(anna, Ct);

        Assert.False(result.Succeeded);
    }

    private async Task<T> WithAsync<T>(Func<IPropertyOwners, Task<T>> action)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IPropertyOwners>());
    }

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        return result.Id!.Value;
    }
}
