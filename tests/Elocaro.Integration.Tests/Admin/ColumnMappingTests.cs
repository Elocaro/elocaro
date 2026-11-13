using System.Text;
using Elocaro.Application.Properties;
using Elocaro.Application.Setup;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Admin;

/// <summary>A file whose headings the import does not know can be mapped by hand; the mapping only renames the headings, the rows go through the same checks.</summary>
public class ColumnMappingTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Foreign =
        "Gebäude;Mieteinheit;Bewohner;Netto;Einzug;Hausanschrift;Postleitzahl;Stadt;Bemerkung\r\n" +
        "Lindenweg 3;Wohnung 1;Beispiel;650,00;01.01.2024;Lindenweg 3;50667;Köln;nichts\r\n" +
        "Lindenweg 3;Wohnung 2;;;;;;;\r\n";

    private InfrastructureEnvironment environment = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => environment = await InfrastructureEnvironment.CreateAsync(postgres);

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private Task<T> WithAsync<T>(Func<IDataImport, Task<T>> action) => WithAsync<IDataImport, T>(action);

    private async Task<T> WithAsync<TService, T>(Func<TService, Task<T>> action)
        where TService : notnull
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    [Fact]
    public async Task Every_heading_of_the_template_is_recognized_by_the_import_itself()
    {
        var analysis = await WithAsync(i => i.AnalyzeAsync(Bytes(string.Join(';', Elocaro.Infrastructure.Setup.ImportSheet.Targets.Select(t => t.Label)) + "\r\n"), Ct));

        Assert.Empty(analysis.Missing);
        Assert.Equal(analysis.Target.Select(t => t.Key).Order(), analysis.Recognized.Values.Order());
    }

    [Fact]
    public async Task Unknown_headings_leave_the_required_columns_missing_and_show_samples()
    {
        var analysis = await WithAsync(i => i.AnalyzeAsync(Bytes(Foreign), Ct));

        Assert.True(analysis.NeedsMapping);
        Assert.Equal(["Objekt", "Einheit"], analysis.Missing.Select(m => m.Label));
        Assert.Equal("Lindenweg 3", analysis.Source[0].Samples[0]);
        Assert.Equal("Gebäude", analysis.Source[0].Header);
    }

    [Fact]
    public async Task Without_a_mapping_the_file_is_refused_and_with_one_it_previews_and_imports()
    {
        Assert.False((await WithAsync(i => i.PreviewAsync(Bytes(Foreign), Ct))).CanImport);
        var mapping = new Dictionary<int, string> { [0] = "property", [1] = "unit", [2] = "lastName", [3] = "baseRent", [4] = "start", [5] = "street", [6] = "postalCode", [7] = "city" };

        var preview = await WithAsync(i => i.PreviewAsync(Bytes(Foreign), mapping, Ct));

        Assert.True(preview.CanImport, string.Join(" | ", preview.FileErrors.Concat(preview.Rows.SelectMany(r => r.Errors))));
        Assert.Equal(2, preview.Rows.Count);
        Assert.Equal("Lindenweg 3", preview.Rows[0].Property);
        Assert.Equal("Beispiel", preview.Rows[0].Tenant);

        var result = await WithAsync(i => i.ImportAsync(Bytes(Foreign), mapping, Ct));
        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        var house = Assert.Single(await WithAsync<IPropertyService, IReadOnlyList<PropertyOverview>>(p => p.ListAsync(null, Ct)));
        Assert.Equal(2, house.Units);
    }

    [Fact]
    public async Task A_column_without_assignment_is_ignored_even_if_its_heading_looks_like_a_known_one()
    {
        // "Name" would be read as the last name by the heading alone.
        var csv = "Gebäude;Mieteinheit;Name;Straße;PLZ;Ort\r\nLindenweg 3;Wohnung 1;Hausmeister Müller;Lindenweg 3;50667;Köln\r\n";
        var mapping = new Dictionary<int, string> { [0] = "property", [1] = "unit", [3] = "street", [4] = "postalCode", [5] = "city" };

        var preview = await WithAsync(i => i.PreviewAsync(Bytes(csv), mapping, Ct));

        Assert.True(preview.CanImport, string.Join(" | ", preview.FileErrors));
        Assert.Null(preview.Rows[0].Tenant);
    }

    [Fact]
    public async Task Wrong_mappings_are_refused_with_a_message()
    {
        var unknown = await WithAsync(i => i.PreviewAsync(Bytes(Foreign), new Dictionary<int, string> { [0] = "property", [1] = "unit", [2] = "nonsense" }, Ct));
        var twice = await WithAsync(i => i.PreviewAsync(Bytes(Foreign), new Dictionary<int, string> { [0] = "property", [1] = "property", [2] = "unit" }, Ct));
        var missing = await WithAsync(i => i.PreviewAsync(Bytes(Foreign), new Dictionary<int, string> { [0] = "property" }, Ct));

        Assert.Contains(unknown.FileErrors, e => e.Contains("nonsense", StringComparison.Ordinal));
        Assert.Contains(twice.FileErrors, e => e.Contains("mehreren", StringComparison.Ordinal));
        Assert.Contains(missing.FileErrors, e => e.Contains("Einheit", StringComparison.Ordinal));
        Assert.All(new[] { unknown, twice, missing }, p => Assert.False(p.CanImport));
    }
}
