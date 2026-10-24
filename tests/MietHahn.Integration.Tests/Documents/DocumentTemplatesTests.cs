using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Common;
using MietHahn.Application.Documents;
using MietHahn.Domain.Documents;
using MietHahn.Domain.Files;
using MietHahn.Integration.Tests.Infrastructure;

namespace MietHahn.Integration.Tests.Documents;

public class DocumentTemplatesTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly CapturingPdfGenerator pdf = new();
    private InfrastructureEnvironment environment = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() =>
        environment = await InfrastructureEnvironment.CreateAsync(postgres, configure: s => s.AddSingleton<IPdfGenerator>(pdf));

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Every_change_of_the_text_keeps_a_revision_but_renaming_does_not()
    {
        var id = Validate(await WithAsync(t => t.SaveAsync(null, Input("Ankündigung"), Ct)));

        var changed = Input("Ankündigung");
        changed.Body = "<p>{{ salutation }}</p><p>Am Montag wird das Gerüst aufgestellt.</p>";
        Validate(await WithAsync(t => t.SaveAsync(id, changed, Ct)));
        changed.Name = "Ankündigung Gerüst";
        Validate(await WithAsync(t => t.SaveAsync(id, changed, Ct)));

        var template = (await WithAsync(t => t.GetAsync(id, Ct)))!;
        var revisions = await WithAsync(t => t.RevisionsAsync(id, Ct));
        Assert.Equal(("Ankündigung Gerüst", 2), (template.Name, template.Revision));
        Assert.Equal([2, 1], revisions.Select(r => r.Number));
        Assert.Equal([true, false], revisions.Select(r => r.IsCurrent));
        var first = (await WithAsync(t => t.RevisionAsync(revisions[1].Id, Ct)))!;
        Assert.Equal("<p>{{ salutation }}</p>", first.Body);
    }

    [Theory]
    [InlineData("<p>{{ if }}</p>", "Fehler")]
    [InlineData("<p>{{ gibt_es_nicht }}</p>", "gibt_es_nicht")]
    public async Task Syntax_errors_and_unknown_placeholders_are_rejected(string body, string expected)
    {
        var input = Input("Fehlerhaft");
        input.Body = body;

        var result = await WithAsync(t => t.SaveAsync(null, input, Ct));

        Assert.False(result.Succeeded);
        Assert.Contains(expected, result.Errors.Single(), StringComparison.Ordinal);
        Assert.Empty(await WithAsync(t => t.ListAsync(Ct)));
    }

    [Fact]
    public async Task Names_are_unique_and_deleted_templates_disappear()
    {
        var id = Validate(await WithAsync(t => t.SaveAsync(null, Input("Doppelt"), Ct)));

        var duplicate = await WithAsync(t => t.SaveAsync(null, Input("Doppelt"), Ct));
        Validate(await WithAsync(t => t.DeleteAsync(id, Ct)));

        Assert.Contains("gibt es schon", duplicate.Errors.Single(), StringComparison.Ordinal);
        Assert.Empty(await WithAsync(t => t.ListAsync(Ct)));
        Validate(await WithAsync(t => t.SaveAsync(null, Input("Doppelt"), Ct)));
    }

    [Fact]
    public async Task Preview_renders_the_unsaved_text_with_sample_data()
    {
        var preview = await WithAsync(t => t.PreviewAsync(TemplateKind.Letter, "Gerüst für {{ tenant_name }}", "<p>{{ salutation }}</p>", Ct));

        Assert.NotNull(preview.Pdf);
        Assert.Equal(("Gerüst für Erika Muster", "<p>{{ salutation }}</p>"), (pdf.Last!.Title, pdf.Last.TemplateText));
        Assert.Equal("Erika Muster", pdf.Last.Recipient!.Rows[0]);
    }

    [Fact]
    public async Task Preview_of_a_broken_text_reports_errors_instead_of_a_pdf()
    {
        var preview = await WithAsync(t => t.PreviewAsync(TemplateKind.Letter, "Betreff", "<p>{{ unbekannt }}</p>", Ct));

        Assert.Null(preview.Pdf);
        Assert.Contains("unbekannt", preview.Errors.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Letters_can_include_snippets_but_unknown_snippets_are_rejected()
    {
        var unknown = Input("Mit Baustein");
        unknown.Body = "<p>{{ include 'Gibt es nicht' }}</p>";
        var rejected = await WithAsync(t => t.SaveAsync(null, unknown, Ct));
        Validate(await WithAsync(t => t.SaveAsync(null, new TemplateInput { Name = "Gruß", Kind = TemplateKind.Snippet, Body = "<p>Gruß von {{ sender_name }}</p>" }, Ct)));

        unknown.Body = "<p>{{ include 'Gruß' }}</p>";
        Validate(await WithAsync(t => t.SaveAsync(null, unknown, Ct)));

        Assert.Contains("Gibt es nicht", rejected.Errors.Single(), StringComparison.Ordinal);
        Assert.Equal(["Gruß"], await WithAsync(t => t.SnippetNamesAsync(Ct)));
    }

    [Fact]
    public async Task A_snippet_that_is_still_included_cannot_be_deleted_or_renamed()
    {
        var snippet = Validate(await WithAsync(t => t.SaveAsync(null, new TemplateInput { Name = "Gruß", Kind = TemplateKind.Snippet, Body = "<p>Gruß</p>" }, Ct)));
        var letter = Input("Brief");
        letter.Body = "<p>{{ include 'Gruß' }}</p>";
        var letterId = Validate(await WithAsync(t => t.SaveAsync(null, letter, Ct)));

        var delete = await WithAsync(t => t.DeleteAsync(snippet, Ct));
        var renamed = (await WithAsync(t => t.GetAsync(snippet, Ct)))!;
        renamed.Name = "Anderer Name";
        var rename = await WithAsync(t => t.SaveAsync(snippet, renamed, Ct));
        Validate(await WithAsync(t => t.DeleteAsync(letterId, Ct)));

        Assert.Contains("„Brief“", delete.Errors.Single(), StringComparison.Ordinal);
        Assert.Contains("umbenannt", rename.Errors.Single(), StringComparison.Ordinal);
        Validate(await WithAsync(t => t.DeleteAsync(snippet, Ct)));
    }

    [Fact]
    public async Task Bundled_templates_are_installed_once_and_are_valid()
    {
        var first = await WithAsync(t => t.InstallDefaultsAsync(Ct));
        var second = await WithAsync(t => t.InstallDefaultsAsync(Ct));

        Assert.Equal((20, 0, 0, 20), (first.Created, first.Existing, second.Created, second.Existing));
        var rows = await WithAsync(t => t.ListAsync(Ct));
        string[] expected =
        [
            "Allgemeines Schreiben", "Ankündigung von Arbeiten", "Anlage Datenschutzinformation", "Anlage Energieausweis", "Anlage Hausordnung", "Anlage Übergabeprotokoll",
            "Anpassung Betriebskostenpauschale (§ 560 BGB)", "Bankverbindung", "Grußformel", "Information Staffelmiete (§ 557a BGB)", "Mieterhöhung Indexmiete (§ 557b BGB)",
            "Mieterhöhung Vergleichsmiete (§ 558 BGB)", "Mietvertrag Wohnraum", "Zimmervertrag WG",
            "Wohnungsgeberbestätigung Einzug (§ 19 BMG)", "Wohnungsgeberbestätigung Auszug (§ 19 BMG)", "Bestätigung der Kündigung",
            "Mietbescheinigung", "Mietschuldenfreiheitsbescheinigung", "Kautionsabrechnung und -rückzahlung",
        ];
        Assert.Equal(expected.Order(StringComparer.Ordinal), rows.Select(r => r.Name).Order(StringComparer.Ordinal));
        Assert.All(rows, r => Assert.Equal(1, r.Revision));

        // Every bundled text must pass the check that saving applies (syntax, placeholders, snippets) for all samples.
        foreach (var entry in rows)
        {
            var edit = (await WithAsync(t => t.GetAsync(entry.Id, Ct)))!;
            Validate(await WithAsync(t => t.SaveAsync(entry.Id, edit, Ct)));
        }
    }

    [Fact]
    public async Task Export_and_import_roundtrip_with_a_suffix_for_existing_names()
    {
        var id = Validate(await WithAsync(t => t.SaveAsync(null, Input("Original"), Ct)));
        var export = (await WithAsync(t => t.ExportAsync(id, Ct)))!;

        var import = Validate(await WithAsync(t => t.ImportAsync(export.Content, Ct)));
        var second = Validate(await WithAsync(t => t.ImportAsync(export.Content, Ct)));
        var bad = await WithAsync(t => t.ImportAsync("{\"format\":\"x\"}"u8.ToArray(), Ct));

        Assert.Equal("vorlage-Original.json", export.FileName);
        Assert.Equal("Original (Import)", (await WithAsync(t => t.GetAsync(import, Ct)))!.Name);
        Assert.Equal("Original (Import 2)", (await WithAsync(t => t.GetAsync(second, Ct)))!.Name);
        Assert.Equal("<p>{{ salutation }}</p>", (await WithAsync(t => t.GetAsync(import, Ct)))!.Body);
        Assert.False(bad.Succeeded);
    }

    private static TemplateInput Input(string name) => new()
    {
        Name = name,
        Kind = TemplateKind.Letter,
        Subject = "Mitteilung",
        Body = "<p>{{ salutation }}</p>",
    };

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }

    private async Task<T> WithAsync<T>(Func<IDocumentTemplates, Task<T>> action)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IDocumentTemplates>());
    }

    private sealed class CapturingPdfGenerator : IPdfGenerator
    {
        public PdfRequest? Last { get; private set; }

        public Task<byte[]> GenerateAsync(PdfRequest job, CancellationToken cancellationToken = default)
        {
            Last = job;
            return Task.FromResult<byte[]>([1, 2, 3]);
        }

        public Task<StoredFile> GenerateAndStoreAsync(PdfRequest job, string fileName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
