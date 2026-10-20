using Microsoft.EntityFrameworkCore;
using MietHahn.Domain.Documents;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Documents;

internal static class TemplateSnippets
{
    /// <summary>All snippets by name, for <c>include</c> in templates.</summary>
    public static async Task<IReadOnlyDictionary<string, string>> LoadAsync(MietHahnDbContext db, CancellationToken cancellationToken) =>
        await db.Set<DocumentTemplate>().AsNoTracking().Where(t => t.Kind == TemplateKind.Snippet)
            .ToDictionaryAsync(t => t.Name, t => t.Body, cancellationToken);
}
