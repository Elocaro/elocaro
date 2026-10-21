using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.Common;
using MietHahn.Application.Finance;
using MietHahn.Application.Leases;
using MietHahn.Application.People;
using MietHahn.Application.Properties;
using MietHahn.Application.Tax;
using MietHahn.Domain.Common;
using MietHahn.Domain.Datev;
using MietHahn.Domain.Receipts;
using MietHahn.Domain.Tax;
using MietHahn.Infrastructure.Persistence;
using MietHahn.Integration.Tests.Infrastructure;

namespace MietHahn.Integration.Tests.Tax;

public class DatevExportTests(PostgresFixture postgres) : IAsyncLifetime
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
            new PersonInput { FirstName = "Anna", LastName = "Schmidt", Salutation = MietHahn.Domain.People.Salutation.Mrs, Email = "anna@example.de" }, null, Ct));
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
        var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
        var receipt = new Receipt(new DateOnly(2026, 2, 1), amount) { SupplierName = "Stadt Köln", PaidOn = paidOn, DueOn = dueOn };
        receipt.AddCostItem(costType, amount, propertyId, Period.CalendarYear(2026));
        db.Add(receipt);
        await db.SaveChangesAsync(Ct);
    }

    private async Task<TResult> With<TResult>(Func<IDatevExport, Task<TResult>> action)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IDatevExport>());
    }

    private Task<Result> ConfigureAsync(int accountLength = 4) =>
        With(d => d.SaveSettingsAsync(new DatevSettingsInput { ConsultantNumber = 1001, ClientNumber = 4711, AccountLength = accountLength }, Ct));

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }

    private static readonly DateOnly Start = new(2026, 11, 1);
    private static readonly DateOnly End = new(2026, 12, 31);

    [Fact]
    public async Task Payments_and_paid_receipts_become_bookings_by_payment_date_with_the_mapped_accounts()
    {
        await AddReceiptAsync("betrkv-01", 300m, new DateOnly(2026, 12, 3));
        await AddReceiptAsync("betrkv-02", 90m, null);

        var preview = await With(d => d.PreviewAsync(new DateOnly(2026, 1, 1), End, false, Ct));

        var rent = preview.Bookings.Where(b => b.SourceKey.StartsWith("payment:", StringComparison.Ordinal)).ToList();
        Assert.Equal(3 * 770m, rent.Sum(b => b.Amount)); // 5 Nov, 4 Dec and 28 Dec, each split into base rent and prepayment
        Assert.All(rent, b => Assert.True(b.Debit && b.Account == 1200));
        Assert.Contains(rent, b => b.CounterAccount == 8105);
        Assert.Contains(rent, b => b.CounterAccount == 8110);

        var tax = Assert.Single(preview.Bookings, b => b.SourceKey.StartsWith("cost:", StringComparison.Ordinal));
        Assert.Equal((300m, true, 4270, 1200), (tax.Amount, tax.Debit, tax.Account, tax.CounterAccount));
        Assert.Equal(new DateOnly(2026, 12, 3), tax.Date);
        Assert.Contains(preview.Warnings, w => w.Contains("noch kein Zahlungsdatum", StringComparison.Ordinal));
        Assert.False(preview.SettingsComplete);
    }

    [Fact]
    public async Task Export_needs_the_client_data_stays_in_the_fiscal_year_and_is_logged()
    {
        await AddReceiptAsync("betrkv-01", 300m, new DateOnly(2026, 12, 3));

        Assert.False((await With(d => d.ExportAsync(Start, End, false, false, "Admin", Ct))).Succeeded);
        Validate(await ConfigureAsync());
        Assert.False((await With(d => d.ExportAsync(new DateOnly(2026, 6, 1), new DateOnly(2027, 6, 30), false, false, "Admin", Ct))).Succeeded);

        var id = Validate(await With(d => d.ExportAsync(Start, End, true, false, "Admin", Ct)));

        var file = (await With(d => d.FileAsync(id, Ct)))!;
        Assert.Equal("EXTF_Buchungsstapel_20261101_20261231.csv", file.FileName);
        var lines = System.Text.Encoding.GetEncoding(1252).GetString(file.Content).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("\"EXTF\";700;21;\"Buchungsstapel\";13;", lines[0], StringComparison.Ordinal);
        Assert.Contains(";1001;4711;20260101;4;20261101;20261231;", lines[0], StringComparison.Ordinal);
        Assert.Contains(";1;0;1;\"EUR\";", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("Umsatz (ohne Soll/Haben-Kz);", lines[1], StringComparison.Ordinal);
        Assert.Equal(2 + 7, lines.Length);
        Assert.Contains("300,00;\"S\";\"EUR\";;;;4270;1200;;0312;\"\";;;\"Stadt Köln – Laufende öffentliche Lasten (Grundsteuer)\"", lines);

        var row = Assert.Single(await With(d => d.ExportsAsync(Ct)));
        Assert.Equal((7, true), (row.Count, row.FixedPosting));
        Assert.Equal(3 * 770m + 300m, row.Total);
    }

    [Fact]
    public async Task An_item_is_not_exported_twice_unless_asked_for()
    {
        Validate(await ConfigureAsync());
        Validate(await With(d => d.ExportAsync(Start, End, false, false, "Admin", Ct)));

        var again = await With(d => d.PreviewAsync(Start, End, false, Ct));
        Assert.Empty(again.Bookings);
        Assert.Equal(6, again.AlreadyExported);
        Assert.False((await With(d => d.ExportAsync(Start, End, false, false, "Admin", Ct))).Succeeded);

        await AddReceiptAsync("betrkv-01", 50m, new DateOnly(2026, 12, 10));
        Assert.Single((await With(d => d.PreviewAsync(Start, End, false, Ct))).Bookings);
        Assert.Equal(7, (await With(d => d.PreviewAsync(Start, End, true, Ct))).Bookings.Count);
        Validate(await With(d => d.ExportAsync(Start, End, false, true, "Admin", Ct)));
        Assert.Equal(2, (await With(d => d.ExportsAsync(Ct))).Count);
        Assert.Empty((await With(d => d.PreviewAsync(Start, End, false, Ct))).Bookings);
    }

    [Fact]
    public async Task Accounts_can_be_changed_follow_the_chart_and_are_validated()
    {
        await AddReceiptAsync("betrkv-01", 300m, new DateOnly(2026, 12, 3));
        Validate(await ConfigureAsync());

        Validate(await With(d => d.SaveAccountsAsync(new Dictionary<string, int> { [DatevAccountKeys.Cost("betrkv-01")] = 4281, [DatevAccountKeys.Bank] = 1210 }, Ct)));
        var accounts = await With(d => d.AccountsAsync(Ct));
        Assert.False(accounts.Single(a => a.Key == DatevAccountKeys.Bank).IsDefault);
        var booking = (await With(d => d.PreviewAsync(Start, End, false, Ct))).Bookings.Single(b => b.SourceKey.StartsWith("cost:", StringComparison.Ordinal));
        Assert.Equal((4281, 1210), (booking.Account, booking.CounterAccount));

        Assert.False((await With(d => d.SaveAccountsAsync(new Dictionary<string, int> { ["cost:gibtesnicht"] = 4000 }, Ct))).Succeeded);
        Validate(await With(d => d.ResetAccountsAsync(Ct)));
        Assert.True((await With(d => d.AccountsAsync(Ct))).All(a => a.IsDefault));

        Validate(await With(d => d.SaveSettingsAsync(new DatevSettingsInput { ConsultantNumber = 1001, ClientNumber = 4711, Chart = DatevChart.Skr04 }, Ct)));
        Assert.Equal(1800, (await With(d => d.AccountsAsync(Ct))).Single(a => a.Key == DatevAccountKeys.Bank).Account);
        Assert.False((await With(d => d.SaveSettingsAsync(new DatevSettingsInput { ConsultantNumber = 5, ClientNumber = 1 }, Ct))).Succeeded);
    }

    [Fact]
    public async Task Accounts_that_do_not_fit_the_account_length_stop_the_export_and_longer_accounts_are_padded()
    {
        await AddReceiptAsync("betrkv-01", 300m, new DateOnly(2026, 12, 3));
        Validate(await ConfigureAsync());
        Validate(await With(d => d.SaveAccountsAsync(new Dictionary<string, int> { [DatevAccountKeys.Cost("betrkv-01")] = 42700 }, Ct)));
        Assert.False((await With(d => d.ExportAsync(Start, End, false, false, "Admin", Ct))).Succeeded);

        Validate(await ConfigureAsync(accountLength: 6));
        var id = Validate(await With(d => d.ExportAsync(Start, End, false, false, "Admin", Ct)));
        var text = System.Text.Encoding.GetEncoding(1252).GetString((await With(d => d.FileAsync(id, Ct)))!.Content);
        Assert.Contains(";427000;120000;", text, StringComparison.Ordinal);
        Assert.Contains(";120000;810500;", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Plain_booking_list_has_a_header_and_german_numbers()
    {
        var csv = System.Text.Encoding.UTF8.GetString(await With(d => d.BookingsCsvAsync(Start, End, Ct)));

        Assert.StartsWith("\uFEFFDatum;Soll/Haben;Konto;Gegenkonto;Betrag;Belegnummer;Text\r\n", csv, StringComparison.Ordinal);
        Assert.Contains(";620,00;", csv, StringComparison.Ordinal);
    }
}
