namespace MietHahn.Domain.Receipts;

/// <summary>Groups of cost types that are calculated by different cores.</summary>
public enum CostGroup
{
    /// <summary>Operating costs under BetrKV that are distributed by an allocation key.</summary>
    Operating = 0,

    /// <summary>Heating and hot water (HeizkostenV); calculated by a separate core, see <see cref="OperatingCosts.ICostGroupCalculator"/>.</summary>
    Heating = 1,
}

/// <summary>Cost type to cost group assignment.</summary>
public static class CostGroups
{
    public static CostGroup Of(string costType) => costType is "betrkv-04" or "betrkv-05" or "betrkv-06" ? CostGroup.Heating : CostGroup.Operating;
}
