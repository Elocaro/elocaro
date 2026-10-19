namespace MietHahn.Domain.OperatingCosts;

/// <summary>Allocation key (Umlageschlüssel): how the costs of a cost type are distributed over the units.</summary>
public enum AllocationKey
{
    /// <summary>Living area (WoFlV) times days.</summary>
    LivingArea = 0,

    /// <summary>Heated area times days.</summary>
    HeatedArea = 1,

    /// <summary>Persons times days (person days).</summary>
    Persons = 2,

    /// <summary>One share per unit and day.</summary>
    Units = 3,

    /// <summary>Consumption of the meters of a meter type assigned to the units.</summary>
    Consumption = 4,

    /// <summary>The cost items are assigned to one unit (<c>CostItem.UnitId</c>).</summary>
    Direct = 5,

    /// <summary>A fixed percentage per unit (the rest stays with the landlord).</summary>
    FixedPercent = 6,
}

/// <summary>How the rooms of a shared flat take part in the operating cost statement.</summary>
public enum SharedFlatBilling
{
    /// <summary>Flat rate: the rooms get no statement, actual costs are compared with the flat-rate income internally.</summary>
    FlatRate = 0,

    /// <summary>The costs of the flat are distributed over the rooms by area; each room tenant gets a statement.</summary>
    ByRoom = 1,
}

/// <summary>Groups of cost types that are calculated by different cores.</summary>
public enum CostGroup
{
    /// <summary>Operating costs under BetrKV that are distributed by an allocation key.</summary>
    Operating = 0,

    /// <summary>Heating and hot water (HeizkostenV); calculated by a separate core, see <see cref="ICostGroupCalculator"/>.</summary>
    Heating = 1,
}

/// <summary>Cost type to cost group assignment.</summary>
public static class CostGroups
{
    public static CostGroup Of(string costType) => costType is "betrkv-04" or "betrkv-05" or "betrkv-06" ? CostGroup.Heating : CostGroup.Operating;
}

/// <summary>Why a part of the costs stays with the landlord.</summary>
public enum LandlordReason
{
    /// <summary>The rental object was not rented on those days.</summary>
    Vacancy = 0,

    /// <summary>Deduction before the allocation (e.g. share for commercial use or garden).</summary>
    PreDeduction = 1,

    /// <summary>The cost type is not agreed in the lease.</summary>
    NotAgreed = 2,

    /// <summary>The lease has a flat rate or an all-inclusive rent.</summary>
    FlatRate = 3,

    /// <summary>The room belongs to a shared flat billed by flat rate.</summary>
    SharedFlatFlatRate = 4,

    /// <summary>A fixed-percent key that does not add up to 100 %.</summary>
    ResidualPercent = 5,

    /// <summary>No usable basis for the allocation (see the problems).</summary>
    Unassigned = 6,

    /// <summary>The landlord bears a share of the CO₂ costs (CO2KostAufG) that is deducted from the tenants' heating costs.</summary>
    CarbonCostShare = 7,
}

public enum ProblemSeverity
{
    /// <summary>The result is usable but an assumption was made.</summary>
    Warning = 0,

    /// <summary>Data is missing; the result must not be sent.</summary>
    Error = 1,
}
