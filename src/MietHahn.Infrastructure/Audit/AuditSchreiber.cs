using System.Text.Json;
using MietHahn.Application.Audit;
using MietHahn.Application.Gemeinsam;
using MietHahn.Infrastructure.Persistenz;

namespace MietHahn.Infrastructure.Audit;

/// <summary>Schreibt Audit-Einträge für Daten, die nicht über den Change-Tracker erfasst werden (z. B. Konten).</summary>
internal sealed class AuditSchreiber(MietHahnDbContext db, IAktuellerBenutzer benutzer, TimeProvider uhr)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task ProtokollierenAsync(string entitaetTyp, Guid entitaetId, AuditAktion aktion, IReadOnlyList<Feldaenderung> aenderungen, CancellationToken cancellationToken = default)
    {
        db.Add(new AuditEintrag(uhr.GetUtcNow(), benutzer.Kennung, entitaetTyp, entitaetId, aktion, JsonSerializer.Serialize(aenderungen, Json)));
        await db.SaveChangesAsync(cancellationToken);
    }
}
