using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Audit;
using MietHahn.Application.Gemeinsam;
using MietHahn.Application.Konten;
using MietHahn.Application.Objekte;
using MietHahn.Domain.Objekte;
using MietHahn.Infrastructure.Audit;
using MietHahn.Infrastructure.Identitaet;
using MietHahn.Infrastructure.Persistenz;

namespace MietHahn.Infrastructure.Objekte;

internal sealed class ObjektZuordnungen(MietHahnDbContext db, UserManager<Benutzer> benutzerManager, AuditSchreiber audit) : IObjektZuordnungen
{
    public async Task<IReadOnlyList<Guid>> ObjekteVonAsync(Guid benutzerId, CancellationToken cancellationToken = default) =>
        await db.Set<ObjektZuordnung>().Where(z => z.BenutzerId == benutzerId).Select(z => z.ObjektId).ToListAsync(cancellationToken);

    public Task<bool> IstZugeordnetAsync(Guid benutzerId, Guid objektId, CancellationToken cancellationToken = default) =>
        db.Set<ObjektZuordnung>().AnyAsync(z => z.BenutzerId == benutzerId && z.ObjektId == objektId, cancellationToken);

    public async Task<Ergebnis> SetzenAsync(Guid benutzerId, IReadOnlyCollection<Guid> objektIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(objektIds);
        var benutzer = await benutzerManager.FindByIdAsync(benutzerId.ToString());
        if (benutzer is null)
        {
            return Ergebnis.Fehlgeschlagen("Das Konto wurde nicht gefunden.");
        }

        if (!await benutzerManager.IsInRoleAsync(benutzer, Rollen.Hausmeister))
        {
            return Ergebnis.Fehlgeschlagen("Objekte lassen sich nur Hausmeistern zuordnen.");
        }

        var gewuenscht = objektIds.Distinct().ToHashSet();
        var bekannt = await db.Set<Objekt>().Where(o => gewuenscht.Contains(o.Id)).Select(o => new { o.Id, o.Bezeichnung }).ToListAsync(cancellationToken);
        if (bekannt.Count != gewuenscht.Count)
        {
            return Ergebnis.Fehlgeschlagen("Mindestens ein Objekt wurde nicht gefunden.");
        }

        var bisher = await db.Set<ObjektZuordnung>().Where(z => z.BenutzerId == benutzerId).ToListAsync(cancellationToken);
        var vorher = await Namen(bisher.Select(z => z.ObjektId), cancellationToken);
        db.RemoveRange(bisher.Where(z => !gewuenscht.Contains(z.ObjektId)));
        db.AddRange(gewuenscht.Where(id => bisher.All(z => z.ObjektId != id)).Select(id => new ObjektZuordnung(id, benutzerId)));
        await db.SaveChangesAsync(cancellationToken);

        var nachher = string.Join(", ", bekannt.Select(b => b.Bezeichnung).Order(StringComparer.CurrentCulture));
        if (vorher != nachher)
        {
            await audit.ProtokollierenAsync(nameof(Benutzer), benutzerId, AuditAktion.Geaendert, [new Feldaenderung("Objekte", vorher, nachher)], cancellationToken);
        }

        return Ergebnis.Ok(benutzerId);
    }

    private async Task<string> Namen(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var liste = ids.ToList();
        var namen = await db.Set<Objekt>().Where(o => liste.Contains(o.Id)).Select(o => o.Bezeichnung).ToListAsync(cancellationToken);
        return string.Join(", ", namen.Order(StringComparer.CurrentCulture));
    }
}
