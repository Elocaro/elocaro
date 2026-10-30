using System.Globalization;
using Elocaro.Application.Accounts;
using Elocaro.Application.Deadlines;
using Elocaro.Domain.Common;
using Elocaro.Domain.Deadlines;
using Elocaro.Domain.Documents;
using Elocaro.Domain.Leases;
using Elocaro.Domain.Properties;
using Elocaro.Domain.Statements;
using Elocaro.Domain.Tax;
using Elocaro.Infrastructure.Leases;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using ZaehlerEntitaet = Elocaro.Domain.Metering.Meter;

namespace Elocaro.Infrastructure.Deadlines;

/// <summary>Deadline for the operating cost statement (§ 556 para. 3 BGB): last year and the current one, until a statement is released.</summary>
internal sealed class StatementDeadlineSource(ElocaroDbContext db, TimeProvider clock) : IDeadlineSource
{
    public async Task<IReadOnlyList<DeadlineEntry>> DeadlinesAsync(DateOnly until, CancellationToken cancellationToken = default)
    {
        var today = GermanTime.Today(clock.GetUtcNow());
        var properties = await db.Set<Property>().AsNoTracking().Select(p => new { p.Id, p.Name }).ToListAsync(cancellationToken);
        var released = (await db.Set<BillingStatement>().AsNoTracking()
            .Select(s => new { s.PropertyId, s.BillingPeriod.From, s.BillingPeriod.To }).ToListAsync(cancellationToken))
            .Select(s => (s.PropertyId, Period: new Period(s.From, s.To))).ToHashSet();

        var dismissed = (await db.Set<StatementReminderDismissal>().AsNoTracking().Select(d => new { d.PropertyId, d.Year }).ToListAsync(cancellationToken))
            .Select(d => (d.PropertyId, d.Year)).ToHashSet();

        var entries = new List<DeadlineEntry>();
        foreach (var year in new[] { today.Year - 1, today.Year })
        {
            var period = Period.CalendarYear(year);
            var deadline = StatementDeadline.Of(period);
            if (deadline > until)
            {
                continue;
            }

            var billed = await db.Set<Lease>().AsNoTracking()
                .Where(v => v.BillingType == BillingType.Prepayment && v.Term.From <= period.To && (v.Term.To == null || v.Term.To >= period.From))
                .Join(db.Set<Unit>(), v => v.UnitId, u => u.Id, (v, u) => u.PropertyId)
                .Distinct().ToListAsync(cancellationToken);
            foreach (var property in properties.Where(p => billed.Contains(p.Id) && !released.Contains((p.Id, period)) && !dismissed.Contains((p.Id, year))))
            {
                entries.Add(new DeadlineEntry(
                    $"statement:{property.Id}:{year}",
                    DeadlineOrigin.Derived,
                    "Abrechnung",
                    $"Abrechnung {year} erstellen: {property.Name}",
                    deadline,
                    $"/verwaltung/objekte/{property.Id}/abrechnung?jahr={year}",
                    AccessRight.Finance,
                    "Letzter Tag für Nachforderungen nach § 556 Abs. 3 BGB.",
                    LeadDays: 90));
            }
        }

        return entries;
    }
}

