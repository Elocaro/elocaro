using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Documents;
using MietHahn.Application.Files;
using MietHahn.Domain.Admin;
using MietHahn.Domain.Common;
using MietHahn.Domain.Files;
using MietHahn.Infrastructure.Documents;
using MietHahn.Infrastructure.Persistence;
using MietHahn.Infrastructure.Templates;

namespace MietHahn.Infrastructure.Pdf;

internal sealed class PdfGenerator(
    MietHahnDbContext db,
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

        var sender = await Admin.LandlordProfiles.ForPropertyAsync(db, job.PropertyId, cancellationToken)
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
            ["content"] = new RawHtml(content.Html),
        };

        var html = renderer.RenderEmbedded("Pdf/Templates/_layout.sbn", null, frame).Html;
        var footer = renderer.RenderEmbedded("Pdf/Templates/_footer.sbn", null, frame).Html;
        return (html, footer, sender);
    }
}
