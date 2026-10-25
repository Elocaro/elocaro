using MietHahn.Domain.Common;
using MietHahn.Domain.Heating;
using MietHahn.Domain.Metering;
using MietHahn.Domain.Receipts;

namespace MietHahn.Domain.OperatingCosts;

/// <summary>Which consumption of the chosen meters is billed by the supplier.</summary>
public enum SupplyBasis
{
    /// <summary>The whole consumption of the chosen meters (water supply, gas).</summary>
    MainMeter = 0,

    /// <summary>The chosen main meters minus all meters of the same type assigned to units or rooms (common-area electricity).</summary>
    CommonArea = 1,
}

/// <summary>Unit in which the supplier states the price.</summary>
public enum PriceUnit
{
    CubicMeter = 0,
    KilowattHour = 1,
}

/// <summary>
/// Supply contract of a property (water, sewage, electricity, gas): the cost of a billing period is calculated from the
/// consumption of the chosen meters and the tariffs, not taken from invoices (they arrive months late and advance invoices are not costs).
/// A <see cref="Receipt.SupplierStatement"/> replaces the calculation for the period it covers.
/// </summary>
public sealed class SupplyContract : Entity
{
    private SupplyContract()
    {
        Name = "";
        CostType = "";
    }

    public SupplyContract(
        Guid propertyId,
        string name,
        string? supplier,
        string costType,
        MeterType meterType,
        SupplyBasis basis,
        PriceUnit priceUnit,
        IEnumerable<Guid> meterIds,
        HeatingCostKind? heatingKind = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new RuleViolationException("Bitte eine Bezeichnung angeben, z. B. „Wasser Stadtwerke“.");
        }

        var type = CostTypes.Find(costType) ?? throw new RuleViolationException("Unbekannte Kostenart.");
        if (!type.Allocable)
        {
            throw new RuleViolationException($"Die Kostenart „{type.Name}“ ist nicht umlagefähig.");
        }

        var meters = meterIds.Distinct().ToList();
        if (meters.Count == 0)
        {
            throw new RuleViolationException("Bitte mindestens einen Zähler wählen, aus dessen Verbrauch die Kosten berechnet werden.");
        }

        if (priceUnit == PriceUnit.KilowattHour && meterType is not (MeterType.Gas or MeterType.Electricity or MeterType.Heat))
        {
            throw new RuleViolationException("Ein Preis je kWh passt nur zu Gas, Strom oder Wärme.");
        }

        if (CostGroups.Of(costType) == CostGroup.Heating)
        {
            if (heatingKind is null)
            {
                throw new RuleViolationException("Bei Heizkosten bitte die Art der Heizkosten angeben (z. B. Brennstoff).");
            }
        }
        else
        {
            heatingKind = null;
        }

