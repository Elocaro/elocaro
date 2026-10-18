using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MietHahn.Application.Aufgaben;
using MietHahn.Application.Kommunikation;
using MietHahn.Application.Konten;
using MietHahn.Domain.Gemeinsam;
using MietHahn.Domain.Personen;
using MietHahn.Domain.Vertraege;
using MietHahn.Infrastructure.Identitaet;
using MietHahn.Infrastructure.Persistenz;
using Quartz;

namespace MietHahn.Infrastructure.Vertraege;

/// <summary>Übernimmt fällige Staffeln als Kaltmiete und informiert die Admins per Mail.</summary>
internal sealed class StaffelAnwendung(MietHahnDbContext db, IMailVersand mail, IOptions<AnwendungOptionen> anwendung, TimeProvider uhr)
{
    /// <returns>Anzahl der übernommenen Staffeln.</returns>
    public async Task<int> AnwendenAsync(CancellationToken cancellationToken = default)
    {
        var heute = Zeitzone.Heute(uhr.GetUtcNow());
        var vertraege = await db.Set<Mietvertrag>()
            .Include(v => v.Staffeln).Include(v => v.Bestandteile).Include(v => v.Personen).Include(v => v.Parteien).AsSplitQuery()
            .Where(v => v.Staffeln.Any(s => s.AngewendetAm == null && s.Ab <= heute))
            .ToListAsync(cancellationToken);
        if (vertraege.Count == 0)
        {
            return 0;
        }

        var mietobjekte = await Mietobjekt.LadenAsync(db, cancellationToken);
        var zeilen = new List<string>();
        foreach (var vertrag in vertraege)
        {
            foreach (var staffel in vertrag.FaelligeStaffelnAnwenden(heute, uhr.GetUtcNow()))
            {
                var objekt = mietobjekte.GetValueOrDefault(vertrag.MietobjektId);
                zeilen.Add(string.Create(CultureInfo.GetCultureInfo("de-DE"), $"{objekt?.Bezeichnung}, {objekt?.Objekt}: ab {staffel.Ab:dd.MM.yyyy} {staffel.Kaltmiete:#,##0.00} € Kaltmiete"));
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        var empfaenger = await AdminEmpfaenger.LadenAsync(db, cancellationToken);
        if (zeilen.Count > 0 && empfaenger.Count > 0)
        {
            await mail.EinreihenAsync(new MailAuftrag(empfaenger, "staffel-angewendet", new { Staffeln = zeilen, Link = anwendung.Value.Link("/verwaltung/vertraege").ToString() }, Bezug: "staffelmiete"), cancellationToken);
        }

        return zeilen.Count;
    }
}

[DisallowConcurrentExecution]
internal sealed class StaffelJob(StaffelAnwendung anwendung) : IJob
{
    public static readonly JobKey Schluessel = new("staffelmiete", "vertraege");

    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        await anwendung.AnwendenAsync(context.CancellationToken);
    }
}

/// <summary>Dashboard: anstehende Staffeln (60 Tage) und mögliche Indexanpassungen.</summary>
internal sealed class MietmodellAufgaben(MietHahnDbContext db, TimeProvider uhr) : IAufgabenQuelle
{
    public async Task<IReadOnlyList<Aufgabe>> AufgabenAsync(ClaimsPrincipal benutzer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(benutzer);
        if (!benutzer.IsInRole(Rollen.Admin) && !benutzer.HasClaim(Rechte.ClaimTyp, nameof(Recht.Stammdaten)))
        {
            return [];
        }

        var heute = Zeitzone.Heute(uhr.GetUtcNow());
        var bis = heute.AddDays(60);
        var vertraege = await db.Set<Mietvertrag>().AsNoTracking()
            .Include(v => v.Staffeln).Include(v => v.Bestandteile).AsSplitQuery()
            .Where(v => v.Mietmodell != Mietmodell.Standard && (v.Laufzeit.Bis == null || v.Laufzeit.Bis >= heute))
            .ToListAsync(cancellationToken);
        if (vertraege.Count == 0)
        {
            return [];
        }

        var mietobjekte = await Mietobjekt.LadenAsync(db, cancellationToken);
        var neuesterVpi = await db.Set<VpiWert>().AsNoTracking().OrderByDescending(v => v.Monat).FirstOrDefaultAsync(cancellationToken);
        var aufgaben = new List<Aufgabe>();
        foreach (var vertrag in vertraege)
        {
            var name = mietobjekte.GetValueOrDefault(vertrag.MietobjektId)?.Bezeichnung ?? "Mietobjekt";
            var link = $"/verwaltung/vertraege/{vertrag.Id}";
            foreach (var staffel in vertrag.Staffeln.Where(s => s.AngewendetAm is null && s.Ab <= bis))
            {
                aufgaben.Add(new Aufgabe("Mieten", $"Staffel {name}: {staffel.Kaltmiete.ToString("#,##0.00", CultureInfo.GetCultureInfo("de-DE"))} € ab {staffel.Ab:dd.MM.yyyy}", link, Dringlichkeit.Info,
                    "Wird am Stichtag automatisch als Kaltmiete übernommen.", staffel.Ab));
            }

            if (vertrag.Mietmodell == Mietmodell.Index && vertrag.IndexBasisWert is { } basis && neuesterVpi is not null
                && neuesterVpi.Monat > vertrag.IndexBasisMonat && neuesterVpi.Wert > basis)
            {
                var fruehestens = Indexmiete.FruehestesWirksamwerden(vertrag.LetzteKaltmieteAenderung() ?? vertrag.Beginn);
                if (Indexmiete.WirksamAb(heute) >= fruehestens)
                {
                    aufgaben.Add(new Aufgabe("Mieten", $"Indexanpassung möglich: {name} (+{Indexmiete.VeraenderungProzent(basis, neuesterVpi.Wert).ToString("0.0", CultureInfo.GetCultureInfo("de-DE"))} %)", link, Dringlichkeit.Bald,
                        "Erklärung in Textform; die neue Miete gilt ab dem übernächsten Monat."));
                }
            }
        }

        return aufgaben;
    }
}
