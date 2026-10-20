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

internal sealed class DepreciationService(MietHahnDbContext db, TimeProvider clock) : IDepreciationService
{
    /// <summary>The plan runs to the end of the current year, at least five years beyond the acquisition.</summary>
    private int LastYear(int firstYear) => Math.Max(GermanTime.Today(clock.GetUtcNow()).Year, firstYear + 5);

    public async Task<DepreciationOverview?> GetAsync(Guid propertyId, CancellationToken cancellationToken = default)
    {
        var name = await db.Set<Property>().AsNoTracking().Where(o => o.Id == propertyId).Select(o => o.Name).SingleOrDefaultAsync(cancellationToken);
        if (name is null)
        {
            return null;
        }

        var acquisition = await db.Set<PropertyAcquisition>().AsNoTracking().SingleOrDefaultAsync(a => a.PropertyId == propertyId, cancellationToken);
        var costs = await db.Set<PropertyCost>().AsNoTracking().Where(k => k.PropertyId == propertyId).OrderBy(k => k.Date).ToListAsync(cancellationToken);
        var assets = await db.Set<PropertyAsset>().AsNoTracking().Where(w => w.PropertyId == propertyId).OrderBy(w => w.AcquisitionDate).ToListAsync(cancellationToken);

        // Maintenance from receipts counts for the 15 % limit as well (date of payment, otherwise invoice date)
        var receiptCosts = await db.Set<Receipt>().AsNoTracking()
            .SelectMany(b => b.Items.Where(p => p.PropertyId == propertyId && p.CostType == CostTypes.MaintenanceKey && p.Amount > 0), (b, p) => new { b.InvoiceDate, b.PaidOn, p.Amount })
            .ToListAsync(cancellationToken);
        var calculationCosts = costs.Select(k => k.ToCalculation())
            .Concat(receiptCosts.Select(r => new SubsequentCost(r.PaidOn ?? r.InvoiceDate, r.Amount, "Beleg", true)))
            .ToList();

        var plan = acquisition is null
            ? null
            : DepreciationCalculator.Calculate(
                acquisition.ToCalculation(),
                calculationCosts,
                assets.Select(w => w.ToCalculation()).ToList(),
                LastYear(acquisition.PurchaseDate.Year));

        return new DepreciationOverview(
            propertyId,
            name,
            acquisition is null ? null : ToInput(acquisition),
            costs.Select(k => new CostRow(k.Id, k.Date, k.Amount, k.Description, k.IsMaintenance)).ToList(),
            assets.Select(w => new AssetRow(w.Id, w.Name, w.AcquisitionDate, w.Cost, w.UsefulLifeYears)).ToList(),
            plan);
    }

    public Task<Result> SaveAcquisitionAsync(Guid propertyId, AcquisitionInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            await EnsurePropertyAsync(propertyId, cancellationToken);
            var acquisition = await db.Set<PropertyAcquisition>().SingleOrDefaultAsync(a => a.PropertyId == propertyId, cancellationToken);
            if (acquisition is null)
            {
                acquisition = new PropertyAcquisition(propertyId);
                db.Add(acquisition);
            }

            acquisition.Set(
                input.PurchaseDate ?? throw new RuleViolationException("Bitte das Kaufdatum angeben."),
                input.PurchasePrice ?? throw new RuleViolationException("Bitte den Kaufpreis angeben."),
                (input.Notary ?? 0m, input.LandRegistry ?? 0m, input.TransferTax ?? 0m, input.Broker ?? 0m),
                input.LandSharePercent ?? throw new RuleViolationException("Bitte den Anteil Grund und Boden angeben."),
                input.CompletionYear ?? throw new RuleViolationException("Bitte das Baujahr bzw. Fertigstellungsjahr angeben."),
                input.RateOverridePercent,
                input.SpecialRatePercent,
                input.SpecialYears,
                input.Notes);
            await db.SaveChangesAsync(cancellationToken);
            return acquisition.Id;
        });

    public Task<Result> AddCostAsync(Guid propertyId, CostInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            await EnsurePropertyAsync(propertyId, cancellationToken);
            var cost = new PropertyCost(
                propertyId,
                input.Date ?? throw new RuleViolationException("Bitte das Datum angeben."),
                input.Amount ?? throw new RuleViolationException("Bitte den Nettobetrag angeben."),
                input.Description,
                input.IsMaintenance);
            db.Add(cost);
            await db.SaveChangesAsync(cancellationToken);
            return cost.Id;
        });

    public Task<Result> RemoveCostAsync(Guid propertyId, Guid costId, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var cost = await db.Set<PropertyCost>().SingleOrDefaultAsync(k => k.Id == costId && k.PropertyId == propertyId, cancellationToken) ?? throw new NotFoundException();
            db.Remove(cost);
            await db.SaveChangesAsync(cancellationToken);
            return costId;
        });

    public Task<Result> AddAssetAsync(Guid propertyId, AssetInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            await EnsurePropertyAsync(propertyId, cancellationToken);
            var asset = new PropertyAsset(
                propertyId,
                input.Name,
                input.AcquisitionDate ?? throw new RuleViolationException("Bitte das Anschaffungsdatum angeben."),
                input.Cost ?? throw new RuleViolationException("Bitte die Anschaffungskosten angeben."),
                input.UsefulLifeYears);
            db.Add(asset);
            await db.SaveChangesAsync(cancellationToken);
            return asset.Id;
        });

    public Task<Result> RemoveAssetAsync(Guid propertyId, Guid assetId, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var asset = await db.Set<PropertyAsset>().SingleOrDefaultAsync(w => w.Id == assetId && w.PropertyId == propertyId, cancellationToken) ?? throw new NotFoundException();
            db.Remove(asset);
            await db.SaveChangesAsync(cancellationToken);
            return assetId;
        });

    private async Task EnsurePropertyAsync(Guid propertyId, CancellationToken cancellationToken)
    {
        if (!await db.Set<Property>().AnyAsync(o => o.Id == propertyId, cancellationToken))
        {
            throw new NotFoundException();
        }
    }

    private static AcquisitionInput ToInput(PropertyAcquisition a) => new()
    {
        PurchaseDate = a.PurchaseDate,
        PurchasePrice = a.PurchasePrice,
        Notary = a.Notary,
        LandRegistry = a.LandRegistry,
        TransferTax = a.TransferTax,
        Broker = a.Broker,
        LandSharePercent = a.LandSharePercent,
        CompletionYear = a.CompletionYear,
        RateOverridePercent = a.RateOverridePercent,
        SpecialRatePercent = a.SpecialRatePercent,
        SpecialYears = a.SpecialYears,
        Notes = a.Notes,
    };
}
