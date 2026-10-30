namespace Elocaro.Domain.Receipts;

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

    /// <summary>
    /// Cost types a supplier invoices by advance payments and an annual statement (water, sewage, heating and hot water, common-area
    /// electricity): only for these a receipt can be marked as the annual statement of the supplier.
    /// </summary>
    public static bool HasSupplierStatement(string costType) => costType is "betrkv-02" or "betrkv-03" or "betrkv-04" or "betrkv-05" or "betrkv-06" or "betrkv-11";
}
