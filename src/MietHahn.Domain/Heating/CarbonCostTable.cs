using System.Globalization;

namespace MietHahn.Domain.Heating;

/// <summary>One level of the CO₂ cost split (CO2KostAufG, annex to § 1 para. 1).</summary>
/// <param name="From">Inclusive lower bound in kg CO₂ per m² living area and year.</param>
/// <param name="To">Exclusive upper bound, <c>null</c> = open.</param>
/// <param name="LandlordPercent">Share of the landlord in percent (0 to 95).</param>
public sealed record CarbonCostLevel(int Number, decimal From, decimal? To, decimal LandlordPercent)
{
    public decimal TenantPercent => 100m - LandlordPercent;

    public bool Contains(decimal specificKg) => specificKg >= From && (To is null || specificKg < To);

    public string Range => To is { } to
        ? string.Create(CultureInfo.GetCultureInfo("de-DE"), $"{(Number == 1 ? "unter" : $"{From:0.##} bis unter")} {to:0.##} kg/(m²·a)")
        : string.Create(CultureInfo.GetCultureInfo("de-DE"), $"{From:0.##} kg/(m²·a) und mehr");
}

/// <summary>Versioned table of the ten levels.</summary>
public sealed record CarbonCostTable(string Version, int ValidFromYear, string Status, string Source, IReadOnlyList<CarbonCostLevel> Levels)
{
    public bool Verified { get; init; }

    public CarbonCostLevel LevelFor(decimal specificKg) =>
        Levels.FirstOrDefault(l => l.Contains(specificKg)) ?? Levels[^1];
}

/// <summary>
/// Versioned level tables of the CO2KostAufG. <b>Stand/prüfen:</b> the table was entered from the statute (in force since 1.1.2023)
/// by hand and has not been verified against the Federal Law Gazette; a statement must not be sent before a check.
/// </summary>
public static class CarbonCostTables
{
    public static IReadOnlyList<CarbonCostTable> All { get; } =
    [
        new CarbonCostTable(
            "CO2KostAufG-2023",
            2023,
            "Stand/prüfen: Stand 2023, von Hand erfasst, nicht gegen das Bundesgesetzblatt geprüft",
            "Anlage zu § 1 Abs. 1 CO2KostAufG (Stufenmodell, 10 Stufen)",
            [
                new(1, 0m, 12m, 0m),
                new(2, 12m, 17m, 10m),
                new(3, 17m, 22m, 20m),
                new(4, 22m, 27m, 30m),
                new(5, 27m, 32m, 40m),
                new(6, 32m, 37m, 50m),
                new(7, 37m, 42m, 60m),
                new(8, 42m, 47m, 70m),
                new(9, 47m, 52m, 80m),
                new(10, 52m, null, 95m),
            ]),
    ];

    /// <summary>The table valid for the billing year; <c>null</c> before the law came into force.</summary>
    public static CarbonCostTable? For(int billingYear) =>
        All.Where(t => t.ValidFromYear <= billingYear).OrderByDescending(t => t.ValidFromYear).FirstOrDefault();
}

/// <summary>Result of the level determination.</summary>
public sealed record CarbonCostClassification(CarbonCostTable Table, CarbonCostLevel Level, decimal SpecificKg, decimal Co2Kg, decimal LivingArea, decimal YearFactor);

/// <summary>Determination of the level from the emissions of the building (pure).</summary>
public static class CarbonCostCalculator
{
    /// <param name="co2Kg">CO₂ emissions of the fuel supplied in the billing period (heating and hot water).</param>
    /// <param name="livingArea">Living area of the building in m².</param>
    /// <param name="billingDays">Days of the billing period; periods that are not a year long are converted to a year.</param>
    public static CarbonCostClassification Classify(CarbonCostTable table, decimal co2Kg, decimal livingArea, int billingDays)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (livingArea <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(livingArea), "Die Wohnfläche muss größer als 0 sein.");
        }

        var yearFactor = billingDays is 365 or 366 ? 1m : 365m / billingDays;
        var specific = co2Kg * yearFactor / livingArea;
        return new CarbonCostClassification(table, table.LevelFor(specific), specific, co2Kg, livingArea, yearFactor);
    }
}
