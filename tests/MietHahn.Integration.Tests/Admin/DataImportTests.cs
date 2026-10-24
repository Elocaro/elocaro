using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Admin;
using MietHahn.Application.Finance;
using MietHahn.Application.Leases;
using MietHahn.Application.People;
using MietHahn.Application.Properties;
using MietHahn.Domain.Finance;
using MietHahn.Domain.Leases;
using MietHahn.Infrastructure.Admin;
using MietHahn.Infrastructure.Persistence;
using MietHahn.Integration.Tests.Infrastructure;

namespace MietHahn.Integration.Tests.Admin;

public class DataImportTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Header = "Objekt;Straße;PLZ;Ort;Einheit;Lage;Wohnfläche;Vorname;Nachname;E-Mail;Mietbeginn;Kaltmiete;Betriebskosten;Heizkosten;Kaution;Saldo\r\n";

    private InfrastructureEnvironment environment = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => environment = await InfrastructureEnvironment.CreateAsync(postgres);

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    private static byte[] File(string rows, bool header = true) => Encoding.UTF8.GetBytes((header ? Header : "") + rows);

    [Fact]
    public async Task The_template_is_a_valid_file_and_previews_what_it_would_create()
    {
        var template = await WithAsync(i => Task.FromResult(i.TemplateCsv()));

        var preview = await WithAsync(i => i.PreviewAsync(template, Ct));

        Assert.True(preview.CanImport, string.Join(" | ", preview.Rows.SelectMany(r => r.Errors).Concat(preview.FileErrors)));
        Assert.Equal(new DataImportSummary(1, 2, 1, 1, 0, 0), preview.Summary);
        Assert.Equal(2, preview.Rows.Count);
        Assert.Contains("Objekt neu", preview.Rows[0].Actions);
        Assert.Contains("Einheit neu", preview.Rows[0].Actions);
        Assert.Contains("Mieter neu", preview.Rows[0].Actions);
        Assert.Contains("Vertrag neu ab 01.01.2024", preview.Rows[0].Actions);
        Assert.Contains("Objekt wie oben", preview.Rows[1].Actions);
        Assert.Null(preview.Rows[1].Tenant);
        Assert.Empty(await WithAsync<IPropertyService, IReadOnlyList<PropertyOverview>>(p => p.ListAsync(null, Ct)));
    }

    [Fact]
    public async Task Import_creates_properties_units_with_area_tenants_leases_and_deposit_and_a_second_run_creates_nothing_again()
    {
        var template = await WithAsync(i => Task.FromResult(i.TemplateCsv()));

        var first = await WithAsync(i => i.ImportAsync(template, Ct));

        Assert.True(first.Succeeded, string.Join(" | ", first.Errors));
        Assert.Equal(new DataImportSummary(1, 2, 1, 1, 0, 0), first.Summary);
        var house = Assert.Single(await WithAsync<IPropertyService, IReadOnlyList<PropertyOverview>>(p => p.ListAsync(null, Ct)));
        Assert.Equal(("Lindenweg 3", 2), (house.Name, house.Units));
        Assert.Equal(55.5m + 62m, house.LivingArea);

        await using (var scope = environment.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
            var lease = await db.Set<Lease>().AsNoTracking().Include(v => v.Components).Include(v => v.Parties).SingleAsync(Ct);
            Assert.Equal(new DateOnly(2024, 1, 1), lease.Start);
            Assert.Equal(650m + 150m + 80m, lease.MonthlyRentOn(new DateOnly(2026, 1, 1)));
            Assert.Equal(1950m, lease.Deposit);
            Assert.Single(lease.Tenant);
        }

        var second = await WithAsync(i => i.ImportAsync(template, Ct));

        Assert.True(second.Succeeded, string.Join(" | ", second.Errors));
        Assert.Equal(new DataImportSummary(0, 0, 0, 0, 0, 2), second.Summary);
        Assert.Single(await WithAsync<IPropertyService, IReadOnlyList<PropertyOverview>>(p => p.ListAsync(null, Ct)));
        await using var check = environment.Services.CreateAsyncScope();
        var dbCheck = check.ServiceProvider.GetRequiredService<MietHahnDbContext>();
        Assert.Equal(1, await dbCheck.Set<Lease>().CountAsync(Ct));
        Assert.Equal(1, await dbCheck.Set<MietHahn.Domain.People.Person>().CountAsync(Ct));
    }

    [Fact]
    public async Task Existing_properties_people_and_units_are_reused_by_name_and_email()
    {
        await using (var scope = environment.Services.CreateAsyncScope())
        {
            var properties = scope.ServiceProvider.GetRequiredService<IPropertyService>();
            var house = (await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct)).Id!.Value;
            await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 1" }, Ct);
            await scope.ServiceProvider.GetRequiredService<IPersonService>().CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Beispiel", Email = "ANNA@example.de" }, null, Ct);
        }

        var rows = "lindenweg 3;;;;wohnung 1;;48;Anna;Beispiel;anna@example.de;01.02.2025;500;100;;;\r\n";
        var preview = await WithAsync(i => i.PreviewAsync(File(rows), Ct));
        var row = Assert.Single(preview.Rows);

        Assert.True(preview.CanImport, string.Join(" | ", row.Errors));
        Assert.Equal(["Objekt vorhanden", "Einheit vorhanden", "Fläche 48 m²", "Mieter vorhanden", "Vertrag neu ab 01.02.2025"], row.Actions);

        Assert.True((await WithAsync(i => i.ImportAsync(File(rows), Ct))).Succeeded);
        await using var check = environment.Services.CreateAsyncScope();
        var db = check.ServiceProvider.GetRequiredService<MietHahnDbContext>();
        Assert.Equal(1, await db.Set<MietHahn.Domain.People.Person>().CountAsync(Ct));
        Assert.Equal(1, await db.Set<MietHahn.Domain.Properties.Property>().CountAsync(Ct));
        Assert.Equal(1, await db.Set<Lease>().CountAsync(Ct));
    }

    [Fact]
    public async Task Opening_balance_becomes_a_carried_forward_charge_and_a_credit_a_refund()
    {
        var rows = "Haus;Weg 1;50667;Köln;W1;;50;Anna;Eins;;01.01.2025;500;;;;125,50\r\n" +
                   "Haus;;;;W2;;60;Ben;Zwei;;01.01.2025;600;;;;-40\r\n";

        var result = await WithAsync(i => i.ImportAsync(File(rows), Ct));

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Equal(2, result.Summary.OpeningBalances);
        await using var scope = environment.Services.CreateAsyncScope();
        var charges = await scope.ServiceProvider.GetRequiredService<MietHahnDbContext>().Set<Charge>().AsNoTracking().OrderBy(c => c.Amount).ToListAsync(Ct);
        Assert.Equal([(ChargeType.Refund, -40m), (ChargeType.BalanceCarriedForward, 125.5m)], charges.Select(c => (c.Kind, c.Amount)));
    }

    [Fact]
    public async Task Errors_in_the_file_are_reported_per_row_and_nothing_is_written()
    {
        var rows = "Haus;Weg 1;50667;Köln;W1;;abc;Anna;Eins;;01.01.2025;500;;;;\r\n" +
                   "Haus;;;;W2;;60;Ben;Zwei;kaputt;31.02.2025;;;;;\r\n" +
                   "Neu;Weg 2;1234;Köln;W3;;;;;;;;;;;\r\n" +
                   ";;;;W5;;;;;;;;;;;\r\n";

        var preview = await WithAsync(i => i.PreviewAsync(File(rows), Ct));
        var result = await WithAsync(i => i.ImportAsync(File(rows), Ct));

        Assert.False(preview.CanImport);
        Assert.Contains(preview.Rows[0].Errors, e => e.Contains("Wohnfläche „abc“ ist keine Zahl", StringComparison.Ordinal));
        Assert.Contains(preview.Rows[1].Errors, e => e.Contains("Mietbeginn „31.02.2025“ ist kein Datum", StringComparison.Ordinal));
        Assert.Contains(preview.Rows[1].Errors, e => e.Contains("E-Mail-Adresse „kaputt“", StringComparison.Ordinal));
        Assert.Contains(preview.Rows[2].Errors, e => e.Contains("Postleitzahl „1234“", StringComparison.Ordinal));
        Assert.Contains(preview.Rows[3].Errors, e => e.Contains("Objekt fehlt", StringComparison.Ordinal));
        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.StartsWith("Zeile 2:", StringComparison.Ordinal));
        Assert.Empty(await WithAsync<IPropertyService, IReadOnlyList<PropertyOverview>>(p => p.ListAsync(null, Ct)));
    }

    [Fact]
    public async Task A_lease_that_overlaps_an_existing_one_is_an_error_and_a_second_running_lease_in_the_file_too()
    {
        var first = "Haus;Weg 1;50667;Köln;W1;;50;Anna;Eins;;01.01.2025;500;;;;\r\n";
        Assert.True((await WithAsync(i => i.ImportAsync(File(first), Ct))).Succeeded);

        var overlapping = await WithAsync(i => i.PreviewAsync(File("Haus;;;;W1;;;Ben;Zwei;;01.06.2025;600;;;;\r\n"), Ct));
        Assert.Contains(Assert.Single(overlapping.Rows).Errors, e => e.Contains("schon einen Vertrag ab 01.01.2025", StringComparison.Ordinal));

        var twice = await WithAsync(i => i.PreviewAsync(File("Haus2;Weg 2;50667;Köln;W9;;50;Cem;Drei;;01.01.2025;500;;;;\r\nHaus2;;;;W9;;;Dora;Vier;;01.03.2025;500;;;;\r\n"), Ct));
        Assert.Contains(twice.Rows[1].Errors, e => e.Contains("weiteren laufenden Vertrag", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Missing_columns_empty_files_and_files_without_rows_are_file_errors()
    {
        var noUnit = await WithAsync(i => i.PreviewAsync(Encoding.UTF8.GetBytes("Objekt;Ort\r\nHaus;Köln\r\n"), Ct));
        var empty = await WithAsync(i => i.PreviewAsync([], Ct));
        var onlyHeader = await WithAsync(i => i.PreviewAsync(File("", header: true), Ct));

        Assert.Contains(noUnit.FileErrors, e => e.Contains("„Einheit“ fehlt", StringComparison.Ordinal));
        Assert.Contains(empty.FileErrors, e => e.Contains("leer", StringComparison.Ordinal));
        Assert.Contains(onlyHeader.FileErrors, e => e.Contains("keine Zeilen", StringComparison.Ordinal));
        Assert.All([noUnit, empty, onlyHeader], p => Assert.False(p.CanImport));
    }

    [Fact]
    public async Task Excel_style_files_are_read_with_tabs_windows_1252_quotes_and_german_numbers()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var text = "Objekt\tEinheit\tWohnfläche\tNachname\tMietbeginn\tKaltmiete\r\n" +
                   "\"Haus \"\"Am Eck\"\"\"\t\"Wohnung 1\"\t\"1.234,5\"\tMüller\t1.2.24\t\"1.050,00 €\"\r\n";

        var (lines, errors) = ImportSheet.Read(Encoding.GetEncoding(1252).GetBytes(text));

        Assert.Empty(errors);
        var line = Assert.Single(lines);
        Assert.Empty(line.Errors);
        Assert.Equal("Haus \"Am Eck\"", line.Property);
        Assert.Equal(1234.5m, line.Area);
        Assert.Equal("Müller", line.LastName);
        Assert.Equal(new DateOnly(2024, 2, 1), line.Start);
        Assert.Equal(1050m, line.BaseRent);
    }

    [Theory]
    [InlineData("650", 650)]
    [InlineData("650,5", 650.5)]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1.234", 1234)]
    [InlineData("650.5", 650.5)]
    [InlineData("-40", -40)]
    [InlineData("1 050,00 €", 1050)]
    public void Numbers_are_read_in_german_and_plain_form(string text, double expected)
    {
        Assert.True(ImportSheet.TryNumber(text, out var value));
        Assert.Equal((decimal)expected, value);
    }

    private async Task<T> WithAsync<T>(Func<IDataImport, Task<T>> action) => await WithAsync<IDataImport, T>(action);

    private async Task<TResult> WithAsync<TService, TResult>(Func<TService, Task<TResult>> action)
        where TService : notnull
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
