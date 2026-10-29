using Elocaro.Application.Audit;
using Elocaro.Application.Files;
using Elocaro.Application.Receipts;
using Elocaro.Domain.Common;
using Elocaro.Domain.People;
using Elocaro.Domain.Receipts;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Receipts.Extraction;

/// <summary>
/// "Read suggestion": loads the stored pages, asks the configured provider, checks the plausibility and applies learned
/// supplier rules. Saves nothing; the result only pre-fills the form.
/// </summary>
internal sealed class ReceiptExtraction(IFileStore files, IReceiptExtractor extractor, ElocaroDbContext db, IAccessLog accessLog, TimeProvider clock) : IReceiptExtraction
{
    public ExtractorInfo Info => extractor.Info;

    public async Task<ExtractionOutcome> SuggestAsync(IReadOnlyList<Guid> fileIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileIds);
        var info = extractor.Info;
        if (!info.Ready)
        {
            return ExtractionOutcome.Failed(info.Problem ?? "Die Belegauslesung ist nicht aktiviert.");
        }

        if (fileIds.Count == 0)
        {
            return ExtractionOutcome.Failed("Bitte zuerst mindestens eine Datei auswählen.");
        }

        try
        {
            var documents = new List<ExtractionDocument>();
            foreach (var id in fileIds.Distinct())
            {
                await using var opened = await files.OpenAsync(id, cancellationToken) ?? throw new ExtractionException("Eine Datei wurde nicht gefunden.");
                using var memory = new MemoryStream();
                await opened.Content.CopyToAsync(memory, cancellationToken);
                documents.Add(new ExtractionDocument(opened.StoredFile.Name, opened.StoredFile.MimeType, memory.ToArray()));
            }

            // The transfer is evidence for data protection: who sent which document to which provider.
            await accessLog.LogAsync("StoredFile", fileIds[0], $"Beleg zur Auslesung an {info.Name} übergeben", cancellationToken);

            var today = GermanTime.Today(clock.GetUtcNow());
            var result = await extractor.ExtractAsync(documents, new ExtractionContext(today), cancellationToken);
            var suggestion = SuggestionChecks.Check(result.Suggestion, today);
            return await EnrichAsync(suggestion, result.Usage, cancellationToken);
        }
        catch (ExtractionException ex)
        {
            return ExtractionOutcome.Failed(ex.Message);
        }
    }

    /// <summary>Matches the supplier to a contact and replaces the cost type with the one learned from earlier receipts.</summary>
    private async Task<ExtractionOutcome> EnrichAsync(ReceiptSuggestion suggestion, ExtractionUsage usage, CancellationToken cancellationToken)
    {
        Guid? supplierId = null;
        if (suggestion.SupplierName.HasValue)
        {
            var wanted = SupplierCostTypeRule.Normalize(suggestion.SupplierName.Value);
            var contacts = await db.Set<Person>().AsNoTracking()
                .Where(p => p.ContactType != ContactType.None && p.AnonymizedAt == null).ToListAsync(cancellationToken);
            var matches = contacts.Where(p => Names(p).Any(n => SupplierCostTypeRule.Normalize(n) == wanted)).ToList();
            if (matches.Count == 1)
            {
                supplierId = matches[0].Id;
            }
        }

        var keys = new[] { SupplierCostTypeRule.KeyFor(supplierId, null), SupplierCostTypeRule.NameKey(suggestion.SupplierName.Value) }
            .Where(k => k is not null).Cast<string>().ToList();
        if (keys.Count > 0)
        {
            var rules = await db.Set<SupplierCostTypeRule>().AsNoTracking().Where(r => keys.Contains(r.SupplierKey)).ToListAsync(cancellationToken);
            var rule = keys.Select(k => rules.FirstOrDefault(r => r.SupplierKey == k)).FirstOrDefault(r => r is not null);
            if (rule is not null && CostTypes.IsKnown(rule.CostType))
            {
                suggestion = suggestion with
                {
                    CostType = new Suggested<string>(rule.CostType, 0.95),
                    CostTypeReason = rule.Uses > 1 ? $"aus {rule.Uses} früheren Belegen dieses Lieferanten gelernt" : "aus einem früheren Beleg dieses Lieferanten gelernt",
                };
            }
        }

        return new ExtractionOutcome(suggestion, usage, null, supplierId);
    }

    /// <summary>A document usually names the company, sometimes the person.</summary>
    private static IEnumerable<string> Names(Person person) =>
        new[] { person.Company, string.Join(' ', new[] { person.FirstName, person.LastName }.Where(t => !string.IsNullOrWhiteSpace(t))), person.DisplayName }
            .Where(n => !string.IsNullOrWhiteSpace(n))!;
}
