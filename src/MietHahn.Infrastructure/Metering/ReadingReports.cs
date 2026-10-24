using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Accounts;
using MietHahn.Application.Admin;
using MietHahn.Application.Common;
using MietHahn.Application.Metering;
using MietHahn.Application.Todos;
using MietHahn.Domain.Common;
using MietHahn.Domain.Metering;
using MietHahn.Domain.Properties;
using MietHahn.Infrastructure.Common;
using MietHahn.Infrastructure.Leases;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Metering;

internal sealed class ReadingReports(MietHahnDbContext db, IMeterService meters, TimeProvider clock) : IReadingReports
{
    public async Task<IReadOnlyList<ReadingReportRow>> OpenAsync(Guid? propertyId = null, CancellationToken cancellationToken = default)
    {
        var reports = await db.Set<ReadingReport>().AsNoTracking().Where(r => r.Status == ReadingReportStatus.Open).OrderBy(r => r.CreatedAt).ToListAsync(cancellationToken);
        if (reports.Count == 0)
        {
            return [];
        }

        var ids = reports.Select(r => r.MeterId).Distinct().ToList();
        var meterList = await db.Set<Meter>().AsNoTracking().Include(m => m.Readings).Where(m => ids.Contains(m.Id)).AsSplitQuery().ToDictionaryAsync(m => m.Id, cancellationToken);
        var properties = await db.Set<Property>().AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);
        var units = await RentalUnit.GetAsync(db, cancellationToken);
        var rows = new List<ReadingReportRow>();
        foreach (var report in reports)
        {
            var meter = meterList[report.MeterId];
            if (propertyId is { } wanted && meter.PropertyId != wanted)
            {
                continue;
            }

            var before = meter.Readings.Where(s => s.Date <= report.Date).MaxBy(s => s.Date);
            var assignment = (meter.RoomId ?? meter.UnitId) is { } rentalId && units.TryGetValue(rentalId, out var unit) ? unit.Name : "Haus";
            rows.Add(new ReadingReportRow(
                report.Id, meter.Id, meter.PropertyId, properties.GetValueOrDefault(meter.PropertyId, ""), meter.Name, meter.Number, meter.MeasurementUnit, assignment,
                report.Date, report.Value, before?.Value, before?.Date, report.ReportedBy, report.Note, meter.Validate(report.Date, report.Value)));
        }

        return rows;
    }

    public async Task<ReadingResult> AcceptAsync(Guid id, bool confirmed = false, CancellationToken cancellationToken = default)
    {
        var report = await db.Set<ReadingReport>().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (report is null)
        {
            return new ReadingResult(false, ["Die Meldung wurde nicht gefunden."], []);
        }

        if (report.Status != ReadingReportStatus.Open)
        {
            return new ReadingResult(false, ["Die Meldung wurde schon bearbeitet."], []);
        }

        var result = await meters.RecordReadingAsync(report.MeterId, new ReadingInput
        {
            Date = report.Date,
            Value = report.Value,
            Kind = ReadingType.Reading,
            Note = $"Vom Mieter gemeldet ({report.ReportedBy})" + (report.Note is null ? "" : ": " + report.Note),
            Confirmed = confirmed,
        }, null, cancellationToken);
        if (!result.Succeeded)
        {
            return result;
        }

        // RecordReadingAsync works on its own scope of the context; load the report again before changing it.
        db.ChangeTracker.Clear();
        var open = await db.Set<ReadingReport>().SingleAsync(r => r.Id == id, cancellationToken);
        open.Accept(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return result;
    }

    public Task<Result> RejectAsync(Guid id, string? reason, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var report = await db.Set<ReadingReport>().SingleOrDefaultAsync(r => r.Id == id, cancellationToken) ?? throw new NotFoundException();
            report.Reject(reason, clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });
}

/// <summary>Dashboard: readings that tenants reported and that wait for the landlord to check them.</summary>
[Module(AppModule.Metering)]
internal sealed class ReadingReportTodos(MietHahnDbContext db) : ITodoSource
{
    public async Task<IReadOnlyList<TodoItem>> TodosAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.IsInRole(Roles.Admin) && !user.HasClaim(Permissions.ClaimType, nameof(AccessRight.MasterData)))
        {
            return [];
        }

        var open = await (from report in db.Set<ReadingReport>().AsNoTracking()
                          join meter in db.Set<Meter>().AsNoTracking() on report.MeterId equals meter.Id
                          where report.Status == ReadingReportStatus.Open
                          select new { meter.PropertyId, report.CreatedAt }).ToListAsync(cancellationToken);
        return open.GroupBy(o => o.PropertyId).Select(g => new TodoItem(
            "Zähler",
            g.Count() == 1 ? "1 gemeldeten Zählerstand prüfen" : $"{g.Count()} gemeldete Zählerstände prüfen",
            $"/verwaltung/objekte/{g.Key}/zaehler",
            Urgency.Soon,
            "Mieter haben Zählerstände im Portal gemeldet. Übernehmen oder ablehnen.",
            DateOnly.FromDateTime(g.Min(o => o.CreatedAt).Date))).ToList();
    }
}
