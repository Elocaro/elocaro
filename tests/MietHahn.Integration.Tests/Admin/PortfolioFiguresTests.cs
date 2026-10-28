using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.Admin;
using MietHahn.Application.Common;
using MietHahn.Application.Finance;
using MietHahn.Application.Leases;
using MietHahn.Application.People;
using MietHahn.Application.Portfolio;
using MietHahn.Application.Properties;
using MietHahn.Integration.Tests.Infrastructure;

namespace MietHahn.Integration.Tests.Admin;

public class PortfolioFiguresTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly ClaimsPrincipal Administrator = new(new ClaimsIdentity([new Claim(ClaimTypes.Role, Roles.Admin)], "test"));

    private InfrastructureEnvironment environment = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => environment = await InfrastructureEnvironment.CreateAsync(postgres);

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Without_anything_rentable_there_are_no_figures()
    {
        Assert.Null(await FiguresAsync(Administrator));
    }

    [Fact]
    public async Task Figures_count_rent_payments_arrears_and_vacancy()
    {
        // Clock: 1 March 2026. Two apartments, one let from January for 650 + 120, one vacant.
        await using var scope = environment.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var properties = sp.GetRequiredService<IPropertyService>();
        var house = Validate(await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct));
        var rented = Validate(await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 1" }, Ct));
        Validate(await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 2" }, Ct));
        var tenant = Validate(await sp.GetRequiredService<IPersonService>().CreateAsync(new PersonInput { LastName = "Schmidt" }, null, Ct));
        var lease = Validate(await sp.GetRequiredService<ILeaseService>().CreateAsync(
            new LeaseCreation { RentalUnitId = rented, Start = new DateOnly(2026, 1, 1), TenantIds = [tenant], BaseRent = 650m, OperatingCostPrepayment = 120m }, Ct));
        await sp.GetRequiredService<IFinanceSettings>().SaveAsync(new FinanceSettingsInput { AccountingStart = new DateOnly(2026, 1, 1) }, Ct);
        var accounts = sp.GetRequiredService<ITenantAccounts>();
        await accounts.GenerateChargesAsync(null, Ct);
        Validate(await accounts.RecordPaymentAsync(lease, new PaymentInput { Date = new DateOnly(2026, 1, 3), Amount = 770m }, Ct));
        Validate(await accounts.RecordPaymentAsync(lease, new PaymentInput { Date = new DateOnly(2026, 3, 1), Amount = 300m }, Ct));

        var figures = await FiguresAsync(Administrator);

        Assert.NotNull(figures);
        Assert.Equal(new DateOnly(2026, 3, 1), figures.Month);
        Assert.Equal((2, 1, 1), (figures.Units, figures.Occupied, figures.Vacant));
        Assert.Equal(770m, figures.MonthlyRent);
        Assert.Equal(300m, figures.PaidThisMonth);
        // January is paid; February is due and settled by 300 (oldest claim first); March is due only on the 4th and not counted yet.
        Assert.Equal(770m - 300m, figures.Arrears);
        Assert.Equal(1, figures.ArrearsLeases);

        // History: the last twelve months up to March 2026, with the open balance at each month end.
        Assert.Equal(12, figures.History!.Count);
        Assert.Equal((new DateOnly(2025, 4, 1), new DateOnly(2026, 3, 1)), (figures.History[0].Month, figures.History[^1].Month));
        var january = figures.History.Single(m => m.Month == new DateOnly(2026, 1, 1));
        Assert.Equal((770m, 770m, 0m), (january.Due, january.Paid, january.OpenAtEnd));
        Assert.Equal(770m, figures.History.Single(m => m.Month == new DateOnly(2026, 2, 1)).OpenAtEnd);
    }

    [Fact]
    public async Task Users_without_finance_rights_get_no_figures()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var properties = scope.ServiceProvider.GetRequiredService<IPropertyService>();
        var house = Validate(await properties.CreateAsync(new PropertyInput { Name = "Haus", Street = "Weg 1", PostalCode = "50667", City = "Köln" }, Ct));
        Validate(await properties.CreateUnitAsync(house, new UnitInput { Name = "W1" }, Ct));

        Assert.NotNull(await FiguresAsync(Administrator));
        var manager = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, Roles.Manager), new Claim(Permissions.ClaimType, nameof(AccessRight.MasterData))], "test"));
        Assert.Null(await FiguresAsync(manager));
    }

    private async Task<PortfolioFigures?> FiguresAsync(ClaimsPrincipal user)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPortfolioFigures>().FiguresAsync(user, Ct);
    }

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }
}
