using Elocaro.Domain.Documents;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Documents;

internal static class TemplateSnippets
{
    /// <summary>All snippets by name, for <c>include</c> in templates.</summary>
    public static async Task<IReadOnlyDictionary<string, string>> LoadAsync(ElocaroDbContext db, CancellationToken cancellationToken) =>
        await db.Set<DocumentTemplate>().AsNoTracking().Where(t => t.Kind == TemplateKind.Snippet)
            .ToDictionaryAsync(t => t.Name, t => t.Body, cancellationToken);
}
