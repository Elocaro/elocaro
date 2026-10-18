using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Objekte;
using MietHahn.Application.Personen;
using MietHahn.Domain.Gemeinsam;
using MietHahn.Domain.Personen;
using MietHahn.Domain.Vertraege;
using MietHahn.Infrastructure.Persistenz;

namespace MietHahn.Infrastructure.Vertraege;

/// <summary>Wer ein Mietobjekt an einem Tag gemietet hat (für Objektübersicht und Leerstand).</summary>
internal sealed class VertragsBelegung(MietHahnDbContext db) : IBelegungsauskunft
{
    public async Task<IReadOnlyDictionary<Guid, Belegung>> AmAsync(IReadOnlyCollection<Guid> mietobjektIds, DateOnly tag, CancellationToken cancellationToken = default)
    {
        var vertraege = await db.Set<Mietvertrag>().AsNoTracking().Include(v => v.Parteien)
            .Where(v => mietobjektIds.Contains(v.MietobjektId) && v.Laufzeit.Von <= tag && (v.Laufzeit.Bis == null || v.Laufzeit.Bis >= tag))
            .ToListAsync(cancellationToken);
        var personIds = vertraege.SelectMany(v => v.Parteien.Select(p => p.PersonId)).Distinct().ToList();
        var namen = (await db.Set<Person>().AsNoTracking().Where(p => personIds.Contains(p.Id)).ToListAsync(cancellationToken))
            .ToDictionary(p => p.Id, p => p.Anzeigename);
        return vertraege.ToDictionary(v => v.MietobjektId, v => new Belegung(v.Id, VertragsVerwaltung.MieterText(v, namen), v.Beginn, v.Ende));
    }

    public Task<bool> HatVertraegeAsync(Guid mietobjektId, CancellationToken cancellationToken = default) =>
        db.Set<Mietvertrag>().AnyAsync(v => v.MietobjektId == mietobjektId, cancellationToken);
}

/// <summary>Mieter und Bürgen aus den Verträgen: Rollenanzeige, Löschsperre und Aufbewahrungsfrist.</summary>
internal sealed class VertragsPersonenbezug(MietHahnDbContext db, TimeProvider uhr) : IPersonenbezug
{
    public async Task<IReadOnlyList<Personenbezug>> BezuegeAsync(IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken = default)
    {
        var heute = Zeitzone.Heute(uhr.GetUtcNow());
        var vertraege = await db.Set<Mietvertrag>().AsNoTracking().Include(v => v.Parteien)
            .Where(v => v.Parteien.Any(p => personIds.Contains(p.PersonId)))
            .ToListAsync(cancellationToken);
        if (vertraege.Count == 0)
        {
            return [];
        }

        var mietobjekte = await Mietobjekt.LadenAsync(db, cancellationToken);
        return vertraege.SelectMany(v => v.Parteien.Where(p => personIds.Contains(p.PersonId)).Select(p =>
        {
            var mietobjekt = mietobjekte.GetValueOrDefault(v.MietobjektId);
            return new Personenbezug(
                p.PersonId,
                p.Rolle == Parteirolle.Mieter ? "Mieter" : "Bürge",
                $"{mietobjekt?.Bezeichnung}, {mietobjekt?.Objekt} ({v.Laufzeit})",
                v.Ende is null || v.Ende >= heute,
                v.Ende,
                $"/verwaltung/vertraege/{v.Id}");
        })).ToList();
    }
}

/// <summary>Verträge der Person für die DSGVO-Auskunft.</summary>
internal sealed class VertragsPersonendaten(MietHahnDbContext db) : IPersonendatenQuelle
{
    public string Abschnitt => "mietvertraege";

    public async Task<object?> DatenAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        var vertraege = await db.Set<Mietvertrag>().AsNoTracking()
            .Include(v => v.Parteien).Include(v => v.Bestandteile).Include(v => v.Personen).AsSplitQuery()
            .Where(v => v.Parteien.Any(p => p.PersonId == personId))
            .ToListAsync(cancellationToken);
        if (vertraege.Count == 0)
        {
            return null;
        }

        var mietobjekte = await Mietobjekt.LadenAsync(db, cancellationToken);
        return vertraege.Select(v => new
        {
            v.Id,
            Mietobjekt = mietobjekte.GetValueOrDefault(v.MietobjektId)?.Bezeichnung,
            Objekt = mietobjekte.GetValueOrDefault(v.MietobjektId)?.Objekt,
            Rolle = v.Parteien.Single(p => p.PersonId == personId).Rolle.ToString(),
            v.Laufzeit,
            v.BefristetBis,
            v.KuendigungEingegangenAm,
            KuendigungDurch = v.KuendigungDurch?.ToString(),
            Abrechnungsart = v.Abrechnungsart.ToString(),
            v.FaelligAmWerktag,
            Zahlungsart = v.Zahlungsart.ToString(),
            v.Kaution,
            v.KautionNotiz,
            v.Notizen,
            Bestandteile = v.Bestandteile.OrderBy(b => b.Gueltigkeit.Von).Select(b => new { Art = b.Art.ToString(), b.Bezeichnung, b.Gueltigkeit, b.Betrag }),
            Personenanzahl = v.Personen.OrderBy(p => p.Gueltigkeit.Von).Select(p => new { p.Gueltigkeit, p.Anzahl }),
        }).ToList();
    }
}
