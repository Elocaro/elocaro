using System.Globalization;
using Elocaro.Application.Common;
using Elocaro.Application.OperatingCosts;
using Elocaro.Application.Statements;
using Elocaro.Domain.Common;
using Elocaro.Domain.Leases;
using Elocaro.Domain.Metering;
using Elocaro.Domain.OperatingCosts;
using Elocaro.Domain.Properties;
using Elocaro.Domain.Receipts;
using Elocaro.Domain.Statements;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Statements;

/// <summary>
/// Statement wizard on top of the operating cost preview: adds the plausibility checks (including meters and occupancy),
/// freezes a released statement as a snapshot and keeps the revisions of a period.
/// </summary>
internal sealed class StatementService(ElocaroDbContext db, IOperatingCosts operatingCosts, TimeProvider clock) : IStatements
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public async Task<StatementWizard?> PrepareAsync(Guid propertyId, Period billingPeriod, decimal thresholdPercent = PlausibilityChecker.DefaultThresholdPercent, CancellationToken cancellationToken = default)
    {
        var end = billingPeriod.To ?? throw new RuleViolationException("Der Abrechnungszeitraum braucht ein Ende.");
        var preview = await operatingCosts.PreviewAsync(propertyId, billingPeriod, cancellationToken);
        if (preview is null)
        {
            return null;
        }

        BillingResult? previous = null;
        try
        {
            previous = (await operatingCosts.PreviewAsync(propertyId, new Period(billingPeriod.From.AddYears(-1), end.AddYears(-1)), cancellationToken))?.Result;
        }
        catch (RuleViolationException)
        {
            // No comparison possible (e.g. period of more than 12 months after shifting).
        }

        var findings = new List<PlausibilityFinding>(PlausibilityChecker.Check(preview.Result, previous, thresholdPercent));
        findings.AddRange(preview.Hints.Select(h => new PlausibilityFinding(WizardStep.Calculation, FindingSeverity.Warning, h)));
        findings.AddRange(await MeterFindingsAsync(propertyId, billingPeriod, cancellationToken));
        findings.AddRange(await OccupancyFindingsAsync(propertyId, billingPeriod, cancellationToken));
        findings.AddRange(await KeyFindingsAsync(propertyId, cancellationToken));
        findings.AddRange(await UnsplitReceiptFindingsAsync(billingPeriod, cancellationToken));
        findings.AddRange(await ObjectionFindingsAsync(preview.Result, cancellationToken));

        var today = GermanTime.Today(clock.GetUtcNow());
        var released = (await ListAsync(propertyId, cancellationToken)).FirstOrDefault(s => s.BillingPeriod == billingPeriod && s.Current);
        return new StatementWizard(
            preview,
            previous,
            findings,
            StatementDeadline.Of(billingPeriod),
            released is null ? StatementDeadline.StateOn(billingPeriod, today) : null,
            released,
            thresholdPercent,
            await db.Set<StatementReminderDismissal>().AnyAsync(d => d.PropertyId == propertyId && d.Year == billingPeriod.From.Year, cancellationToken));
    }

    public async Task<Result> ReleaseAsync(Guid propertyId, Period billingPeriod, bool acknowledgeWarnings, string? note, CancellationToken cancellationToken = default)
    {
        try
        {
            var wizard = await PrepareAsync(propertyId, billingPeriod, cancellationToken: cancellationToken);
            if (wizard is null)
            {
                return Result.Failed("Das Objekt wurde nicht gefunden.");
            }

            var errors = wizard.Findings.Where(f => f.Severity == FindingSeverity.Error).Select(f => f.Text).Distinct().ToList();
            if (errors.Count > 0)
            {
                return new Result(false, [.. errors.Prepend("Die Abrechnung kann nicht freigegeben werden, solange Angaben fehlen:")]);
            }

            if (wizard.HasWarnings && !acknowledgeWarnings)
            {
                return Result.Failed("Bitte bestätigen Sie, dass Sie die Hinweise geprüft haben.");
            }

            var snapshot = StatementSnapshot.Serialize(wizard.Preview.Result);
            var latest = await db.Set<Domain.Statements.BillingStatement>()
                .Where(s => s.PropertyId == propertyId && s.BillingPeriod.From == billingPeriod.From && s.BillingPeriod.To == billingPeriod.To)
                .OrderByDescending(s => s.Revision).FirstOrDefaultAsync(cancellationToken);
            if (latest is not null)
            {
                if (latest.Snapshot == snapshot)
                {
                    return Result.Failed("Seit der freigegebenen Fassung hat sich nichts geändert; es gibt keine neue Version.");
                }

                if (string.IsNullOrWhiteSpace(note))
                {
                    return Result.Failed("Für die Korrektur einer freigegebenen Abrechnung ist eine Begründung nötig.");
                }
            }

            var statement = new Domain.Statements.BillingStatement(
                propertyId,
                billingPeriod,
                (latest?.Revision ?? 0) + 1,
                latest?.Id,
                snapshot,
                wizard.Preview.Result.TotalCosts,
                wizard.Preview.Result.LandlordTotal,
                clock.GetUtcNow(),
                note);
            db.Add(statement);
            await db.SaveChangesAsync(cancellationToken);
            return Result.Ok(statement.Id);
        }
        catch (RuleViolationException ex)
        {
            return Result.Failed(ex.Message);
        }
        catch (DbUpdateException ex) when (DatabaseErrors.IsUniqueViolation(ex))
        {
            return Result.Failed("Die Abrechnung wurde inzwischen von jemand anderem freigegeben. Bitte laden Sie die Seite neu.");
        }
    }

    public async Task<IReadOnlyList<StatementSummary>> ListAsync(Guid propertyId, CancellationToken cancellationToken = default)
    {
        var rows = await db.Set<Domain.Statements.BillingStatement>().AsNoTracking()
            .Where(s => s.PropertyId == propertyId)
            .Select(s => new { Statement = s, s.CreatedBy })
            .ToListAsync(cancellationToken);
        var newest = rows.GroupBy(r => r.Statement.BillingPeriod).ToDictionary(g => g.Key, g => g.Max(r => r.Statement.Revision));
        return [.. rows
            .OrderByDescending(r => r.Statement.BillingPeriod.From).ThenByDescending(r => r.Statement.Revision)
            .Select(r => ToSummary(r.Statement, r.CreatedBy, newest[r.Statement.BillingPeriod]))];
    }

    public async Task<StoredStatement?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var statement = await db.Set<Domain.Statements.BillingStatement>().AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (statement is null)
        {
            return null;
        }

        var newest = await db.Set<Domain.Statements.BillingStatement>().AsNoTracking()
            .Where(s => s.PropertyId == statement.PropertyId && s.BillingPeriod.From == statement.BillingPeriod.From && s.BillingPeriod.To == statement.BillingPeriod.To)
            .MaxAsync(s => s.Revision, cancellationToken);
        var propertyName = await db.Set<Property>().AsNoTracking().Where(p => p.Id == statement.PropertyId).Select(p => p.Name).SingleAsync(cancellationToken);
        var result = StatementSnapshot.Deserialize(statement.Snapshot);

        StatementDifference? difference = null;
        var excluded = false;
        if (statement.SupersedesId is { } previousId)
        {
            var previous = await db.Set<Domain.Statements.BillingStatement>().AsNoTracking().SingleAsync(s => s.Id == previousId, cancellationToken);
            difference = StatementDifference.Between(StatementSnapshot.Deserialize(previous.Snapshot), result);
            excluded = difference.ClaimsExcluded(statement.BillingPeriod, GermanTime.Today(statement.ReleasedAt));
        }

        return new StoredStatement(ToSummary(statement, statement.CreatedBy, newest), propertyName, result, difference, excluded);
    }

    public async Task SetReminderDismissedAsync(Guid propertyId, int year, bool dismissed, CancellationToken cancellationToken = default)
    {
        var existing = await db.Set<StatementReminderDismissal>().SingleOrDefaultAsync(d => d.PropertyId == propertyId && d.Year == year, cancellationToken);
        if (dismissed && existing is null)
        {
            db.Add(new StatementReminderDismissal(propertyId, year));
        }
        else if (!dismissed && existing is not null)
        {
            db.Remove(existing);
        }
        else
        {
            return;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static StatementSummary ToSummary(Domain.Statements.BillingStatement s, string? releasedBy, int newestRevision) =>
        new(s.Id, s.PropertyId, s.BillingPeriod, s.Revision, s.ReleasedAt, releasedBy, s.TotalCosts, s.LandlordTotal, s.Note, s.Revision == newestRevision);

    /// <summary>Notes on typical objections: cost items without a receipt file, items marked as not allocable.</summary>
    private async Task<IReadOnlyList<PlausibilityFinding>> ObjectionFindingsAsync(BillingResult result, CancellationToken cancellationToken)
    {
        var ids = result.CostTypes.SelectMany(t => t.Items.Select(i => i.Id)).Distinct().ToList();
        var itemReceipts = await db.Set<CostItem>().AsNoTracking().Where(i => ids.Contains(i.Id)).Select(i => new { i.Id, i.ReceiptId }).ToListAsync(cancellationToken);
        var receiptIds = itemReceipts.Select(i => i.ReceiptId).Distinct().ToList();
        var withFile = (await db.Set<ReceiptFile>().AsNoTracking().Where(f => receiptIds.Contains(f.ReceiptId)).Select(f => f.ReceiptId).Distinct().ToListAsync(cancellationToken)).ToHashSet();
        return ObjectionChecker.Check(result, itemReceipts.Where(i => withFile.Contains(i.ReceiptId)).Select(i => i.Id).ToHashSet());
    }

    /// <summary>Meters in use during the period need a reading at the start and at the end of their use.</summary>

    /// <summary>Receipts of the period without a (complete) split are missing in the statement: a warning with the open sum.</summary>
    private async Task<List<PlausibilityFinding>> UnsplitReceiptFindingsAsync(Period period, CancellationToken cancellationToken)
    {
        var receipts = await db.Set<Receipt>().AsNoTracking().Include(r => r.Items)
            .Where(r => !r.Incomplete && r.InvoiceDate <= period.To!.Value && (r.ServiceTo ?? r.InvoiceDate) >= period.From)
            .ToListAsync(cancellationToken);
        var open = receipts.Where(r => !r.IsFullyAllocated).ToList();
        if (open.Count == 0)
        {
            return [];
        }

        var sum = open.Sum(r => r.Gross - r.Allocated);
        var text = $"{(open.Count == 1 ? "1 Beleg des Zeitraums ist" : $"{open.Count} Belege des Zeitraums sind")} nicht vollständig auf Kostenarten und Objekte aufgeteilt ({GermanText.Money(sum)} offen) und fehlt in dieser Abrechnung: Unter Geld → Belege (Filter „nicht aufgeteilt“) zuordnen.";
        return [new PlausibilityFinding(WizardStep.Costs, FindingSeverity.Warning, text)];
    }

    private async Task<List<PlausibilityFinding>> MeterFindingsAsync(Guid propertyId, Period period, CancellationToken cancellationToken)
    {
        var end = period.To!.Value;
        var meters = await db.Set<Meter>().AsNoTracking().Include(m => m.Readings)
            .Where(m => m.PropertyId == propertyId && !m.Terms.Any() && m.InstalledOn <= end && (m.RemovedOn == null || m.RemovedOn >= period.From))
            .OrderBy(m => m.Order).ThenBy(m => m.Number)
            .ToListAsync(cancellationToken);
        var findings = new List<PlausibilityFinding>();
        foreach (var meter in meters)
        {
            if (end > RemoteReadingRule.Deadline && RemoteReadingRule.IsMissing(meter))
            {
                findings.Add(new PlausibilityFinding(WizardStep.Meters, FindingSeverity.Info, $"Zähler {meter.Name} ({meter.Number}): {RemoteReadingRule.StatementHint}"));
            }

            var open = period.From.AddDays(-1) < meter.InstalledOn ? meter.InstalledOn : period.From.AddDays(-1);
            var close = meter.RemovedOn is { } removed && removed < end ? removed : end;
            foreach (var (day, label) in new[] { (open, "Anfangsstand"), (close, "Endstand") })
            {
                var reading = ConsumptionCalculator.ReadingAt(meter.Readings, day);
                if (reading is null)
                {
                    findings.Add(new PlausibilityFinding(WizardStep.Meters, FindingSeverity.Warning, $"Zähler {meter.Name} ({meter.Number}): {label} zum {day:dd.MM.yyyy} fehlt."));
                }
                else if (reading.Interpolated)
                {
                    findings.Add(new PlausibilityFinding(WizardStep.Meters, FindingSeverity.Info, $"Zähler {meter.Name} ({meter.Number}): {label} zum {day:dd.MM.yyyy} ist aus benachbarten Ablesungen interpoliert."));
                }
            }
        }

        return findings;
    }

    /// <summary>Rental objects (units, rooms of shared flats) without a lease for part of the period.</summary>
    private async Task<List<PlausibilityFinding>> OccupancyFindingsAsync(Guid propertyId, Period period, CancellationToken cancellationToken)
    {
        var units = await db.Set<Unit>().AsNoTracking().Include(u => u.Room)
            .Where(u => u.PropertyId == propertyId && (u.Type == UnitType.Apartment || u.Type == UnitType.Commercial))
            .OrderBy(u => u.Order).ToListAsync(cancellationToken);
        var unitIds = units.Select(u => u.Id).ToList();
        var end = period.To!.Value;
        var leases = await db.Set<Lease>().AsNoTracking()
            .Where(v => unitIds.Contains(v.UnitId) && v.Term.From <= end && (v.Term.To == null || v.Term.To >= period.From))
            .Include(v => v.ExtraRooms)
            .ToListAsync(cancellationToken);

        var findings = new List<PlausibilityFinding>();
        foreach (var unit in units)
        {
            var targets = unit.Room.Count > 0
                ? unit.Room.OrderBy(r => r.Order).Select(r => ($"{unit.Name}, {r.Name}", leases.Where(v => v.RoomId == r.Id || v.ExtraRooms.Any(x => x.RoomId == r.Id)))).ToList()
                : [(unit.Name, leases.Where(v => v.UnitId == unit.Id))];
            foreach (var (name, own) in targets)
            {
                foreach (var gap in Gaps(period, own.Select(v => v.Term)))
                {
                    var inner = gap.From > period.From && gap.To < end;
                    findings.Add(new PlausibilityFinding(
                        WizardStep.Occupancy,
                        inner ? FindingSeverity.Warning : FindingSeverity.Info,
                        $"{name}: nicht vermietet vom {gap.From:dd.MM.yyyy} bis {gap.To:dd.MM.yyyy} ({gap.Days} Tage); der Anteil geht an den Vermieter{(inner ? ", obwohl davor und danach ein Mietvertrag besteht – fehlt ein Vertrag?" : ".")}"));
                }
            }
        }

        return findings;
    }

    private static List<Period> Gaps(Period period, IEnumerable<Period> terms)
    {
        var end = period.To!.Value;
        var gaps = new List<Period>();
        var next = period.From;
        foreach (var term in terms.OrderBy(t => t.From))
        {
            if (term.From > next)
            {
                gaps.Add(new Period(next, term.From.AddDays(-1)));
            }

            if (term.To is not { } to)
            {
                return gaps;
            }

            next = to.AddDays(1) > next ? to.AddDays(1) : next;
        }

        if (next <= end)
        {
            gaps.Add(new Period(next, end));
        }

        return gaps;
    }

    /// <summary>Fixed shares of a cost type must add up to 100 %.</summary>
    private async Task<List<PlausibilityFinding>> KeyFindingsAsync(Guid propertyId, CancellationToken cancellationToken)
    {
        var rules = await db.Set<CostAllocationRule>().AsNoTracking().Include(r => r.Shares)
            .Where(r => r.PropertyId == propertyId && r.Key == AllocationKey.FixedPercent).ToListAsync(cancellationToken);
        return [.. rules
            .Where(r => r.Shares.Sum(a => a.Percent) != 100m)
            .Select(r => new PlausibilityFinding(
                WizardStep.Calculation,
                FindingSeverity.Warning,
                $"Umlageschlüssel „Festanteil in Prozent“ der Kostenart {r.CostType}: Die Anteile ergeben {r.Shares.Sum(a => a.Percent).ToString("0.##", German)} % statt 100 %.",
                r.CostType))];
    }
}
