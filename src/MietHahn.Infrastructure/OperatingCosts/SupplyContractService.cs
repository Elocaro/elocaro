using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Common;
using MietHahn.Application.OperatingCosts;
using MietHahn.Domain.Common;
using MietHahn.Domain.OperatingCosts;
using MietHahn.Domain.Properties;
using MietHahn.Domain.Receipts;
using MietHahn.Infrastructure.Common;
using MietHahn.Infrastructure.Persistence;
using Zaehler = MietHahn.Domain.Metering.Meter;

namespace MietHahn.Infrastructure.OperatingCosts;

internal sealed class SupplyContractService(MietHahnDbContext db) : ISupplyContracts
{
    public async Task<SupplyOverview?> GetAsync(Guid propertyId, CancellationToken cancellationToken = default)
    {
        var property = await db.Set<Property>().AsNoTracking().SingleOrDefaultAsync(o => o.Id == propertyId, cancellationToken);
        if (property is null)
        {
            return null;
        }

        var meters = await db.Set<Zaehler>().AsNoTracking().Where(m => m.PropertyId == propertyId).OrderBy(m => m.Type).ThenBy(m => m.Name).ToListAsync(cancellationToken);
        var contracts = await db.Set<SupplyContract>().AsNoTracking().Include(c => c.Tariffs).Where(c => c.PropertyId == propertyId).AsSplitQuery().ToListAsync(cancellationToken);
        var numbers = meters.ToDictionary(m => m.Id, m => m.Number);
        return new SupplyOverview(
            property.Id,
            property.Name,
            [.. contracts.OrderBy(c => c.CostType).ThenBy(c => c.Name, StringComparer.CurrentCulture).Select(c => new SupplyContractView(
                c.Id,
                c.Name,
                c.Supplier,
                CostTypes.Find(c.CostType)?.Name ?? c.CostType,
                c.CostType,
                c.MeterType,
                c.Basis,
                c.PriceUnit,
                c.HeatingKind,
                c.Deviation,
                c.UnitMeters,
                [.. c.MeterIds.Select(id => numbers.GetValueOrDefault(id, "?"))],
                [.. c.Tariffs.OrderByDescending(t => t.Validity.From).Select(t => new SupplyTariffView(t.Id, t.Validity, t.UnitPrice, t.BaseFeePerMonth, t.Note))],
                [.. c.MeterIds]))],
            [.. meters.Where(m => m.RemovedOn is null || contracts.Any(c => c.MeterIds.Contains(m.Id)))
                .OrderByDescending(m => m.UnitId is null && m.RoomId is null).ThenBy(m => m.Name, StringComparer.CurrentCulture)
                .Select(m => new MeterOption(m.Id, $"{m.Name} (Nr. {m.Number})", m.Type, m.UnitId is null && m.RoomId is null))]);
    }

    public Task<Result> CreateAsync(Guid propertyId, SupplyContractInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            if (!await db.Set<Property>().AnyAsync(o => o.Id == propertyId, cancellationToken))
            {
                throw new NotFoundException();
            }

            var chosen = await db.Set<Zaehler>().Where(m => m.PropertyId == propertyId && input.MeterIds.Contains(m.Id)).ToListAsync(cancellationToken);
            if (chosen.Count != input.MeterIds.Distinct().Count())
            {
                throw new RuleViolationException("Ein gewählter Zähler gehört nicht zum Objekt.");
            }

            if (chosen.Any(m => m.Type != input.MeterType))
            {
                throw new RuleViolationException("Alle gewählten Zähler müssen die gewählte Zählerart haben.");
            }

            var contract = new SupplyContract(propertyId, input.Name, input.Supplier, input.CostType, input.MeterType, input.Basis, input.PriceUnit, input.MeterIds, input.HeatingKind, input.Deviation);
            contract.UnitMeters = input.UnitMeters;
            Tariff(contract, input.Tariff);
            db.Add(contract);
            await db.SaveChangesAsync(cancellationToken);
            return contract.Id;
        });

    public Task<Result> UpdateAsync(Guid contractId, SupplyContractInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            var contract = await db.Set<SupplyContract>().SingleOrDefaultAsync(c => c.Id == contractId, cancellationToken) ?? throw new NotFoundException();
            var chosen = await db.Set<Zaehler>().Where(m => m.PropertyId == contract.PropertyId && input.MeterIds.Contains(m.Id)).ToListAsync(cancellationToken);
            if (chosen.Count != input.MeterIds.Distinct().Count())
            {
                throw new RuleViolationException("Ein gewählter Zähler gehört nicht zum Objekt.");
            }

            if (chosen.Any(m => m.Type != input.MeterType))
            {
                throw new RuleViolationException("Alle gewählten Zähler müssen die gewählte Zählerart haben.");
            }

            contract.Update(input.Name, input.Supplier, input.CostType, input.MeterType, input.Basis, input.PriceUnit, input.MeterIds, input.HeatingKind, input.Deviation);
            contract.UnitMeters = input.UnitMeters;
            await db.SaveChangesAsync(cancellationToken);
            return contract.Id;
        });

    public Task<Result> UpdateTariffAsync(Guid contractId, Guid tariffId, SupplyTariffInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            var contract = await db.Set<SupplyContract>().Include(c => c.Tariffs).SingleOrDefaultAsync(c => c.Id == contractId, cancellationToken) ?? throw new NotFoundException();
            contract.UpdateTariff(
                tariffId,
                input.From ?? throw new RuleViolationException("Bitte angeben, ab wann der Tarif gilt."),
                input.UnitPrice ?? 0m,
                input.BaseFeePerMonth ?? 0m,
                input.Note);
            await db.SaveChangesAsync(cancellationToken);
            return tariffId;
        });

    public Task<Result> AddTariffAsync(Guid contractId, SupplyTariffInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            var contract = await db.Set<SupplyContract>().Include(c => c.Tariffs).SingleOrDefaultAsync(c => c.Id == contractId, cancellationToken) ?? throw new NotFoundException();
            var tariff = Tariff(contract, input);
            await db.SaveChangesAsync(cancellationToken);
            return tariff.Id;
        });

    public Task<Result> RemoveTariffAsync(Guid contractId, Guid tariffId, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var contract = await db.Set<SupplyContract>().Include(c => c.Tariffs).SingleOrDefaultAsync(c => c.Id == contractId, cancellationToken) ?? throw new NotFoundException();
            if (contract.Tariffs.Count == 1)
            {
                throw new RuleViolationException("Der letzte Tarif lässt sich nicht entfernen. Bitte den Vertrag entfernen.");
            }

            var removed = contract.Tariffs.First(t => t.Id == tariffId);
            contract.RemoveTariff(tariffId);
            db.Remove(removed);
            await db.SaveChangesAsync(cancellationToken);
            return contract.Id;
        });

    public Task<Result> RemoveAsync(Guid contractId, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var contract = await db.Set<SupplyContract>().Include(c => c.Tariffs).SingleOrDefaultAsync(c => c.Id == contractId, cancellationToken) ?? throw new NotFoundException();
            db.RemoveRange(contract.Tariffs);
            db.Remove(contract);
            await db.SaveChangesAsync(cancellationToken);
            return contractId;
        });

    private static SupplyTariff Tariff(SupplyContract contract, SupplyTariffInput input) =>
        contract.AddTariff(
            input.From ?? throw new RuleViolationException("Bitte angeben, ab wann der Tarif gilt."),
            input.UnitPrice ?? 0m,
            input.BaseFeePerMonth ?? 0m,
            input.Note);
}
