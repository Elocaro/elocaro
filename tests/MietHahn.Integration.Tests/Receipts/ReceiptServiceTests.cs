using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.Common;
using MietHahn.Application.Files;
using MietHahn.Application.People;
using MietHahn.Application.Properties;
using MietHahn.Application.Receipts;
using MietHahn.Application.Todos;
using MietHahn.Domain.People;
using MietHahn.Domain.Receipts;
using MietHahn.Integration.Tests.Infrastructure;

namespace MietHahn.Integration.Tests.Receipts;

public class ReceiptServiceTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly ClaimsPrincipal Admin = new(new ClaimsIdentity([new Claim(ClaimTypes.Role, Roles.Admin)], "test"));

    private InfrastructureEnvironment environment = null!;
    private Guid house;
    private Guid otherHouse;
    private Guid apartment;
    private Guid caretakerService;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres);
        await using var scope = environment.Services.CreateAsyncScope();
        var properties = scope.ServiceProvider.GetRequiredService<IPropertyService>();
        house = Validate(await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct));
        otherHouse = Validate(await properties.CreateAsync(new PropertyInput { Name = "Birkenallee 9", Street = "Birkenallee 9", PostalCode = "50668", City = "Köln" }, Ct));
        apartment = Validate(await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 1" }, Ct));
        caretakerService = Validate(await scope.ServiceProvider.GetRequiredService<IPersonService>().CreateAsync(
            new PersonInput { LastName = "Grünpflege Meier", Company = "Meier GmbH", ContactType = ContactType.ServiceProvider }, null, Ct));
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Split_invoice_into_two_cost_types_sum_is_validated()
    {
        var file = await FileAsync();
        var id = Validate(await WithAsync(b => b.CreateAsync(new ReceiptInput
        {
            SupplierId = caretakerService,
            InvoiceNumber = "R-2026-117",
            InvoiceDate = new DateOnly(2026, 4, 30),
            ServiceFrom = new DateOnly(2026, 1, 1),
            ServiceTo = new DateOnly(2026, 3, 31),
            Gross = 595m,
            Net = 500m,
            Vat = 95m,
            DueOn = new DateOnly(2026, 5, 14),
        }, [file], null, Ct)));

        Validate(await WithAsync(b => b.AddItemAsync(id, new CostItemInput
        {
            CostType = "betrkv-14",
            Amount = 357m,
            PropertyId = house,
            Section35a = Section35a.HouseholdService,
            LaborCosts = 357m,
        }, Ct)));
        var tooMuch = await WithAsync(b => b.AddItemAsync(id, new CostItemInput { CostType = "betrkv-10", Amount = 300m, PropertyId = house }, Ct));
        Assert.Contains("übersteigen den Rechnungsbetrag (offen: 238,00 €)", tooMuch.Errors.Single(), StringComparison.Ordinal);
        // Without an amount: the remainder
        Validate(await WithAsync(b => b.AddItemAsync(id, new CostItemInput { CostType = "betrkv-10", PropertyId = house }, Ct)));

        var receipt = (await WithAsync(b => b.GetAsync(id, Ct)))!;
        Assert.Equal((595m, 0m), (receipt.Allocated, receipt.Open));
        Assert.Equal([("Hauswart", 357m), ("Gartenpflege", 238m)], receipt.Items.Select(p => (p.CostTypeText, p.Amount)));
        Assert.All(receipt.Items, p => Assert.Equal(new DateOnly(2026, 1, 1), p.BillingPeriod.From)); // from the service period
        Assert.All(receipt.Items, p => Assert.True(p.Allocable));
        Assert.Equal("Meier GmbH (Grünpflege Meier)", receipt.Supplier);
        Assert.Single(receipt.Files);

        // A gross amount below the sum of the items is not possible.
        receipt.Data.Gross = 500m;
        receipt.Data.Net = null;
        receipt.Data.Vat = null;
        Assert.False((await WithAsync(b => b.UpdateAsync(id, receipt.Data, Ct))).Succeeded);
    }

    [Fact]
    public async Task Receipt_list_filters_by_year_property_cost_type_payment_and_split()
    {
        var propertyTax = await ReceiptAsync("Stadt Köln", new DateOnly(2026, 2, 15), 480m, "betrkv-01", house, paid: new DateOnly(2026, 2, 15));
        var repair = await ReceiptAsync("Sanitär Schulz", new DateOnly(2025, 11, 3), 230m, "instandhaltung", otherHouse);
        var open = await ReceiptAsync("Versicherung AG", new DateOnly(2026, 1, 10), 900m, null, null);

        Assert.Equal([propertyTax, open], await IdsAsync(new ReceiptQuery(Year: 2026)));
        Assert.Equal([propertyTax], await IdsAsync(new ReceiptQuery(PropertyId: house)));
        Assert.Equal([repair], await IdsAsync(new ReceiptQuery(CostType: "instandhaltung")));
        Assert.Equal([open, repair], await IdsAsync(new ReceiptQuery(Unpaid: true)));
        Assert.Equal([open], await IdsAsync(new ReceiptQuery(Unallocated: true)));
        Assert.Equal([repair], await IdsAsync(new ReceiptQuery(Text: "schulz")));

        var line = (await WithAsync(b => b.SearchAsync(new ReceiptQuery(CostType: "instandhaltung"), Ct))).Single();
        Assert.Equal(["Instandhaltung und Reparaturen"], line.CostTypes);
        Assert.Equal(["Birkenallee 9"], line.Properties);
    }

    [Fact]
    public async Task Fuel_receipt_with_quantity_and_CO2()
    {
        var id = Validate(await WithAsync(b => b.CreateAsync(new ReceiptInput
        {
            SupplierName = "Heizöl Müller",
            InvoiceDate = new DateOnly(2026, 10, 1),
            Gross = 3150m,
            FuelQuantity = 3000m,
            FuelUnit = "l",
            Co2Kg = 7980m,
        }, [], new CostItemInput { CostType = "betrkv-04", PropertyId = house }, Ct)));

        var receipt = (await WithAsync(b => b.GetAsync(id, Ct)))!;
        Assert.Equal((3000m, "l", 7980m), (receipt.Data.FuelQuantity!.Value, receipt.Data.FuelUnit, receipt.Data.Co2Kg!.Value));
        Assert.Equal(Domain.Common.Period.CalendarYear(2026), receipt.Items.Single().BillingPeriod);
        Assert.Equal(3150m, receipt.Items.Single().Amount);
    }

    [Fact]
    public async Task Invalid_input_is_rejected()
    {
        Assert.Contains("Lieferanten", (await WithAsync(b => b.CreateAsync(new ReceiptInput { InvoiceDate = new DateOnly(2026, 1, 1), Gross = 10m }, [], null, Ct))).Errors.Single(), StringComparison.Ordinal);
        Assert.Contains("ergibt nicht den Bruttobetrag", (await WithAsync(b => b.CreateAsync(new ReceiptInput { SupplierName = "X", InvoiceDate = new DateOnly(2026, 1, 1), Gross = 119m, Net = 100m, Vat = 18m }, [], null, Ct))).Errors.Single(), StringComparison.Ordinal);
        Assert.False((await WithAsync(b => b.CreateAsync(new ReceiptInput { SupplierName = "X", InvoiceDate = new DateOnly(2026, 1, 1), Gross = 10m }, [Guid.CreateVersion7()], null, Ct))).Succeeded);

        var id = await ReceiptAsync("X", new DateOnly(2026, 1, 1), 100m, null, null);
        Assert.False((await WithAsync(b => b.AddItemAsync(id, new CostItemInput { CostType = "gibt-es-nicht", PropertyId = house }, Ct))).Succeeded);
        Assert.Contains("gehört nicht zum gewählten Objekt", (await WithAsync(b => b.AddItemAsync(id, new CostItemInput { CostType = "betrkv-02", PropertyId = otherHouse, UnitId = apartment }, Ct))).Errors.Single(), StringComparison.Ordinal);
        Assert.Contains("Arbeitskostenanteil", (await WithAsync(b => b.AddItemAsync(id, new CostItemInput { CostType = "instandhaltung", PropertyId = house, Section35a = Section35a.TradesmanService, LaborCosts = 150m }, Ct))).Errors.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Files_removing_line_items_deleting_and_dashboard()
    {
        environment.Clock.SetUtcNow(new DateTimeOffset(2026, 5, 10, 10, 0, 0, TimeSpan.Zero));
        var first = await FileAsync();
        var id = Validate(await WithAsync(b => b.CreateAsync(new ReceiptInput { SupplierName = "Stadtwerke", InvoiceDate = new DateOnly(2026, 5, 1), Gross = 120m, DueOn = new DateOnly(2026, 5, 15) }, [first], null, Ct)));
        var second = await FileAsync("seite2.pdf");
        Validate(await WithAsync(b => b.AddFilesAsync(id, [second], Ct)));
        Assert.Equal([1, 2], (await WithAsync(b => b.GetAsync(id, Ct)))!.Files.Select(d => d.Page));
        Validate(await WithAsync(b => b.RemoveFileAsync(id, second, Ct)));
        Assert.Single((await WithAsync(b => b.GetAsync(id, Ct)))!.Files);

        var position = Validate(await WithAsync(b => b.AddItemAsync(id, new CostItemInput { CostType = "betrkv-11", PropertyId = house }, Ct)));
        Validate(await WithAsync(b => b.RemoveCostItemAsync(id, position, Ct)));
        Assert.Empty((await WithAsync(b => b.GetAsync(id, Ct)))!.Items);

        var todos = await TodosAsync();
        Assert.Contains(todos, a => a.Title == "Rechnung zahlen: Stadtwerke 120,00 €" && a.Urgency == Urgency.Soon);
        Assert.Contains(todos, a => a.Title == "1 Belege ohne vollständige Kostenaufteilung");

        Validate(await WithAsync(b => b.DeleteAsync(id, Ct)));
        Assert.Null(await WithAsync(b => b.GetAsync(id, Ct)));
        Assert.Empty(await TodosAsync());
    }

    private async Task<List<Guid>> IdsAsync(ReceiptQuery query) => (await WithAsync(b => b.SearchAsync(query, Ct))).Select(z => z.Id).ToList();

    private async Task<Guid> ReceiptAsync(string supplier, DateOnly date, decimal amount, string? costType, Guid? property, DateOnly? paid = null) =>
        Validate(await WithAsync(b => b.CreateAsync(new ReceiptInput { SupplierName = supplier, InvoiceDate = date, Gross = amount, PaidOn = paid }, [],
            costType is null ? null : new CostItemInput { CostType = costType, PropertyId = property }, Ct)));

    private async Task<Guid> FileAsync(string name = "rechnung.pdf")
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IFileStore>().SaveAsync(new MemoryStream(TestFiles.Pdf(name)), name, Ct)).Id;
    }

    private async Task<IReadOnlyList<TodoItem>> TodosAsync()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetServices<ITodoSource>().Single(q => q.GetType().Name == "ReceiptTodos").TodosAsync(Admin, Ct);
    }

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }

    private async Task<T> WithAsync<T>(Func<IReceiptService, Task<T>> action)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IReceiptService>());
    }
}
