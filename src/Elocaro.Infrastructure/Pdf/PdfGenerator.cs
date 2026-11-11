using Elocaro.Application.Documents;
using Elocaro.Application.Files;
using Elocaro.Domain.Admin;
using Elocaro.Domain.Common;
using Elocaro.Domain.Files;
using Elocaro.Infrastructure.Documents;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Infrastructure.Properties;
using Elocaro.Infrastructure.Templates;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Pdf;

internal sealed class PdfGenerator(
    ElocaroDbContext db,
    ScribanRenderer renderer,
    GotenbergConverter gotenberg,
    IFileStore fileStore,
    TimeProvider clock) : IPdfGenerator
{
    public async Task<byte[]> GenerateAsync(PdfRequest job, CancellationToken cancellationToken = default)
    {
        var (html, footer, sender) = await GenerateHtmlAsync(job, cancellationToken);
        return await gotenberg.HtmlToPdfAsync(html, footer, new PdfMetadata(job.Title, sender.Name), cancellationToken);
    }

    public async Task<string> RenderHtmlAsync(PdfRequest job, CancellationToken cancellationToken = default) =>
        (await GenerateHtmlAsync(job, cancellationToken)).Html;

    public async Task<StoredFile> GenerateAndStoreAsync(PdfRequest job, string fileName, CancellationToken cancellationToken = default)
    {
        var pdf = await GenerateAsync(job, cancellationToken);
        using var content = new MemoryStream(pdf);
        return await fileStore.SaveAsync(content, fileName, cancellationToken);
    }

    public Task<byte[]> MergeAsync(IReadOnlyList<byte[]> documents, CancellationToken cancellationToken = default) => gotenberg.MergeAsync(documents, cancellationToken);

    /// <summary>Generates the HTML of the document and footer (without Gotenberg; also for preview and tests).</summary>
    internal async Task<(string Html, string Footer, LandlordProfile Sender)> GenerateHtmlAsync(PdfRequest job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentException.ThrowIfNullOrWhiteSpace(job.Title);
        if (job.Recipient is { Rows.Count: 0 or > 6 })
        {
            throw new ArgumentException("Eine Briefanschrift hat 1 bis 6 Zeilen.", nameof(job));
        }

        var sender = await SenderProfiles.ForPropertyAsync(db, job.PropertyId, cancellationToken)
            ?? throw new InvalidOperationException("Es ist noch kein Vermieterprofil hinterlegt (Einstellungen → Vermieter). Es wird für Briefkopf und Fußzeile benötigt.");

        var content = job switch
        {
            { TemplateText: { } text } => ScribanRenderer.RenderText(text, job.Model, name: job.Title, snippets: await TemplateSnippets.LoadAsync(db, cancellationToken)),
            { Template: { } name } => renderer.RenderEmbedded($"Pdf/Templates/{name}.sbn", job.Model),
            _ => throw new ArgumentException("Vorlage oder Vorlagentext ist erforderlich.", nameof(job)),
        };

        var frame = new Dictionary<string, object?>
        {
            ["title"] = job.Title,
            ["sender"] = sender,
            ["recipient"] = job.Recipient,
            ["letter_date"] = job.Date ?? GermanTime.Today(clock.GetUtcNow()),
            ["content"] = new RawHtml(PdfHtmlGuard.Sanitize(content.Html)),
        };

        var html = renderer.RenderEmbedded("Pdf/Templates/_layout.sbn", null, frame).Html;
        var footer = renderer.RenderEmbedded("Pdf/Templates/_footer.sbn", null, frame).Html;
        return (html, footer, sender);
    }
}
