using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Common;
using MietHahn.Application.Tax;
using MietHahn.Domain.Common;
using MietHahn.Domain.Properties;
using MietHahn.Domain.Receipts;
using MietHahn.Domain.Tax;
using MietHahn.Infrastructure.Common;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Tax;

internal sealed class MaintenanceService(MietHahnDbContext db) : IMaintenanceService
{
    public async Task<MaintenanceOverview?> GetAsync(Guid propertyId, CancellationToken cancellationToken = default)
    {
        var name = await db.Set<Property>().AsNoTracking().Where(o => o.Id == propertyId).Select(o => o.Name).SingleOrDefaultAsync(cancellationToken);
        if (name is null)
        {
            return null;
        }

        var rows = await LoadAsync(propertyId, cancellationToken);
        var distributions = await db.Set<MaintenanceDistribution>().AsNoTracking().Where(v => v.PropertyId == propertyId).ToDictionaryAsync(v => v.CostItemId, cancellationToken);
        var items = rows
            .Select(r => new MaintenanceItem(
                r.CostItemId,
                r.ReceiptId,
                r.Description,
                r.InvoiceDate,
                r.PaidOn,
                r.Amount,
                distributions.TryGetValue(r.CostItemId, out var v) ? v.Years : null,
                distributions.TryGetValue(r.CostItemId, out var d) ? d.Schedule() : []))
            .OrderByDescending(i => i.InvoiceDate)
            .ToList();

        var warnings = new List<string>();
        var unpaid = items.Count(i => i.PaidOn is null);
        if (unpaid > 0)
        {
            warnings.Add($"{unpaid} Position(en) sind noch nicht bezahlt. Nach dem Zuflussprinzip (§ 11 EStG) zählen sie erst ab der Zahlung und können erst dann verteilt werden.");
        }

        return new MaintenanceOverview(propertyId, name, items, warnings);
    }

    public Task<Result> DistributeAsync(Guid propertyId, DistributionInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            var costItemId = input.CostItemId ?? throw new RuleViolationException("Bitte eine Position wählen.");
            var item = (await LoadAsync(propertyId, cancellationToken)).SingleOrDefault(r => r.CostItemId == costItemId) ?? throw new NotFoundException();
            var paidOn = item.PaidOn ?? throw new RuleViolationException("Die Position ist noch nicht bezahlt; verteilt wird ab dem Jahr der Zahlung.");

            var distribution = await db.Set<MaintenanceDistribution>().SingleOrDefaultAsync(v => v.CostItemId == costItemId, cancellationToken);
            if (distribution is null)
            {
                distribution = new MaintenanceDistribution(costItemId, propertyId, item.Amount, paidOn.Year, input.Years);
                db.Add(distribution);
            }
            else
            {
                distribution.SetYears(input.Years);
            }

            await db.SaveChangesAsync(cancellationToken);
            return distribution.Id;
        });

    public Task<Result> RevokeAsync(Guid propertyId, Guid costItemId, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var distribution = await db.Set<MaintenanceDistribution>().SingleOrDefaultAsync(v => v.CostItemId == costItemId && v.PropertyId == propertyId, cancellationToken) ?? throw new NotFoundException();
            db.Remove(distribution);
            await db.SaveChangesAsync(cancellationToken);
            return costItemId;
        });

    public async Task<MaintenanceDeduction> DeductionAsync(Guid propertyId, int year, CancellationToken cancellationToken = default)
    {
        var rows = await LoadAsync(propertyId, cancellationToken);
        var distributions = await db.Set<MaintenanceDistribution>().AsNoTracking().Where(v => v.PropertyId == propertyId).ToDictionaryAsync(v => v.CostItemId, cancellationToken);
        var portions = new List<MaintenancePortion>();
        foreach (var row in rows)
        {
            if (distributions.TryGetValue(row.CostItemId, out var distribution))
            {
                if (distribution.Schedule().FirstOrDefault(r => r.Year == year) is { } instalment)
                {
                    portions.Add(new MaintenancePortion(row.CostItemId, row.ReceiptId, row.Description, instalment.Amount, true));
                }
            }
            else if (row.PaidOn?.Year == year)
            {
                portions.Add(new MaintenancePortion(row.CostItemId, row.ReceiptId, row.Description, row.Amount, false));
            }
        }

        return new MaintenanceDeduction(year, portions);
    }

    private async Task<List<Row>> LoadAsync(Guid propertyId, CancellationToken cancellationToken)
    {
        var raw = await db.Set<Receipt>().AsNoTracking()
            .SelectMany(b => b.Items.Where(p => p.PropertyId == propertyId && p.CostType == CostTypes.MaintenanceKey), (b, p) => new
            {
                CostItemId = p.Id,
                ReceiptId = b.Id,
                b.SupplierName,
                b.InvoiceNumber,
                p.Note,
                b.InvoiceDate,
                b.PaidOn,
                p.Amount,
            })
            .ToListAsync(cancellationToken);
        return raw
            .Select(r => new Row(
                r.CostItemId,
                r.ReceiptId,
                string.Join(" – ", new[] { r.SupplierName, r.InvoiceNumber, r.Note }.Where(t => !string.IsNullOrWhiteSpace(t))) is { Length: > 0 } text ? text : "Beleg",
                r.InvoiceDate,
                r.PaidOn,
                r.Amount))
            .ToList();
    }

    private sealed record Row(Guid CostItemId, Guid ReceiptId, string Description, DateOnly InvoiceDate, DateOnly? PaidOn, decimal Amount);
}
