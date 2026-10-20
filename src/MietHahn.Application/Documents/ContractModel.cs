namespace MietHahn.Application.Documents;

/// <summary>Person on a contract: tenant or guarantor.</summary>
public sealed record ContractParty(string Name, DateOnly? BirthDate, string? Address);

/// <summary>Agreed step of a graduated rent.</summary>
public sealed record ContractStep(DateOnly From, decimal BaseRent);

/// <summary>Further monthly amount, e.g. parking space.</summary>
public sealed record ContractComponent(string Name, decimal Amount);

/// <summary>Cost type with its number under § 2 BetrKV (<c>null</c> if none).</summary>
public sealed record ContractCostType(int? Number, string Name);

/// <summary>
/// Data of a lease for the contract templates (apartment contract and shared-flat room contract). The property names are the
/// placeholders in snake_case. Areas, rooms and rent come from the master data as of the start of the lease.
/// </summary>
public sealed record ContractModel(
    DateOnly Today,
    string SenderName,
    string SenderAddress,
    IReadOnlyList<ContractParty> Tenants,
    IReadOnlyList<ContractParty> Guarantors,
    string TenantNames,
    string PropertyName,
    string PropertyStreet,
    string PropertyPostalCode,
    string PropertyCity,
    int? YearBuilt,
    string HeatingType,
    string UnitName,
    string? UnitLocation,
    decimal? RoomCount,
    decimal? LivingArea,
    string? Bathroom,
    bool Balcony,
    bool FittedKitchen,
    string? Amenities,
    string? EnergyStandard,
    bool IsSharedFlatRoom,
    string? RoomName,
    decimal? RoomArea,
    decimal? CommonAreaShare,
    string? Furnishing,
    IReadOnlyList<string> FurnishingList,
    IReadOnlyList<string> OtherRooms,
    DateOnly Start,
    DateOnly? FixedTermUntil,
    bool IsFixedTerm,
    int NoticePeriodMonths,
    bool IsPrepayment,
    bool IsFlatRate,
    bool IsInclusive,
    bool HeatingConsumptionBased,
    bool IsGraduated,
    bool IsIndex,
    IReadOnlyList<ContractStep> RentSteps,
    DateOnly? IndexBaseMonth,
    decimal? IndexBaseValue,
    decimal BaseRent,
    decimal OperatingCostPrepayment,
    decimal HeatingCostPrepayment,
    decimal OperatingCostFlatRate,
    decimal ParkingSpace,
    decimal FurnishingSurcharge,
    IReadOnlyList<ContractComponent> OtherComponents,
    decimal TotalRent,
    int DueOnWorkingDay,
    bool DirectDebit,
    decimal? Deposit,
    string? DepositNote,
    decimal MaxDeposit,
    IReadOnlyList<ContractCostType> CostTypes,
    IReadOnlyList<ContractCostType> ExcludedCostTypes,
    int? PersonCount,
    string? Notes,
    string? SenderIban = null,
    string? SenderBank = null);

/// <summary>Comparable flat for the justification of a rent increase.</summary>
public sealed record ComparableFlat(string Address, decimal Area, decimal Rent, decimal PerSquareMetre);

/// <summary>
/// Data of a letter about a rent increase. The amounts are base rent (comparative, index, graduated) or flat rate. Fields that do not
/// belong to the kind of increase are empty.
/// </summary>
public sealed record RentIncreaseModel(
    DateOnly Today,
    string Salutation,
    string TenantNames,
    string RentalUnit,
    string PropertyName,
    string PropertyStreet,
    string PropertyPostalCode,
    string PropertyCity,
    string SenderName,
    string SenderAddress,
    string? SenderIban,
    string? SenderBank,
    bool IsComparative,
    bool IsIndex,
    bool IsGraduated,
    bool IsFlatRate,
    decimal CurrentAmount,
    decimal NewAmount,
    decimal IncreaseAmount,
    decimal IncreasePercent,
    DateOnly EffectiveFrom,
    DateOnly? ConsentDeadline,
    DateOnly? LastIncrease,
    DateOnly LeaseStart,
    bool BasisRentIndex,
    bool BasisComparables,
    bool BasisAppraisal,
    string? RentIndexName,
    decimal? PerSquareMetreFrom,
    decimal? PerSquareMetreTo,
    decimal? PerSquareMetre,
    decimal? LivingArea,
    string? FeatureText,
    IReadOnlyList<ComparableFlat> Comparables,
    string? AppraiserName,
    DateOnly? AppraisalDate,
    decimal? ComparativeRent,
    int CapPercent,
    decimal CapLimit,
    decimal RentThreeYearsAgo,
    decimal? OldIndex,
    decimal? NewIndex,
    DateOnly? OldIndexMonth,
    DateOnly? NewIndexMonth,
    decimal? IndexChangePercent,
    IReadOnlyList<ContractStep> NextSteps,
    string? CostReason);