        PropertyId = propertyId;
        Name = name.Trim();
        Supplier = string.IsNullOrWhiteSpace(supplier) ? null : supplier.Trim();
        CostType = costType;
        MeterType = meterType;
        Basis = basis;
        PriceUnit = priceUnit;
        MeterIds = meters;
        HeatingKind = heatingKind;
    }

    public Guid PropertyId { get; private set; }

    public string Name { get; private set; }

    public string? Supplier { get; private set; }

    /// <summary>Key from <see cref="CostTypes"/> under which the calculated cost enters the statement.</summary>
    public string CostType { get; private set; }

    public MeterType MeterType { get; private set; }

    public SupplyBasis Basis { get; private set; }

    public PriceUnit PriceUnit { get; private set; }

    /// <summary>For heating costs: the kind (fuel, operating power ...) of the calculated cost.</summary>
    public HeatingCostKind? HeatingKind { get; private set; }

    /// <summary>Main meters (any meter of a metering point; replacements are followed).</summary>
    public List<Guid> MeterIds { get; private set; } = [];

    public List<SupplyTariff> Tariffs { get; private set; } = [];

    /// <summary>Adds a tariff from the day; the tariff valid until then ends the day before.</summary>
    public SupplyTariff AddTariff(DateOnly from, decimal unitPrice, decimal baseFeePerMonth, string? note = null)
    {
        if (Tariffs.Any(t => t.Validity.From >= from))
        {
            throw new RuleViolationException("Es gibt schon einen Tarif ab diesem Tag oder später. Bitte einen späteren Stichtag wählen oder den späteren Tarif entfernen.");
        }

        var tariff = new SupplyTariff(Id, from, unitPrice, baseFeePerMonth, note);
        foreach (var open in Tariffs.Where(t => t.Validity.To is null || t.Validity.To >= from))
        {
            open.EndOn(from.AddDays(-1));
        }

        Tariffs.Add(tariff);
        return tariff;
    }

    /// <summary>Removes a tariff; the previous one is open-ended again if this was the last.</summary>
    public void RemoveTariff(Guid tariffId)
    {
        var tariff = Tariffs.FirstOrDefault(t => t.Id == tariffId) ?? throw new RuleViolationException("Der Tarif wurde nicht gefunden.");
        var last = tariff.Validity.To is null;
        Tariffs.Remove(tariff);
        if (last && Tariffs.OrderBy(t => t.Validity.From).LastOrDefault() is { } previous)
        {
            previous.Reopen();
        }
    }

    public SupplyContractModel ToModel() =>
        new(Id, Name, CostType, MeterType, Basis, PriceUnit, HeatingKind, [.. Tariffs.OrderBy(t => t.Validity.From).Select(t => new SupplyTariffModel(t.Id, t.Validity, t.UnitPrice, t.BaseFeePerMonth))]);
}

/// <summary>Price of a supply contract in a validity period; gross prices (a private landlord cannot deduct input tax).</summary>
public sealed class SupplyTariff : Entity
{
    private SupplyTariff()
    {
    }

    /// <summary>Plausibility limits against decimal separator slips (0.0905 typed in German notation becomes 905).</summary>
    public const decimal MaximumUnitPrice = 100m;

    public const decimal MaximumBaseFee = 1000m;

    internal SupplyTariff(Guid contractId, DateOnly from, decimal unitPrice, decimal baseFeePerMonth, string? note)
    {
        if (unitPrice < 0 || baseFeePerMonth < 0 || (unitPrice == 0 && baseFeePerMonth == 0))
        {
            throw new RuleViolationException("Bitte einen Preis und/oder einen Grundpreis angeben (nicht negativ).");
        }

        if (unitPrice > MaximumUnitPrice || baseFeePerMonth > MaximumBaseFee)
        {
            throw new RuleViolationException($"Der Preis wirkt unplausibel (höchstens {MaximumUnitPrice:0} € je Einheit und {MaximumBaseFee:0} € Grundpreis je Monat). Bitte das Komma als Dezimalzeichen eingeben, z. B. 0,0905 (nicht 0.0905).");
        }

        if (decimal.Round(unitPrice, 6) != unitPrice || decimal.Round(baseFeePerMonth, 4) != baseFeePerMonth)
        {
            throw new RuleViolationException("Der Preis hat höchstens sechs, der Grundpreis höchstens vier Nachkommastellen.");
        }

        ContractId = contractId;
        Validity = new Period(from);
        UnitPrice = unitPrice;
        BaseFeePerMonth = baseFeePerMonth;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    public Guid ContractId { get; private set; }

    public Period Validity { get; private set; }

    /// <summary>Gross price per <see cref="SupplyContract.PriceUnit"/>.</summary>
    public decimal UnitPrice { get; private set; }

    /// <summary>Gross base fee per month.</summary>
    public decimal BaseFeePerMonth { get; private set; }

    public string? Note { get; private set; }

    internal void EndOn(DateOnly lastDay) => Validity = new Period(Validity.From, lastDay);

    internal void Reopen() => Validity = new Period(Validity.From);
}

public sealed record SupplyTariffModel(Guid Id, Period Validity, decimal UnitPrice, decimal BaseFeePerMonth);

/// <summary>Input of the pure calculation (no entity).</summary>
public sealed record SupplyContractModel(
    Guid Id,
    string Name,
    string CostType,
    MeterType MeterType,
    SupplyBasis Basis,
    PriceUnit PriceUnit,
    HeatingCostKind? HeatingKind,
    IReadOnlyList<SupplyTariffModel> Tariffs);
