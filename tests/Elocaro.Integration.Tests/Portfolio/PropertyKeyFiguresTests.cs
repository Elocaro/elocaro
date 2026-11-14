using System.Security.Claims;
using Elocaro.Application.Accounts;
using Elocaro.Application.Common;
using Elocaro.Application.Finance;
using Elocaro.Application.Leases;
using Elocaro.Application.People;
using Elocaro.Application.Portfolio;
using Elocaro.Application.Properties;
using Elocaro.Application.Tax;
using Elocaro.Domain.Tax;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Portfolio;

public class PropertyKeyFiguresTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly ClaimsPrincipal Admin = new(new ClaimsIdentity([new Claim(ClaimTypes.Role, Roles.Admin)], "test"));

    private InfrastructureEnvironment environment = null!;
    private Guid propertyId;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres);
        environment.Clock.SetUtcNow(new DateTimeOffset(2027, 2, 10, 10, 0, 0, TimeSpan.Zero));
        await using var scope = environment.Services.CreateAsyncScope();
        var properties = scope.ServiceProvider.GetRequiredService<IPropertyService>();
        propertyId = Validate(await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct));
        var apartment = Validate(await properties.CreateUnitAsync(propertyId, new UnitInput { Name = "Wohnung 1" }, Ct));
        var anna = Validate(await scope.ServiceProvider.GetRequiredService<IPersonService>().CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt" }, null, Ct));
        var lease = Validate(await scope.ServiceProvider.GetRequiredService<ILeaseService>().CreateAsync(new LeaseCreation
        {
            RentalUnitId = apartment,
            Start = new DateOnly(2026, 11, 1),
            TenantIds = [anna],
            BaseRent = 620m,
            OperatingCostPrepayment = 150m,
        }, Ct));
        await scope.ServiceProvider.GetRequiredService<IFinanceSettings>().SaveAsync(new FinanceSettingsInput { AccountingStart = new DateOnly(2026, 11, 1) }, Ct);
        var accounts = scope.ServiceProvider.GetRequiredService<ITenantAccounts>();
        await accounts.GenerateChargesAsync(null, Ct);
        Validate(await accounts.RecordPaymentAsync(lease, new PaymentInput { Date = new DateOnly(2026, 11, 5), Amount = 770m }, Ct));
        Validate(await scope.ServiceProvider.GetRequiredService<IDepreciationService>().SaveAcquisitionAsync(propertyId, new AcquisitionInput
        {
            PurchaseDate = new DateOnly(2020, 6, 1),
            PurchasePrice = 200_000m,
            Notary = 3_000m,
            TransferTax = 7_000m,
            LandSharePercent = 20m,
            CompletionYear = 1990,
        }, Ct));
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Figures_agree_with_the_anlage_v_and_the_leases()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var report = (await scope.ServiceProvider.GetRequiredService<IAnlageVService>().GetAsync(propertyId, 2026, false, Ct))!;
        var figures = (await scope.ServiceProvider.GetRequiredService<IPropertyKeyFigures>().GetAsync(Admin, propertyId, 2026, Ct))!;

        var baseRent = report.Result.Income.Single(l => l.Info.Category == AnlageVCategory.BaseRent).Amount;
        Assert.True(baseRent > 0m);
        Assert.Equal(decimal.Round(baseRent / 210_000m * 100m, 2), figures.NetYield); // no non-allocable costs yet
        Assert.Equal(report.Result.IncomeTotal, figures.CashflowBeforeRepayment); // no expenses, no loan
        Assert.Equal(decimal.Round(304m / 365m * 100m, 1), figures.VacancyPercent); // let from 1 November: 304 vacant days
        Assert.Contains(figures.Steps, s => s.Label == "Cashflow");
    }

    [Fact]
    public async Task Without_finance_rights_or_for_an_unknown_property_there_are_no_figures()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IPropertyKeyFigures>();

        Assert.Null(await service.GetAsync(new ClaimsPrincipal(new ClaimsIdentity([], "test")), propertyId, 2026, Ct));
        Assert.Null(await service.GetAsync(Admin, Guid.NewGuid(), 2026, Ct));
    }

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }
}