/// <summary>End of fixed terms and terminated leases, graduated steps and the earliest dates for rent increases.</summary>
internal sealed class LeaseDeadlineSource(ElocaroDbContext db, TimeProvider clock) : IDeadlineSource
{
    public async Task<IReadOnlyList<DeadlineEntry>> DeadlinesAsync(DateOnly until, CancellationToken cancellationToken = default)
    {
        var today = GermanTime.Today(clock.GetUtcNow());
        var leases = await db.Set<Lease>().AsNoTracking()
            .Include(v => v.RentSteps).Include(v => v.Components).AsSplitQuery()
            .Where(v => v.Term.To == null || v.Term.To >= today)
            .ToListAsync(cancellationToken);
        if (leases.Count == 0)
        {
            return [];
        }

        var rentalUnits = await RentalUnit.GetAsync(db, cancellationToken);
        var unitsWithMeters = (await db.Set<ZaehlerEntitaet>().AsNoTracking().Where(m => m.UnitId != null && m.RemovedOn == null && !m.Terms.Any()).Select(m => m.UnitId!.Value).Distinct().ToListAsync(cancellationToken)).ToHashSet();
        var entries = new List<DeadlineEntry>();
        foreach (var lease in leases)
        {
            var name = rentalUnits.GetValueOrDefault(lease.RentalUnitId)?.Name ?? "Mietobjekt";
            var link = $"/verwaltung/vertraege/{lease.Id}";
            if (unitsWithMeters.Contains(lease.UnitId) && rentalUnits.GetValueOrDefault(lease.RentalUnitId)?.PropertyId is { } propertyId)
            {
                // A reading on the day of the tenant change avoids interpolating the consumption between two distant readings.
                foreach (var (day, what) in new[] { (lease.End, "Auszug"), (lease.Start > today ? lease.Start : (DateOnly?)null, "Einzug") })
                {
                    if (day is { } date && date <= until)
                    {
                        entries.Add(new DeadlineEntry(
                            $"reading:{lease.Id}:{what}", DeadlineOrigin.Derived, "Zähler",
                            $"Zwischenablesung bei {what}: {name}",
                            date, $"/verwaltung/objekte/{propertyId}/ablesung?einheit={lease.UnitId}&datum={date:yyyy-MM-dd}", AccessRight.MasterData,
                            "Zählerstände am Tag des Mieterwechsels ablesen, damit der Verbrauch nicht interpoliert werden muss.",
                            LeadDays: 7));
                    }
                }
            }

            if (lease.End is { } end && end <= until)
            {
                var terminated = lease.TerminationReceivedOn is not null;
                entries.Add(new DeadlineEntry(
                    $"lease-end:{lease.Id}", DeadlineOrigin.Derived, "Mietverträge",
                    terminated ? $"Mietverhältnis endet (gekündigt): {name}" : $"Befristung endet: {name}",
                    end, link, AccessRight.MasterData,
                    terminated ? "Übergabe, Kaution und Abrechnung vorbereiten." : "Verlängern oder Auszug vorbereiten (§ 575 BGB).",
                    LeadDays: 60));
            }

            foreach (var step in lease.RentSteps.Where(s => s.AppliedAt is null && s.From >= today && s.From <= until))
            {
                entries.Add(new DeadlineEntry(
                    $"rent-step:{step.Id}", DeadlineOrigin.Derived, "Mieten",
                    $"Staffel {name}: {step.BaseRent.ToString("#,##0.00", CultureInfo.GetCultureInfo("de-DE"))} € Kaltmiete",
                    step.From, link, AccessRight.MasterData, "Wird am Stichtag automatisch als Kaltmiete übernommen."));
            }

            if (lease.End == null && (lease.RentModel == RentModel.Standard || lease.RentModel == RentModel.Index))
            {
                var lastChange = lease.LastBaseRentChange(today);
                var earliest = lease.RentModel == RentModel.Index
                    ? IndexedRent.EarliestEffectiveDate(lastChange ?? lease.Start)
                    : ComparativeRent.EarliestDeclaration(lastChange ?? lease.Start, lastChange is not null);
                if (earliest >= today && earliest <= until)
                {
                    entries.Add(new DeadlineEntry(
                        $"rent-increase:{lease.Id}:{earliest:yyyyMMdd}", DeadlineOrigin.Derived, "Mieten",
                        lease.RentModel == RentModel.Index ? $"Indexanpassung möglich ab: {name}" : $"Mieterhöhung frühestens möglich: {name}",
                        earliest, $"{link}/mieterhoehung", AccessRight.MasterData,
                        lease.RentModel == RentModel.Index ? "Sperrfrist von einem Jahr (§ 557b BGB) ist dann abgelaufen." : "Wartefristen nach § 558 BGB sind dann erfüllt."));
                }
            }
        }

        return entries;
    }
}

/// <summary>End of the fixed interest rate of loans.</summary>
internal sealed class LoanDeadlineSource(ElocaroDbContext db, TimeProvider clock) : IDeadlineSource
{
    public async Task<IReadOnlyList<DeadlineEntry>> DeadlinesAsync(DateOnly until, CancellationToken cancellationToken = default)
    {
        var since = GermanTime.Today(clock.GetUtcNow()).AddYears(-1);
        var loans = await (from d in db.Set<Loan>().AsNoTracking()
                           join o in db.Set<Property>().AsNoTracking() on d.PropertyId equals o.Id
                           where d.FixedUntil != null && d.FixedUntil >= since && d.FixedUntil <= until
                           select new { d.Id, d.Bank, d.LoanNumber, d.FixedUntil, Property = o.Name })
            .ToListAsync(cancellationToken);
        return loans.Select(d => new DeadlineEntry(
            $"loan-fixed:{d.Id}", DeadlineOrigin.Derived, "Darlehen",
            $"Zinsbindung endet: {d.Bank} ({d.Property})",
            d.FixedUntil!.Value, $"/verwaltung/finanzen/darlehen/{d.Id}", AccessRight.Finance,
            "Anschlussfinanzierung oder Sondertilgung rechtzeitig klären.", LeadDays: 180)).ToList();
    }
}

