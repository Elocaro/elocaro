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
