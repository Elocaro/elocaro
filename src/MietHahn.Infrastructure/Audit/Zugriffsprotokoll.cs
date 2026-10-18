using Microsoft.Extensions.Options;
using MietHahn.Application.Audit;
using MietHahn.Application.Gemeinsam;
using MietHahn.Infrastructure.Persistenz;

namespace MietHahn.Infrastructure.Audit;

internal sealed class Zugriffsprotokoll(
    MietHahnDbContext db,
    IAktuellerBenutzer benutzer,
    TimeProvider uhr,
    IOptions<AuditOptionen> optionen) : IZugriffsprotokoll
{
    public async Task ProtokollierenAsync(string entitaetTyp, Guid entitaetId, string zweck, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entitaetTyp);
        ArgumentException.ThrowIfNullOrWhiteSpace(zweck);
        if (!optionen.Value.ZugriffeProtokollieren)
        {
            return;
        }

        db.Add(new AuditEintrag(uhr.GetUtcNow(), benutzer.Kennung, entitaetTyp, entitaetId, AuditAktion.Gelesen, "[]", zweck));
        await db.SaveChangesAsync(cancellationToken);
    }
}
