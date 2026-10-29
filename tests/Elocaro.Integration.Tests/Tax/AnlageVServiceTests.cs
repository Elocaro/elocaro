using Elocaro.Application.Accounts;
using Elocaro.Application.Common;
using Elocaro.Application.Finance;
using Elocaro.Application.Leases;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Application.Tax;
using Elocaro.Domain.Common;
using Elocaro.Domain.Receipts;
using Elocaro.Domain.Tax;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Tax;

public class AnlageVServiceTests(PostgresFixture postgres) : IAsyncLifetime
{
    private InfrastructureEnvironment environment = null!;
    private Guid propertyId;
    private Guid lease;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres);
        environment.Clock.SetUtcNow(new DateTimeOffset(2027, 2, 10, 10, 0, 0, TimeSpan.Zero));
        await using var scope = environment.Services.CreateAsyncScope();
        var properties = scope.ServiceProvider.GetRequiredService<IPropertyService>();
        propertyId = Validate(await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct));
        var apartment = Validate(await properties.CreateUnitAsync(propertyId, new UnitInput { Name = "Wohnung 1" }, Ct));
        var anna = Validate(await scope.ServiceProvider.GetRequiredService<IPersonService>().CreateAsync(
            new PersonInput { FirstName = "Anna", LastName = "Schmidt", Salutation = Elocaro.Domain.People.Salutation.Mrs, Email = "anna@example.de" }, null, Ct));
        lease = Validate(await scope.ServiceProvider.GetRequiredService<ILeaseService>().CreateAsync(new LeaseCreation
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
        foreach (var (date, amount) in new[] { (new DateOnly(2026, 11, 5), 770m), (new DateOnly(2026, 12, 4), 770m), (new DateOnly(2026, 12, 28), 770m) })
        {
            Validate(await accounts.RecordPaymentAsync(lease, new PaymentInput { Date = date, Amount = amount }, Ct));
        }
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    private async Task AddReceiptAsync(string costType, decimal amount, DateOnly? paidOn, DateOnly? dueOn = null)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();
        var receipt = new Receipt(new DateOnly(2026, 2, 1), amount) { SupplierName = "Stadt Köln", PaidOn = paidOn, DueOn = dueOn };
        receipt.AddCostItem(costType, amount, propertyId, Period.CalendarYear(2026));
        db.Add(receipt);
        await db.SaveChangesAsync(Ct);
    }

    private async Task<AnlageVReport> ReportAsync(int year)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IAnlageVService>().GetAsync(propertyId, year, false, Ct))!;
    }

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }

    [Fact]
    public async Task Rent_for_january_paid_on_28_december_goes_to_the_following_year()
    {
        var rent2026 = (await ReportAsync(2026)).Result.Income.Single(l => l.Info.Category == AnlageVCategory.BaseRent);
        var rent2027 = (await ReportAsync(2027)).Result.Income.Single(l => l.Info.Category == AnlageVCategory.BaseRent);

        Assert.Equal(2 * 620m, rent2026.Amount); // November and December
        Assert.Equal(620m, rent2027.Amount); // January, paid on 28 December
        Assert.Equal(new DateOnly(2026, 12, 28), Assert.Single(rent2027.Postings).Date);
        Assert.All(rent2027.Postings, p => Assert.Equal(lease, p.LeaseId)); // drilldown to the tenant account
    }

    [Fact]
    public async Task Receipts_count_by_payment_date_and_every_amount_has_a_source()
    {
        await AddReceiptAsync("betrkv-01", 300m, new DateOnly(2026, 3, 1));
        await AddReceiptAsync("betrkv-13", 480m, new DateOnly(2026, 12, 30), new DateOnly(2027, 1, 5)); // recurring: 10-day rule
        await AddReceiptAsync("betrkv-02", 90m, null); // unpaid

        var report = await ReportAsync(2026);

        Assert.Equal(300m, report.Result.Expenses.Single(l => l.Info.Category == AnlageVCategory.PropertyTax).Amount);
        Assert.Equal(0m, report.Result.Expenses.Single(l => l.Info.Category == AnlageVCategory.Insurance).Amount);
        Assert.Equal(480m, (await ReportAsync(2027)).Result.Expenses.Single(l => l.Info.Category == AnlageVCategory.Insurance).Amount);
        Assert.Contains(report.Warnings, w => w.Contains("noch nicht bezahlt", StringComparison.Ordinal));
        Assert.All(report.Result.Income.Concat(report.Result.Expenses), l =>
        {
            Assert.Equal(l.Amount, l.Postings.Sum(p => p.Amount));
            Assert.All(l.Postings, p => Assert.NotNull(p.SourceId));
        });
    }

    [Fact]
    public async Task Line_numbers_are_maintained_per_tax_year()
    {
        await using (var scope = environment.Services.CreateAsyncScope())
        {
            Validate(await scope.ServiceProvider.GetRequiredService<IAnlageVService>().SaveMappingAsync(2026, new Dictionary<AnlageVCategory, int> { [AnlageVCategory.BaseRent] = 9 }, Ct));
        }

        Assert.Equal(9, (await ReportAsync(2026)).Result.Income.Single(l => l.Info.Category == AnlageVCategory.BaseRent).LineNumber);
        Assert.NotEqual(9, (await ReportAsync(2027)).Result.Income.Single(l => l.Info.Category == AnlageVCategory.BaseRent).LineNumber);
    }

    [Fact]
    public async Task Cheap_letting_reduces_the_expenses_and_the_forecast_keeps_them_between_50_and_66_percent()
    {
        await AddReceiptAsync("betrkv-01", 300m, new DateOnly(2026, 3, 1));
        await using (var scope = environment.Services.CreateAsyncScope())
        {
            // Rent 2026: 2 x 770 = 1,540; 1,540 / 2,800 = 55 %
            Validate(await scope.ServiceProvider.GetRequiredService<IAnlageVService>().SaveLocalRentAsync(propertyId, 2026, 2_800m, Ct));
        }

        var without = await ReportAsync(2026);
        Assert.Equal(55m, without.RentPercent);
        Assert.Equal(135m, without.Result.Reduction);
        Assert.Equal(165m, without.Result.DeductibleExpenses);

        await using var other = environment.Services.CreateAsyncScope();
        var withForecast = (await other.ServiceProvider.GetRequiredService<IAnlageVService>().GetAsync(propertyId, 2026, true, Ct))!;
        Assert.Equal(0m, withForecast.Result.Reduction);
        Assert.Contains(withForecast.Warnings, w => w.Contains("Totalüberschussprognose", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Local_rent_can_be_removed_again()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IAnlageVService>();
        Validate(await service.SaveLocalRentAsync(propertyId, 2026, 1_000m, Ct));
        Validate(await service.SaveLocalRentAsync(propertyId, 2026, null, Ct));

        Assert.Null((await service.GetAsync(propertyId, 2026, false, Ct))!.LocalRent);
        Assert.False((await service.SaveLocalRentAsync(propertyId, 2026, -5m, Ct)).Succeeded);
    }

    [Fact]
    public async Task Unknown_property_has_no_report()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<IAnlageVService>().GetAsync(Guid.NewGuid(), 2026, false, Ct));
    }
}
