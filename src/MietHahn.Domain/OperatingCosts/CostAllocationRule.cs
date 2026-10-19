using MietHahn.Domain.Common;
using MietHahn.Domain.Metering;
using MietHahn.Domain.Receipts;

namespace MietHahn.Domain.OperatingCosts;

/// <summary>
/// Allocation key of a cost type for a property with a validity period. Keys are never overwritten (time series):
/// a change applies from an effective date, earlier statements stay reproducible.
/// </summary>
public sealed class CostAllocationRule : Entity, ITimeDependent
{
    private CostAllocationRule()
    {
        CostType = "";
    }

    public CostAllocationRule(
        Guid propertyId,
        string costType,
        Period validity,
        AllocationKey key,
        MeterType? meterType = null,
        decimal? preDeductionPercent = null,
        decimal? preDeductionAmount = null,
        string? preDeductionNote = null,
        IEnumerable<FixedShare>? shares = null)
    {
        var type = CostTypes.Find(costType) ?? throw new RuleViolationException("Unbekannte Kostenart.");
        if (!type.Allocable)
        {
            throw new RuleViolationException($"Die Kostenart „{type.Name}“ ist nicht umlagefähig und braucht keinen Umlageschlüssel.");
        }

        if (CostGroups.Of(costType) == CostGroup.Heating)
        {
            throw new RuleViolationException("Heiz- und Warmwasserkosten werden nach der Heizkostenverordnung abgerechnet und haben keinen Umlageschlüssel der Betriebskosten.");
        }

        if (key == AllocationKey.Consumption && meterType is null)
        {
            throw new RuleViolationException("Beim Schlüssel „Verbrauch“ bitte den Zählertyp wählen.");
        }

        if (preDeductionPercent is not null && preDeductionAmount is not null)
        {
            throw new RuleViolationException("Der Vorwegabzug ist entweder ein Prozentsatz oder ein Betrag.");
        }

        if (preDeductionPercent is < 0 or > 100 || decimal.Round(preDeductionPercent ?? 0, 2) != (preDeductionPercent ?? 0))
        {
            throw new RuleViolationException("Der Vorwegabzug in Prozent liegt zwischen 0 und 100 (höchstens zwei Nachkommastellen).");
        }

        if (preDeductionAmount is < 0 || decimal.Round(preDeductionAmount ?? 0, 2) != (preDeductionAmount ?? 0))
        {
            throw new RuleViolationException("Der Vorwegabzug als Betrag ist nicht negativ und hat höchstens zwei Nachkommastellen.");
        }

        var shareList = (shares ?? []).ToList();
        if (key == AllocationKey.FixedPercent)
        {
            if (shareList.Count == 0)
            {
                throw new RuleViolationException("Beim Schlüssel „fester Prozentsatz“ bitte die Prozentsätze der Einheiten angeben.");
            }

            if (shareList.Select(a => a.UnitId).Distinct().Count() != shareList.Count)
            {
                throw new RuleViolationException("Jede Einheit darf nur einen Prozentsatz haben.");
            }

            if (shareList.Any(a => a.Percent is <= 0 or > 100 || decimal.Round(a.Percent, 2) != a.Percent) || shareList.Sum(a => a.Percent) > 100)
            {
                throw new RuleViolationException("Die Prozentsätze liegen zwischen 0 und 100 und ergeben zusammen höchstens 100 %.");
            }
        }
        else
        {
            shareList.Clear();
        }

        PropertyId = propertyId;
        CostType = costType;
        Validity = validity;
        Key = key;
        MeterType = key == AllocationKey.Consumption ? meterType : null;
        PreDeductionPercent = preDeductionPercent == 0 ? null : preDeductionPercent;
        PreDeductionAmount = preDeductionAmount == 0 ? null : preDeductionAmount;
        PreDeductionNote = string.IsNullOrWhiteSpace(preDeductionNote) ? null : preDeductionNote.Trim();
        Shares = [.. shareList.Select(a => new CostAllocationShare(Id, a.UnitId, a.Percent))];
    }

    public Guid PropertyId { get; private set; }

    /// <summary>Key from <see cref="CostTypes"/>.</summary>
    public string CostType { get; private set; }

    public Period Validity { get; private set; }

    public AllocationKey Key { get; private set; }

    /// <summary>For <see cref="AllocationKey.Consumption"/>: the meters of this type are used.</summary>
    public MeterType? MeterType { get; private set; }

    /// <summary>Deduction before allocation in percent of the costs (e.g. commercial share); mutually exclusive with the amount.</summary>
    public decimal? PreDeductionPercent { get; private set; }

    /// <summary>Deduction before allocation as a fixed amount per year (prorated if the key changes within the period).</summary>
    public decimal? PreDeductionAmount { get; private set; }

    public string? PreDeductionNote { get; private set; }

    /// <summary>For <see cref="AllocationKey.FixedPercent"/>: percentage per unit.</summary>
    public List<CostAllocationShare> Shares { get; private set; } = [];

    public void EndValidity(DateOnly lastDay) => Validity = Validity.EndOn(lastDay);

    public BillingRule ToBillingRule() =>
        new(CostType, Validity, Key, MeterType, PreDeductionPercent, PreDeductionAmount, PreDeductionNote, [.. Shares.Select(a => new FixedShare(a.UnitId, a.Percent))]);

    /// <summary>
    /// Sets the key from the effective date on: the latest rule ends the day before; a rule starting on the same day is replaced.
    /// <paramref name="series"/> contains only the rules of one property and cost type.
    /// </summary>
    public static CostAllocationRule SetFrom(
        ICollection<CostAllocationRule> series,
        Guid propertyId,
        string costType,
        DateOnly from,
        AllocationKey key,
        MeterType? meterType = null,
        decimal? preDeductionPercent = null,
        decimal? preDeductionAmount = null,
        string? preDeductionNote = null,
        IEnumerable<FixedShare>? shares = null) =>
        TimeSeries.CarryForward(
            series,
            from,
            validity => new CostAllocationRule(propertyId, costType, validity, key, meterType, preDeductionPercent, preDeductionAmount, preDeductionNote, shares));

    /// <summary>Ends the rule valid on the day with this day (the cost type is then no longer active).</summary>
    public static void EndOn(IEnumerable<CostAllocationRule> series, DateOnly lastDay)
    {
        var current = TimeSeries.At(series, lastDay) ?? throw new RuleViolationException("Zu diesem Tag gibt es keinen Umlageschlüssel.");
        current.EndValidity(lastDay);
    }
}

/// <summary>Percentage of a unit for the key <see cref="AllocationKey.FixedPercent"/>.</summary>
public sealed class CostAllocationShare : Entity
{
    private CostAllocationShare()
    {
    }

    internal CostAllocationShare(Guid ruleId, Guid unitId, decimal percent)
    {
        RuleId = ruleId;
        UnitId = unitId;
        Percent = percent;
    }

    public Guid RuleId { get; private set; }

    public Guid UnitId { get; private set; }

    public decimal Percent { get; private set; }
}