/// <summary>Calibration deadlines of meters.</summary>
internal sealed class CalibrationDeadlineSource(ElocaroDbContext db, TimeProvider clock) : IDeadlineSource
{
    public async Task<IReadOnlyList<DeadlineEntry>> DeadlinesAsync(DateOnly until, CancellationToken cancellationToken = default)
    {
        _ = clock;
        var meters = await db.Set<ZaehlerEntitaet>().AsNoTracking()
            .Where(z => z.RemovedOn == null && z.CalibrationDueDate != null && z.CalibrationDueDate <= until)
            .Select(z => new { z.Id, z.Name, z.Number, z.CalibrationDueDate })
            .ToListAsync(cancellationToken);
        return meters.Select(z => new DeadlineEntry(
            $"calibration:{z.Id}", DeadlineOrigin.Derived, "Zähler",
            $"Eichfrist: {z.Name} ({z.Number})",
            z.CalibrationDueDate!.Value, $"/verwaltung/zaehler/{z.Id}", AccessRight.MasterData,
            "Nicht geeichte Zähler dürfen nicht für die Abrechnung verwendet werden: Zähler tauschen lassen.", LeadDays: 90)).ToList();
    }
}

/// <summary>Documents with an expiry date (energy certificate, insurance ...).</summary>
internal sealed class DocumentDeadlineSource(ElocaroDbContext db) : IDeadlineSource
{
    public async Task<IReadOnlyList<DeadlineEntry>> DeadlinesAsync(DateOnly until, CancellationToken cancellationToken = default)
    {
        var documents = await db.Set<Document>().AsNoTracking()
            .Where(d => d.ExpiresOn != null && d.ExpiresOn <= until)
            .Select(d => new { d.Id, d.Title, d.Category, d.ExpiresOn })
            .ToListAsync(cancellationToken);
        return documents.Select(d => new DeadlineEntry(
            $"document:{d.Id}", DeadlineOrigin.Derived, "Dokumente",
            d.Category switch
            {
                DocumentCategory.EnergyCertificate => $"Energieausweis läuft ab: {d.Title}",
                DocumentCategory.Insurance => $"Versicherung läuft ab: {d.Title}",
                _ => $"Läuft ab: {d.Title}",
            },
            d.ExpiresOn!.Value, $"/verwaltung/dokumente/{d.Id}", AccessRight.MasterData,
            "Verlängern bzw. neues Dokument ablegen; das Ablaufdatum am alten Dokument dann entfernen.", LeadDays: 60)).ToList();
    }
}

/// <summary>Maintenance tasks entered by hand.</summary>
internal sealed class TaskDeadlineSource(ElocaroDbContext db) : IDeadlineSource
{
    public async Task<IReadOnlyList<DeadlineEntry>> DeadlinesAsync(DateOnly until, CancellationToken cancellationToken = default)
    {
        var tasks = await (from t in db.Set<RecurringTask>().AsNoTracking()
                           join o in db.Set<Property>().AsNoTracking() on t.PropertyId equals o.Id into properties
                           from o in properties.DefaultIfEmpty()
                           where t.Active && t.DueOn <= until
                           select new { t.Id, t.Title, t.DueOn, t.LeadDays, t.Note, Property = o == null ? null : o.Name })
            .ToListAsync(cancellationToken);
        return tasks.Select(t => new DeadlineEntry(
            $"task:{t.Id}", DeadlineOrigin.Task, "Wartung",
            t.Property is null ? t.Title : $"{t.Title} ({t.Property})",
            t.DueOn, $"/verwaltung/fristen/wartung/{t.Id}", AccessRight.MasterData, t.Note, t.LeadDays, t.Id)).ToList();
    }
}
