using System.Net;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Common;
using MietHahn.Application.Documents;
using MietHahn.Domain.Common;
using MietHahn.Domain.Documents;
using MietHahn.Infrastructure.Common;
using MietHahn.Infrastructure.Persistence;
using MietHahn.Infrastructure.Templates;

namespace MietHahn.Infrastructure.Documents;

internal sealed class DocumentTemplates(MietHahnDbContext db, IPdfGenerator pdf) : IDocumentTemplates
{
    public async Task<IReadOnlyList<TemplateRow>> ListAsync(CancellationToken cancellationToken = default) =>
        (await db.Set<DocumentTemplate>().AsNoTracking().OrderBy(t => t.Name).ToListAsync(cancellationToken))
            .Select(t => new TemplateRow(t.Id, t.Name, t.Kind, t.Description, t.Revision, t.ModifiedAt ?? t.CreatedAt)).ToList();

    public async Task<TemplateInput?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var template = await db.Set<DocumentTemplate>().AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        return template is null ? null : new TemplateInput
        {
            Name = template.Name,
            Kind = template.Kind,
            Description = template.Description,
            Subject = template.Subject,
            Body = template.Body,
            Revision = template.Revision,
        };
    }

    public Task<Result> SaveAsync(Guid? id, TemplateInput data, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(data);
            Check(data.Kind, data.Subject, data.Body);

            DocumentTemplate template;
            bool changed;
            if (id is { } existing)
            {
                template = await db.Set<DocumentTemplate>().SingleOrDefaultAsync(t => t.Id == existing, cancellationToken) ?? throw new NotFoundException();
                changed = template.Edit(data.Name, data.Description, data.Subject, data.Body);
            }
            else
            {
                template = new DocumentTemplate(data.Name, data.Kind, data.Description, data.Subject, data.Body);
                db.Add(template);
                changed = true;
            }

            if (changed)
            {
                db.Add(new DocumentTemplateRevision(template));
            }

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (DatabaseErrors.IsUniqueViolation(ex))
            {
                throw new RuleViolationException("Eine Vorlage mit diesem Namen gibt es schon.");
            }

            return template.Id;
        });

    public Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var template = await db.Set<DocumentTemplate>().SingleOrDefaultAsync(t => t.Id == id, cancellationToken) ?? throw new NotFoundException();
            db.Remove(template);
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    public async Task<IReadOnlyList<TemplateRevisionRow>> RevisionsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var current = await db.Set<DocumentTemplate>().AsNoTracking().Where(t => t.Id == id).Select(t => (int?)t.Revision).SingleOrDefaultAsync(cancellationToken);
        return (await db.Set<DocumentTemplateRevision>().AsNoTracking().Where(r => r.TemplateId == id).OrderByDescending(r => r.Number).ToListAsync(cancellationToken))
            .Select(r => new TemplateRevisionRow(r.Id, r.Number, r.CreatedAt, r.CreatedBy, r.Number == current)).ToList();
    }

    public async Task<TemplateRevisionText?> RevisionAsync(Guid revisionId, CancellationToken cancellationToken = default)
    {
        var revision = await db.Set<DocumentTemplateRevision>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == revisionId, cancellationToken);
        return revision is null ? null : new TemplateRevisionText(revision.Id, revision.TemplateId, revision.Number, revision.Subject, revision.Body);
    }

    public async Task<TemplatePreview> PreviewAsync(TemplateKind kind, string subject, string body, CancellationToken cancellationToken = default)
    {
        try
        {
            var title = Check(kind, subject, body);
            var sample = new PostalAddress(["Erika Muster", "Lindenweg 3", "50667 Köln"]);
            var bytes = await pdf.GenerateAsync(new PdfRequest(null, title, TemplateCatalog.Sample(kind), sample, body, new DateOnly(2026, 10, 20)), cancellationToken);
            return new TemplatePreview(bytes, []);
        }
        catch (RuleViolationException ex)
        {
            return new TemplatePreview(null, [ex.Message]);
        }
        catch (InvalidOperationException ex)
        {
            // e.g. no landlord profile yet
            return new TemplatePreview(null, [ex.Message]);
        }
        catch (HttpRequestException)
        {
            return new TemplatePreview(null, ["Der PDF-Dienst ist nicht erreichbar. Die Vorschau steht erst wieder zur Verfügung, wenn er läuft."]);
        }
    }

    /// <summary>Checks syntax and placeholders by rendering with the sample data; returns the rendered subject.</summary>
    internal static string Check(TemplateKind kind, string subject, string body)
    {
        var sample = TemplateCatalog.Sample(kind);
        try
        {
            var errors = ScribanRenderer.Validate(body).Concat(ScribanRenderer.Validate(subject)).ToList();
            if (errors.Count > 0)
            {
                throw new RuleViolationException("Die Vorlage enthält Fehler: " + string.Join("; ", errors));
            }

            ScribanRenderer.RenderText(body, sample);
            return WebUtility.HtmlDecode(ScribanRenderer.RenderText(subject, sample).Html).Trim();
        }
        catch (TemplateErrorException ex)
        {
            throw new RuleViolationException(ex.Message);
        }
    }
}
