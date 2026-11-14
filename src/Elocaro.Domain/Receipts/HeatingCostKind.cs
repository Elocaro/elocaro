namespace Elocaro.Domain.Receipts;

/// <summary>What a cost item of the heating group is (for the aggregation of the total costs of the heating plant, § 7 para. 2 HeizkostenV).</summary>
public enum HeatingCostKind
{
    /// <summary>Fuel (gas, oil, pellets ...): its quantity and CO₂ emissions come from the receipt.</summary>
    Fuel = 0,

    /// <summary>Electricity for the operation of the plant (pumps, burner).</summary>
    OperatingPower = 1,

    /// <summary>Servicing and maintenance of the plant.</summary>
    Maintenance = 2,

    /// <summary>Emission measurement (flue gas measurement by the chimney sweep).</summary>
    EmissionMeasurement = 3,

    /// <summary>Cleaning of the plant and the boiler room.</summary>
    Cleaning = 4,

    /// <summary>Rental, calibration and reading of the metering devices.</summary>
    MeterService = 5,

    /// <summary>CO₂ costs according to the supplier's invoice (CO2KostAufG): part of the fuel costs, shared between landlord and tenant.</summary>
    CarbonPrice = 6,

    /// <summary>Other costs of the plant.</summary>
    Other = 9,
}
