using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.Admin;
using MietHahn.Application.Finance;
using MietHahn.Application.Legal;
using MietHahn.Application.Todos;
using MietHahn.Domain.Leases;
using MietHahn.Infrastructure.Persistence;
using MietHahn.Integration.Tests.Infrastructure;

namespace MietHahn.Integration.Tests.Admin;

public class LegalReferencesTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly ClaimsPrincipal Administrator = new(new ClaimsIdentity([new Claim(ClaimTypes.Role, Roles.Admin)], "test"));

    private InfrastructureEnvironment environment = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => environment = await InfrastructureEnvironment.CreateAsync(postgres);

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Base_rate_is_missing_then_current_in_the_half_year_and_to_check_in_the_next()
    {
        // Clock: 1 March 2026, first half-year.
        Assert.Equal(LegalReferenceStatus.Missing, (await ReferenceAsync("base-rate")).Status);

        await SaveBaseRateAsync(2.27m, new DateOnly(2026, 1, 1));
        var current = await ReferenceAsync("base-rate");
        Assert.Equal(LegalReferenceStatus.Current, current.Status);
        Assert.Equal("2,27 % p. a.", current.Value);
        Assert.Equal(new DateOnly(2026, 7, 1), current.NextCheck);

        environment.Clock.SetUtcNow(new DateTimeOffset(2026, 7, 2, 9, 0, 0, TimeSpan.Zero));
        var stale = await ReferenceAsync("base-rate");
        Assert.Equal(LegalReferenceStatus.Check, stale.Status);
        Assert.Contains("laufenden Halbjahr", stale.Note, StringComparison.Ordinal);
        Assert.Equal(new DateOnly(2027, 1, 1), stale.NextCheck);
    }

    [Fact]
    public async Task Cpi_is_missing_without_values_and_stale_when_a_published_month_is_missing()
    {
        Assert.Equal(LegalReferenceStatus.Missing, (await ReferenceAsync("cpi")).Status);

        await AddCpiAsync(new DateOnly(2025, 12, 1), 121.2m);
        // 1 March, before the 16th: January is the newest month that must be there (published mid-February), February is not yet due.
        Assert.Equal(LegalReferenceStatus.Check, (await ReferenceAsync("cpi")).Status);

        await AddCpiAsync(new DateOnly(2026, 1, 1), 121.8m);
        var current = await ReferenceAsync("cpi");
        Assert.Equal(LegalReferenceStatus.Current, current.Status);
        Assert.Equal("Stand 01/2026", current.AsOf);

        environment.Clock.SetUtcNow(new DateTimeOffset(2026, 3, 20, 9, 0, 0, TimeSpan.Zero));
        Assert.Equal(LegalReferenceStatus.Check, (await ReferenceAsync("cpi")).Status);
    }

    [Fact]
    public async Task The_shipped_tables_say_that_they_are_not_checked()
    {
        var carbon = await ReferenceAsync("carbon");
        var degreeDays = await ReferenceAsync("degree-days");

        Assert.Equal(LegalReferenceStatus.Check, carbon.Status);
        Assert.Contains("CO2KostAufG", carbon.Source, StringComparison.Ordinal);
        Assert.Equal(LegalReferenceStatus.Check, degreeDays.Status);
        Assert.Contains("Platzhalter", degreeDays.Note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_check_note_settles_the_carbon_levels_for_a_year_but_not_the_placeholder_and_ends_when_the_value_changes()
    {
        await MarkAsync("carbon", "Gesetzestext");
        await MarkAsync("degree-days", null);

        var carbon = await ReferenceAsync("carbon");
        Assert.Equal(LegalReferenceStatus.Current, carbon.Status);
        Assert.Equal((new DateOnly(2026, 3, 1), "Gesetzestext"), (carbon.CheckedOn, carbon.CheckSource));
        Assert.NotNull(carbon.CheckedBy);
        Assert.Equal(LegalReferenceStatus.Check, (await ReferenceAsync("degree-days")).Status);
        Assert.NotNull((await ReferenceAsync("degree-days")).CheckedOn);

        environment.Clock.SetUtcNow(new DateTimeOffset(2027, 3, 2, 9, 0, 0, TimeSpan.Zero));
        Assert.Equal(LegalReferenceStatus.Check, (await ReferenceAsync("carbon")).Status);

        await using var scope = environment.Services.CreateAsyncScope();
        Assert.False((await scope.ServiceProvider.GetRequiredService<ILegalReferences>().MarkCheckedAsync("base-rate", null, Ct)).Succeeded); // value missing
        Assert.False((await scope.ServiceProvider.GetRequiredService<ILegalReferences>().MarkCheckedAsync("unbekannt", null, Ct)).Succeeded);
    }

    [Fact]
    public async Task Only_what_the_enabled_modules_need_is_listed()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var state = scope.ServiceProvider.GetRequiredService<IModuleState>();
        var all = Enum.GetValues<AppModule>().Where(m => m is not AppModule.OperatingCosts and not AppModule.RentIncreases and not AppModule.Dunning);
        await state.SaveAsync(all, completeSetup: true, Enum.GetValues<AppOption>(), Ct);

        Assert.Empty(await OverviewAsync());
    }

    [Fact]
    public async Task Dashboard_lists_missing_or_outdated_maintained_values_but_not_the_shipped_tables()
    {
        var todos = await TodosAsync(Administrator);

        Assert.Equal(["Basiszinssatz (Verzugszinsen): Wert fehlt", "Verbraucherpreisindex (Indexmieten): Wert fehlt"], todos.Select(t => t.Title).Order());
        Assert.All(todos, t => Assert.Equal(Urgency.Soon, t.Urgency));
        Assert.DoesNotContain(todos, t => t.Title.Contains("CO₂", StringComparison.Ordinal) || t.Title.Contains("Gradtag", StringComparison.Ordinal));

        await SaveBaseRateAsync(2.27m, new DateOnly(2026, 1, 1));
        await AddCpiAsync(new DateOnly(2026, 1, 1), 121.8m);
        Assert.Empty(await TodosAsync(Administrator));

        Assert.Empty(await TodosAsync(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, Roles.Tenant)], "test"))));
    }

    private async Task<IReadOnlyList<LegalReference>> OverviewAsync()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ILegalReferences>().OverviewAsync(Ct);
    }

    private async Task MarkAsync(string key, string? source)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<ILegalReferences>().MarkCheckedAsync(key, source, Ct);
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
    }

    private async Task<LegalReference> ReferenceAsync(string key) => (await OverviewAsync()).Single(r => r.Key == key);

    private async Task<IReadOnlyList<TodoItem>> TodosAsync(ClaimsPrincipal user)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var source = scope.ServiceProvider.GetServices<ITodoSource>().Single(s => s.GetType().Name == "LegalReferenceTodos");
        return await source.TodosAsync(user, Ct);
    }

    private async Task SaveBaseRateAsync(decimal rate, DateOnly since)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<IFinanceSettings>();
        var input = await settings.GetAsync(Ct);
        input.ChargeDefaultInterest = true;
        input.BaseRate = rate;
        input.BaseRateAsOf = since;
        await settings.SaveAsync(input, Ct);
    }

    private async Task AddCpiAsync(DateOnly month, decimal value)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
        db.Add(new CpiValue(month, value));
        await db.SaveChangesAsync(Ct);
    }
}
