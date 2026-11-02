using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Elocaro.Application.Common;
using Elocaro.Application.Documents;
using Elocaro.Domain.Common;
using Elocaro.Domain.Documents;
using Elocaro.Infrastructure.Common;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Infrastructure.Templates;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Documents;

internal sealed partial class DocumentTemplates(ElocaroDbContext db, IPdfGenerator pdf) : IDocumentTemplates
{
    private const int MaxImportSize = 300 * 1024;
    private const string ExportFormat = "elocaro-template";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

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
            var snippets = await TemplateSnippets.LoadAsync(db, cancellationToken);
            DocumentTemplate template;
            bool changed;
            if (id is { } existing)
            {
                template = await db.Set<DocumentTemplate>().SingleOrDefaultAsync(t => t.Id == existing, cancellationToken) ?? throw new NotFoundException();
                data.Kind = template.Kind;
                if (template.Kind == TemplateKind.Snippet && data.Name.Trim() != template.Name)
                {
                    await EnsureNotIncludedAsync(template, "umbenannt", cancellationToken);
                }

                Check(data.Kind, data.Subject, data.Body, Without(snippets, template));
                changed = template.Edit(data.Name, data.Description, data.Subject, data.Body);
            }
            else
            {
                Check(data.Kind, data.Subject, data.Body, snippets);
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
            if (template.Kind == TemplateKind.Snippet)
            {
                await EnsureNotIncludedAsync(template, "gelöscht", cancellationToken);
            }

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
            var title = Check(kind, subject, body, await TemplateSnippets.LoadAsync(db, cancellationToken));
            title = string.IsNullOrWhiteSpace(title) ? "Vorschau" : title; // a new template has no subject yet
            var sample = new PostalAddress(["Erika Muster", "Lindenweg 3", "50667 Köln"]);
            var bytes = await pdf.GenerateAsync(new PdfRequest(null, title, TemplateCatalog.Sample(kind), kind == TemplateKind.Contract ? null : sample, body, new DateOnly(2026, 10, 20)), cancellationToken);
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

    public async Task<TemplateHtmlPreview> PreviewHtmlAsync(TemplateKind kind, string subject, string body, CancellationToken cancellationToken = default)
    {
        try
        {
            var title = Check(kind, subject, body, await TemplateSnippets.LoadAsync(db, cancellationToken));
            title = string.IsNullOrWhiteSpace(title) ? "Vorschau" : title; // a new template has no subject yet
            var sample = new PostalAddress(["Erika Muster", "Lindenweg 3", "50667 Köln"]);
            var html = await pdf.RenderHtmlAsync(new PdfRequest(null, title, TemplateCatalog.Sample(kind), kind == TemplateKind.Contract ? null : sample, body, new DateOnly(2026, 10, 20)), cancellationToken);
            return new TemplateHtmlPreview(html, []);
        }
        catch (RuleViolationException ex)
        {
            return new TemplateHtmlPreview(null, [ex.Message]);
        }
        catch (InvalidOperationException ex)
        {
            // e.g. no landlord profile yet
            return new TemplateHtmlPreview(null, [ex.Message]);
        }
    }

    public async Task<IReadOnlyList<string>> SnippetNamesAsync(CancellationToken cancellationToken = default) =>
        await db.Set<DocumentTemplate>().AsNoTracking().Where(t => t.Kind == TemplateKind.Snippet).OrderBy(t => t.Name).Select(t => t.Name).ToListAsync(cancellationToken);

    public Task<DefaultsInstalled> InstallDefaultsAsync(CancellationToken cancellationToken = default) => InstallAsync(restoreDeleted: true, cancellationToken);

    public Task<DefaultsInstalled> EnsureDefaultsAsync(CancellationToken cancellationToken = default) => InstallAsync(restoreDeleted: false, cancellationToken);

    private async Task<DefaultsInstalled> InstallAsync(bool restoreDeleted, CancellationToken cancellationToken)
    {
        var known = restoreDeleted ? db.Set<DocumentTemplate>() : db.Set<DocumentTemplate>().IgnoreQueryFilters();
        var existing = (await known.Select(t => t.Name).ToListAsync(cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var created = 0;
        // Snippets first: the bundled letters include them.
        foreach (var entry in TemplateDefaults.All.OrderBy(e => e.Kind == TemplateKind.Snippet ? 0 : 1))
        {
            if (!existing.Add(entry.Name))
            {
                continue;
            }

            var template = new DocumentTemplate(entry.Name, entry.Kind, entry.Description, entry.Subject, entry.Body);
            db.Add(template);
            db.Add(new DocumentTemplateRevision(template));
            created++;
        }

        if (created > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return new DefaultsInstalled(created, TemplateDefaults.All.Count - created);
    }

    public async Task<TemplateExport?> ExportAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var template = await db.Set<DocumentTemplate>().AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (template is null)
        {
            return null;
        }

        var file = new ExportFile(ExportFormat, 1, template.Name, template.Kind.ToString(), template.Description, template.Subject, template.Body);
        var name = string.Concat(template.Name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-')).Trim('-');
        return new TemplateExport($"vorlage-{(name.Length == 0 ? "export" : name)}.json", JsonSerializer.SerializeToUtf8Bytes(file, Json));
    }

    public Task<Result> ImportAsync(byte[] content, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(content);
            if (content.Length is 0 or > MaxImportSize)
            {
                throw new RuleViolationException("Die Datei ist leer oder größer als 300 KB.");
            }

            ExportFile? file;
            try
            {
                file = JsonSerializer.Deserialize<ExportFile>(content, Json);
            }
            catch (JsonException)
            {
                file = null;
            }

            if (file is not { Format: ExportFormat, Version: 1 } || !Enum.TryParse<TemplateKind>(file.Kind, out var kind) || !Enum.IsDefined(kind))
            {
                throw new RuleViolationException("Das ist keine Vorlagendatei von Elocaro (Export unter „Vorlagen“).");
            }

            var snippets = await TemplateSnippets.LoadAsync(db, cancellationToken);
            Check(kind, file.Subject ?? "", file.Body ?? "", snippets);

            var taken = (await db.Set<DocumentTemplate>().Select(t => t.Name).ToListAsync(cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var name = (file.Name ?? "").Trim();
            if (name.Length == 0 || name.Length > 100)
            {
                throw new RuleViolationException("Der Name der Vorlage fehlt oder ist zu lang.");
            }

            var candidate = name;
            for (var n = 1; !taken.Add(candidate); n++)
            {
                candidate = $"{name} (Import{(n > 1 ? " " + n : "")})";
            }

            var template = new DocumentTemplate(candidate, kind, file.Description, file.Subject ?? "", file.Body ?? "");
            db.Add(template);
            db.Add(new DocumentTemplateRevision(template));
            await db.SaveChangesAsync(cancellationToken);
            return template.Id;
        });

    /// <summary>Checks syntax and placeholders by rendering with the sample data; returns the rendered subject.</summary>
    internal static string Check(TemplateKind kind, string? subject, string body, IReadOnlyDictionary<string, string> snippets)
    {
        subject ??= "";
        var errors = ScribanRenderer.Validate(body).Concat(ScribanRenderer.Validate(subject)).ToList();
        if (errors.Count > 0)
        {
            throw new RuleViolationException("Die Vorlage enthält Fehler: " + string.Join("; ", errors));
        }

        // All samples of the kind must work; a snippet only has to work for one kind of document it is used in.
        var failures = new List<string>();
        var passed = 0;
        string? title = null;
        foreach (var sample in TemplateCatalog.Samples(kind))
        {
            try
            {
                ScribanRenderer.RenderText(body, sample, snippets: snippets);
                title ??= WebUtility.HtmlDecode(ScribanRenderer.RenderText(subject, sample, snippets: snippets).Html).Trim();
                passed++;
            }
            catch (TemplateErrorException ex)
            {
                failures.Add(ex.Message);
            }
        }

        if (failures.Count > 0 && (kind != TemplateKind.Snippet || passed == 0))
        {
            throw new RuleViolationException(failures[0]);
        }

        return title ?? "";
    }

    private static Dictionary<string, string> Without(IReadOnlyDictionary<string, string> snippets, DocumentTemplate template)
    {
        // A snippet must not include itself.
        var copy = snippets.ToDictionary(s => s.Key, s => s.Value);
        copy.Remove(template.Name);
        return copy;
    }

    private async Task EnsureNotIncludedAsync(DocumentTemplate snippet, string action, CancellationToken cancellationToken)
    {
        var users = (await db.Set<DocumentTemplate>().AsNoTracking().Where(t => t.Id != snippet.Id).Select(t => new { t.Name, t.Body }).ToListAsync(cancellationToken))
            .Where(t => IncludeCall().Matches(t.Body).Any(m => m.Groups[1].Value == snippet.Name))
            .Select(t => $"„{t.Name}“")
            .ToList();
        if (users.Count > 0)
        {
            throw new RuleViolationException($"Der Baustein „{snippet.Name}“ wird noch in {string.Join(", ", users)} verwendet und kann nicht {action} werden.");
        }
    }

    [GeneratedRegex("""include\s+['"]([^'"]+)['"]""")]
    private static partial Regex IncludeCall();

    private sealed record ExportFile(string Format, int Version, string? Name, string? Kind, string? Description, string? Subject, string? Body);
}
