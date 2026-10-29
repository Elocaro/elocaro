using Elocaro.Application.Metering;
using Elocaro.Domain.Common;
using Elocaro.Domain.Metering;
using Elocaro.Domain.Properties;
using Elocaro.Infrastructure.Common;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using ZaehlerEntitaet = Elocaro.Domain.Metering.Meter;

namespace Elocaro.Infrastructure.Metering;

internal sealed class MeterImport(ElocaroDbContext db) : IMeterImport
{
    private const string ReadBy = "Funkzähler-Import";

    public async Task<ReadingImportPreview> PreviewAsync(Guid propertyId, byte[] csv, CancellationToken cancellationToken = default)
    {
        var (preview, _) = await PlanAsync(propertyId, csv, tracking: false, cancellationToken);
        return preview;
    }

    public async Task<ReadingImportOutcome> ImportAsync(Guid propertyId, byte[] csv, CancellationToken cancellationToken = default)
    {
        var (preview, meters) = await PlanAsync(propertyId, csv, tracking: true, cancellationToken);
        if (!preview.CanImport)
        {
            var errors = preview.Errors.Count > 0 ? preview.Errors : ["Die Datei enthält keine neuen Zählerstände."];
            return new ReadingImportOutcome(false, errors, 0, preview.Rows.Count);
        }

        // Oldest first: the plausibility check of a reading looks at the ones before it.
        var imported = 0;
        foreach (var row in preview.Rows.Where(r => r.Status == ImportRowStatus.New).OrderBy(r => r.Date))
        {
            meters[row.MeterNumber.ToLowerInvariant()].RecordReading(row.Date!.Value, row.Value!.Value, ReadingType.Reading, ReadBy);
            imported++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return new ReadingImportOutcome(true, [], imported, preview.Rows.Count - imported);
    }

    private async Task<(ReadingImportPreview Preview, Dictionary<string, ZaehlerEntitaet> Meters)> PlanAsync(Guid propertyId, byte[] csv, bool tracking, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(csv);
        var query = db.Set<ZaehlerEntitaet>().Include(z => z.Readings).Where(z => z.PropertyId == propertyId);
        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        var meters = (await query.ToListAsync(cancellationToken)).GroupBy(z => z.Number.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.OrderByDescending(z => z.InstalledOn).First());
        var (raw, error) = ReadingCsv.Parse(TextDecoding.Read(csv));
        if (error is not null)
        {
            return (new ReadingImportPreview([], [error]), meters);
        }

        var rows = new List<ImportRow>();
        // Several rows for one meter are checked in date order and against each other.
        var added = new Dictionary<(string Number, DateOnly Date), decimal>();
        foreach (var row in raw.OrderBy(r => r.Date).ThenBy(r => r.Line))
        {
            rows.Add(Check(row, meters, added));
        }

        return (new ReadingImportPreview(rows.OrderBy(r => r.Line).ToList(), []), meters);
    }

    private static ImportRow Check(ReadingCsv.Row row, Dictionary<string, ZaehlerEntitaet> meters, Dictionary<(string Number, DateOnly Date), decimal> added)
    {
        ImportRow Make(ImportRowStatus status, string? note, string? name = null) => new(row.Line, row.Number, name, row.Date, row.Value, status, note);
        if (row.Problem is not null)
        {
            return Make(ImportRowStatus.Rejected, row.Problem);
        }

        if (!meters.TryGetValue(row.Number.ToLowerInvariant(), out var meter))
        {
            return Make(ImportRowStatus.UnknownMeter, "Kein Zähler mit dieser Nummer im Objekt.");
        }

        var date = row.Date!.Value;
        var value = row.Value!.Value;
        if (meter.Readings.FirstOrDefault(s => s.Date == date) is { } existing)
        {
            return existing.Value == value
                ? Make(ImportRowStatus.Duplicate, "Der Stand ist schon erfasst.", meter.Name)
                : Make(ImportRowStatus.Rejected, $"Für den {date:dd.MM.yyyy} ist schon ein anderer Stand erfasst ({existing.Value:#,##0.###}).", meter.Name);
        }

        if (added.TryGetValue((meter.Number.ToLowerInvariant(), date), out var other))
        {
            return other == value
                ? Make(ImportRowStatus.Duplicate, "Der Stand steht in der Datei doppelt.", meter.Name)
                : Make(ImportRowStatus.Rejected, "In der Datei stehen für diesen Tag zwei verschiedene Stände.", meter.Name);
        }

        if (!meter.IsActiveOn(date))
        {
            return Make(ImportRowStatus.Rejected, $"Der Zähler war am {date:dd.MM.yyyy} nicht eingebaut.", meter.Name);
        }

        // Stored readings and the ones of this file that are already accepted count together.
        var key = meter.Number.ToLowerInvariant();
        var known = meter.Readings.Select(r => (r.Date, r.Value))
            .Concat(added.Where(a => a.Key.Number == key).Select(a => (a.Key.Date, a.Value))).ToList();
        if (known.Where(k => k.Date < date).OrderByDescending(k => k.Date).Cast<(DateOnly Date, decimal Value)?>().FirstOrDefault() is { } before && value < before.Value)
        {
            return Make(ImportRowStatus.Rejected, $"Rückwärtslauf: kleiner als der Stand vom {before.Date:dd.MM.yyyy} ({before.Value:#,##0.###}).", meter.Name);
        }

        if (known.Where(k => k.Date > date).OrderBy(k => k.Date).Cast<(DateOnly Date, decimal Value)?>().FirstOrDefault() is { } after && value > after.Value)
        {
            return Make(ImportRowStatus.Rejected, $"Größer als der spätere Stand vom {after.Date:dd.MM.yyyy} ({after.Value:#,##0.###}).", meter.Name);
        }

        var warnings = meter.Validate(date, value);
        added[(meter.Number.ToLowerInvariant(), date)] = value;
        return Make(ImportRowStatus.New, warnings.Count > 0 ? warnings[0] : null, meter.Name);
    }
}
