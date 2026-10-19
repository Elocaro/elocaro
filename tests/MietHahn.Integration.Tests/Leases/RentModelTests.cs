using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.Common;
using MietHahn.Application.Leases;
using MietHahn.Application.People;
using MietHahn.Application.Properties;
using MietHahn.Application.Todos;
using MietHahn.Domain.Communication;
using MietHahn.Domain.Leases;
using MietHahn.Infrastructure.Leases;
using MietHahn.Infrastructure.Persistence;
using MietHahn.Integration.Tests.Infrastructure;

namespace MietHahn.Integration.Tests.Leases;

public class RentModelTests(PostgresFixture postgres) : IAsyncLifetime
{
    // Startzeit der Testumgebung: 01.03.2026
    private static readonly DateOnly Start = new(2026, 3, 1);
    private static readonly ClaimsPrincipal Admin = new(new ClaimsIdentity([new Claim(ClaimTypes.Role, Roles.Admin)], "test"));

    private InfrastructureEnvironment environment = null!;
    private Guid leaseId;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres);
        await using var scope = environment.Services.CreateAsyncScope();
        var properties = scope.ServiceProvider.GetRequiredService<IPropertyService>();
        var property = Validate(await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct));
        var apartment = Validate(await properties.CreateUnitAsync(property, new UnitInput { Name = "Wohnung 1" }, Ct));
        var tenant = Validate(await scope.ServiceProvider.GetRequiredService<IPersonService>().CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt" }, null, Ct));
        leaseId = Validate(await scope.ServiceProvider.GetRequiredService<ILeaseService>().CreateAsync(
            new LeaseCreation { RentalUnitId = apartment, Start = Start, TenantIds = [tenant], BaseRent = 650m }, Ct));
        await scope.ServiceProvider.GetRequiredService<IAccountService>().CreateFirstAdminAsync("admin@example.de", "Admin", "Langes-Passwort-2026", Ct);
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Rent_step_is_applied_on_effective_date_and_admin_notified()
    {
        Validate(await WithAsync(v => v.SetRentModelAsync(leaseId, new RentModelInput { Model = RentModel.Graduated }, Ct)));
        Validate(await WithAsync(v => v.AddRentStepAsync(leaseId, new RentStepInput { From = new DateOnly(2027, 3, 1), BaseRent = 700m }, Ct)));
        var tooEarly = await WithAsync(v => v.AddRentStepAsync(leaseId, new RentStepInput { From = new DateOnly(2027, 9, 1), BaseRent = 720m }, Ct));
        Assert.False(tooEarly.Succeeded);

        environment.Clock.SetUtcNow(new DateTimeOffset(2027, 2, 27, 23, 0, 0, TimeSpan.Zero)); // 28.02. deutsche Zeit
        Assert.Equal(0, await ApplyAsync());
        Assert.Contains(await TodosAsync(), a => a.Title.StartsWith("Staffel Wohnung 1: 700,00 € ab 01.03.2027", StringComparison.Ordinal));

        environment.Clock.SetUtcNow(new DateTimeOffset(2027, 3, 1, 0, 30, 0, TimeSpan.Zero));
        Assert.Equal(1, await ApplyAsync());
        Assert.Equal(0, await ApplyAsync());

        var lease = (await WithAsync(v => v.GetAsync(leaseId, Ct)))!;
        Assert.Equal(700m, lease.RentModel.BaseRent);
        Assert.NotNull(lease.RentModel.RentSteps.Single().AppliedAt);
        Assert.Equal(new DateOnly(2027, 3, 1), lease.RentModel.LastIncrease);

        await using var scope = environment.Services.CreateAsyncScope();
        var mail = await scope.ServiceProvider.GetRequiredService<MietHahnDbContext>().Set<OutgoingMail>().SingleAsync(m => m.Template == "graduated-rent-applied", Ct);
        Assert.Equal(["admin@example.de"], mail.To);
        Assert.Contains("Wohnung 1, Lindenweg 3: ab 01.03.2027 700,00 € Kaltmiete", mail.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Calculate_and_apply_indexed_rent_with_imported_Cpi()
    {
        Validate(await WithAsync(v => v.SetRentModelAsync(leaseId, new RentModelInput { Model = RentModel.Index, IndexBaseMonth = new DateOnly(2026, 1, 1), IndexBaseValue = 117.4m }, Ct)));
        await using (var scope = environment.Services.CreateAsyncScope())
        {
            var import = await scope.ServiceProvider.GetRequiredService<ICpiService>().ImportAsync(
                "GENESIS-Tabelle: 61111-0002\nJahr;Monat;Verbraucherpreisindex\n2026;Januar;117,4\n2026;Februar;117,9\n2027;Januar;121,7\n2027;Februar;...\n", Ct);
            Assert.Equal(3, import.Added);
            Assert.Empty(import.Errors);
        }

        var lease = (await WithAsync(v => v.GetAsync(leaseId, Ct)))!;
        var suggestion = lease.RentModel.Index!;
        Assert.Equal(new DateOnly(2027, 1, 1), suggestion.CpiMonth);
        Assert.Equal(673.81m, suggestion.NewBaseRent);
        Assert.Equal(3.7m, suggestion.ChangePercent);
        Assert.Equal(new DateOnly(2027, 3, 1), suggestion.EarliestEffective);
        Assert.False(suggestion.PossibleNow);

        // Erklärung zu früh: wirksam ab 01.02.2027 < 01.03.2027
        Assert.False((await WithAsync(v => v.AdjustIndexAsync(leaseId, new IndexAdjustmentInput { DeclaredOn = new DateOnly(2026, 12, 10) }, Ct))).Succeeded);

        environment.Clock.SetUtcNow(new DateTimeOffset(2027, 1, 10, 10, 0, 0, TimeSpan.Zero));
        Assert.Contains(await TodosAsync(), a => a.Title == "Indexanpassung möglich: Wohnung 1 (+3,7 %)");
        Validate(await WithAsync(v => v.AdjustIndexAsync(leaseId, new IndexAdjustmentInput { DeclaredOn = new DateOnly(2027, 1, 10) }, Ct)));

        environment.Clock.SetUtcNow(new DateTimeOffset(2027, 3, 2, 10, 0, 0, TimeSpan.Zero));
        lease = (await WithAsync(v => v.GetAsync(leaseId, Ct)))!;
        Assert.Equal(673.81m, lease.RentModel.BaseRent);
        Assert.Equal(121.7m, lease.RentModel.IndexBaseValue);
        Assert.Null(lease.RentModel.Index); // kein neuerer VPI als der neue Ausgangsindex
    }

    [Fact]
    public async Task Maintain_Cpi_manually_and_reimport()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var vpi = scope.ServiceProvider.GetRequiredService<ICpiService>();

        Validate(await vpi.SetAsync(new DateOnly(2026, 3, 15), 118.2m, Ct));
        Validate(await vpi.SetAsync(new DateOnly(2026, 3, 1), 118.3m, Ct)); // korrigiert denselben Monat
        var import = await vpi.ImportAsync("2026-03;118,3\n03.2026;118,3\n2026-04;118,8\nKaputt;abc\n", Ct);

        var list = await vpi.ListAsync(Ct);
        Assert.Equal([new DateOnly(2026, 4, 1), new DateOnly(2026, 3, 1)], list.Select(v => v.Month));
        Assert.Equal(118.3m, list[1].Value);
        Assert.Equal((1, 0), (import.Added, import.Modified));
    }

    [Theory]
    [InlineData("2026;Januar;117,4", 2026, 1, 117.4)]
    [InlineData("\"2026\";\"März\";\"118,1\"", 2026, 3, 118.1)]
    [InlineData("2026-12;123.0", 2026, 12, 123.0)]
    [InlineData("01.2027\t124,5", 2027, 1, 124.5)]
    public void Cpi_Csv_formats(string line, int year, int month, double value)
    {
        var (values, errors) = CpiService.Read(line);

        Assert.Empty(errors);
        Assert.Equal((new DateOnly(year, month, 1), (decimal)value), Assert.Single(values));
    }

    private async Task<int> ApplyAsync()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<RentStepApplication>().ApplyAsync(Ct);
    }

    private async Task<List<TodoItem>> TodosAsync()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var source = scope.ServiceProvider.GetServices<ITodoSource>().OfType<RentModelTodos>().Single();
        return [.. await source.TodosAsync(Admin, Ct)];
    }

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }

    private async Task<T> WithAsync<T>(Func<ILeaseService, Task<T>> action)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ILeaseService>());
    }
}
