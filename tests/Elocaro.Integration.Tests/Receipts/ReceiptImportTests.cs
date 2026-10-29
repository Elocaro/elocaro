using System.Text;
using Elocaro.Application.Common;
using Elocaro.Application.Properties;
using Elocaro.Application.Receipts;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Receipts;

public class ReceiptImportTests(PostgresFixture postgres) : IAsyncLifetime
{
    private InfrastructureEnvironment environment = null!;
    private Guid propertyId;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres);
        await using var scope = environment.Services.CreateAsyncScope();
        var created = await scope.ServiceProvider.GetRequiredService<IPropertyService>().CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct);
        propertyId = created.Id!.Value;
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    private static byte[] Csv(params string[] lines) => Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + "\r\n");

    [Fact]
    public async Task Cash_book_rows_become_receipts_by_group_and_a_second_import_changes_nothing()
    {
        var csv = Csv(
            "Datum;Gruppe;Empfänger;Betrag;Info",
            "15.03.2025;Gebäudeversicherung;Allianz;-1.242,31;Jahresbeitrag",
            "02.04.2025;Müllabfuhr;Landratsamt;173,08;Gebühren",
            "10.05.2025;Sonstiges Zeug;Baumarkt;49,90;Farbe",
            "kein Datum;Müllabfuhr;Landratsamt;10,00;");

        var preview = await WithAsync<IReceiptImport, ReceiptImportPreview>(i => i.PreviewAsync(propertyId, csv, null, Ct));

        Assert.Equal(["Gebäudeversicherung", "Müllabfuhr", "Sonstiges Zeug"], preview.Groups.Select(g => g.Name));
        Assert.Equal(["betrkv-13", "betrkv-08", null], preview.Groups.Select(g => g.CostType));
        Assert.Equal([ReceiptRowStatus.New, ReceiptRowStatus.New, ReceiptRowStatus.NoCostType, ReceiptRowStatus.Invalid], preview.Rows.Select(r => r.Status));
        Assert.Equal(1242.31m, preview.Rows[0].Amount);

        // The user assigns the third group, then everything with a cost type is imported.
        var mapping = new Dictionary<string, string?> { ["Sonstiges Zeug"] = "betrkv-17" };
        var outcome = await WithAsync<IReceiptImport, ReceiptImportOutcome>(i => i.ImportAsync(propertyId, csv, mapping, Ct));
        Assert.True(outcome.Succeeded, string.Join(", ", outcome.Errors));
        Assert.Equal((3, 1), (outcome.Imported, outcome.Skipped));

        var rows = await WithAsync<IReceiptService, IReadOnlyList<ReceiptRow>>(s => s.SearchAsync(new ReceiptQuery(Year: 2025), Ct));
        Assert.Equal([1242.31m, 173.08m, 49.9m], rows.Select(r => r.Gross).OrderByDescending(x => x));

        var again = await WithAsync<IReceiptImport, ReceiptImportPreview>(i => i.PreviewAsync(propertyId, csv, mapping, Ct));
        Assert.Equal(3, again.Rows.Count(r => r.Status == ReceiptRowStatus.Duplicate));
        Assert.False((await WithAsync<IReceiptImport, ReceiptImportOutcome>(i => i.ImportAsync(propertyId, csv, mapping, Ct))).Succeeded);
    }

    [Fact]
    public async Task A_file_without_the_needed_columns_is_rejected_with_a_message()
    {
        var preview = await WithAsync<IReceiptImport, ReceiptImportPreview>(i => i.PreviewAsync(propertyId, Csv("a;b;c", "1;2;3"), null, Ct));

        Assert.Contains("Datum", Assert.Single(preview.Errors), StringComparison.Ordinal);
        Assert.False(preview.CanImport);
    }

    private async Task<T> WithAsync<TService, T>(Func<TService, Task<T>> action)
        where TService : notnull
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
