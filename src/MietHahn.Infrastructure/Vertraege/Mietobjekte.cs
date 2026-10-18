using Microsoft.EntityFrameworkCore;
using MietHahn.Domain.Objekte;
using MietHahn.Infrastructure.Persistenz;

namespace MietHahn.Infrastructure.Vertraege;

/// <summary>Bezeichnungen der vermietbaren Objekte (Einheit ohne Zimmer oder WG-Zimmer) für Anzeige und Prüfung.</summary>
internal sealed record Mietobjekt(Guid Id, Guid EinheitId, Guid? ZimmerId, Guid ObjektId, string Bezeichnung, string Objekt, bool IstVermietbar)
{
    public static async Task<Dictionary<Guid, Mietobjekt>> LadenAsync(MietHahnDbContext db, CancellationToken cancellationToken)
    {
        var objekte = await db.Set<Objekt>().AsNoTracking()
            .Include(o => o.Einheiten).ThenInclude(e => e.Zimmer)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var ergebnis = new Dictionary<Guid, Mietobjekt>();
        foreach (var objekt in objekte.OrderBy(o => o.Bezeichnung, StringComparer.CurrentCulture))
        {
            foreach (var einheit in objekt.Einheiten.OrderBy(e => e.Reihenfolge))
            {
                ergebnis[einheit.Id] = new(einheit.Id, einheit.Id, null, objekt.Id, einheit.Bezeichnung, objekt.Bezeichnung, einheit.IstSelbstVermietbar);
                foreach (var zimmer in einheit.Zimmer.OrderBy(z => z.Reihenfolge))
                {
                    ergebnis[zimmer.Id] = new(zimmer.Id, einheit.Id, zimmer.Id, objekt.Id, $"{einheit.Bezeichnung}, {zimmer.Bezeichnung}", objekt.Bezeichnung, true);
                }
            }
        }

        return ergebnis;
    }
}
