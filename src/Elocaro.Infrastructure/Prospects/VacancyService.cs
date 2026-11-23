using Elocaro.Application.Admin;
using Elocaro.Application.Common;
using Elocaro.Application.Deadlines;
using Elocaro.Application.Leases;
using Elocaro.Application.Prospects;
using Elocaro.Domain.Common;
using Elocaro.Domain.Leases;
using Elocaro.Domain.Prospects;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Prospects;

internal sealed class VacancyService(ElocaroDbContext db, ILeaseService leases, TimeProvider clock) : IVacancies
{
    /// <summary>Days of vacancy after which a unit without any prospect is pointed out.</summary>
    internal const int NoticeAfterDays = 30;

    private const decimal DaysPerMonth = 365.25m / 12m;

    public async Task<IReadOnlyList<VacancyRow>> ListAsync(CancellationToken cancellationToken = default)
    {
        var today = GermanTime.Today(clock.GetUtcNow());
        var vacant = (await leases.RentalUnitsAsync(cancellationToken)).Where(u => u.CurrentlyOccupied is null).ToList();
        var ids = vacant.Select(u => u.Id).ToList();
        var all = await db.Set<Lease>().AsNoTracking().Include(l => l.Components).Where(l => ids.Contains(l.RentalUnitId)).ToListAsync(cancellationToken);
        var prospects = (await db.Set<Prospect>().AsNoTracking().Where(p => ids.Contains(p.RentalUnitId) && p.Status != ProspectStatus.Accepted && p.Status != ProspectStatus.Declined).ToListAsync(cancellationToken))
            .ToLookup(p => p.RentalUnitId);
        var rows = new List<VacancyRow>();
        foreach (var unit in vacant)
        {
            var own = all.Where(l => l.RentalUnitId == unit.Id).ToList();
            if (own.Any(l => l.Start > today))
            {
                continue; // already re-let
            }

            var last = own.Where(l => l.End is not null).MaxBy(l => l.End);
            var since = last?.End?.AddDays(1);
            var days = since is { } s ? Math.Max(0, today.DayNumber - s.DayNumber) : (int?)null;
            var rent = last is null ? null : last.AmountAt(RentComponentType.BaseRent, last.End!.Value);
            var open = prospects[unit.Id].ToList();
            var next = open.Where(p => p.ViewingOn >= today).Select(p => p.ViewingOn).Min();
            rows.Add(new VacancyRow(unit.Id, unit.Name, unit.Property, since, days, rent, rent is { } r && days is { } d ? Math.Round(r * d / DaysPerMonth, 2) : null, open.Count, next, Hint(days, open.Count, next)));
        }

        return [.. rows.OrderByDescending(r => r.Days ?? int.MaxValue).ThenBy(r => r.Property, StringComparer.CurrentCultureIgnoreCase).ThenBy(r => r.RentalUnit, StringComparer.CurrentCultureIgnoreCase)];
    }

    internal static string? Hint(int? days, int openProspects, DateOnly? nextViewing) =>
        openProspects == 0 ? (days is >= NoticeAfterDays ? $"Seit {days} Tagen leer und kein Interessent erfasst: Exposé erstellen und vermarkten." : days is null ? "Noch nie vermietet und kein Interessent erfasst." : null)
        : nextViewing is null ? "Interessenten vorhanden, aber kein Besichtigungstermin geplant." : null;
}

/// <summary>Reminder for vacant units that stand empty for a month without any prospect.</summary>
internal sealed class VacancyDeadlineSource(IVacancies vacancies, IModuleState modules) : IDeadlineSource
{
    public async Task<IReadOnlyList<DeadlineEntry>> DeadlinesAsync(DateOnly until, CancellationToken cancellationToken = default)
    {
        if (!modules.IsEnabled(AppModule.Prospects))
        {
            return [];
        }

        return [.. (await vacancies.ListAsync(cancellationToken))
            .Where(v => v is { OpenProspects: 0, VacantSince: not null })
            .Select(v => new DeadlineEntry(
                $"vacancy:{v.RentalUnitId}",
                DeadlineOrigin.Derived,
                "Vermarktung",
                $"Leerstand vermarkten: {v.RentalUnit}, {v.Property}",
                v.VacantSince!.Value.AddDays(VacancyService.NoticeAfterDays),
                "/verwaltung/leerstand",
                AccessRight.MasterData,
                "Die Einheit steht seit einem Monat leer und es ist kein Interessent erfasst."))
            .Where(e => e.Due <= until)];
    }
}
