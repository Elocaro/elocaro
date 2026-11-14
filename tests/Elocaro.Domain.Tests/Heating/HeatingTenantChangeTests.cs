using Elocaro.Domain.Heating;
using Elocaro.Domain.Metering;
using Elocaro.Domain.OperatingCosts;
using Elocaro.Domain.Receipts;
using static Elocaro.Domain.Tests.Heating.HeatingScenario;
using static Elocaro.Domain.Tests.OperatingCosts.Scenario;

namespace Elocaro.Domain.Tests.Heating;

/// <summary>
/// Tenant change in the middle of the billing year: interim reading preferred, otherwise degree days (heating) or days (hot water).
/// Wohnung 1 (60 m²): X until 31.03. (90 days), Y from 01.04. (275 days); Wohnung 2 (60 m²): Z all year (365 days).
/// Costs 1,000.00 € (heating only), 70 % by heat quantity, 30 % by heated area.
/// </summary>
public class HeatingTenantChangeTests
{
    private const int Apartment1 = 1;
    private const int Apartment2 = 2;
    private const int X = 11;
    private const int Y = 12;
    private const int Z = 13;
    private static readonly DateOnly ChangeDay = D(2026, 3, 31);

    private static readonly HeatingParameters Parameters = new() { JointPlant = false, ApplyCarbonCosts = false };

    private static BillingInput House(IConsumptionSource consumption) =>
        Input(
            [Unit(Apartment1, "Wohnung 1", 60m), Unit(Apartment2, "Wohnung 2", 60m)],
            [
                Lease(X, "X", Apartment1, D(2024, 1, 1), ChangeDay),
                Lease(Y, "Y", Apartment1, D(2026, 4, 1)),
                Lease(Z, "Z", Apartment2, D(2024, 1, 1)),
            ],
            [HeatCost(101, "betrkv-04", 1000m, HeatingCostKind.Other, description: "Heizkosten")],
            [],
            consumption);

    /// <summary>Wohnung 1 uses 12,000 kWh in 2026, Wohnung 2 uses 8,000 kWh. Without readings on the change day the parts are interpolated.</summary>
    private static RuleConsumption WithoutInterimReading() =>
        new((unit, type, period) =>
        {
            if (type != MeterType.Heat)
            {
                return new ConsumptionQuantity(null, false, "Kein Zähler zugeordnet.");
            }

            if (unit == Id(Apartment2))
            {
                return period == Year2026 ? new ConsumptionQuantity(8000m) : new ConsumptionQuantity(8000m * period.Days / 365m, true, null, [period.From.AddDays(-1), period.To!.Value]);
            }

            return period == Year2026
                ? new ConsumptionQuantity(12000m)
                : new ConsumptionQuantity(12000m * period.Days / 365m, true, "interpoliert", [ChangeDay]);
        });

    /// <summary>With an interim reading on 31.03.: X consumed 5,000 kWh, Y 7,000 kWh.</summary>
    private static RuleConsumption WithInterimReading() =>
        new((unit, type, period) =>
        {
            if (type != MeterType.Heat)
            {
                return new ConsumptionQuantity(null, false, "Kein Zähler zugeordnet.");
            }

            if (unit == Id(Apartment2))
            {
                return new ConsumptionQuantity(8000m);
            }

            return period.To == ChangeDay ? new ConsumptionQuantity(5000m) : period.From == ChangeDay.AddDays(1) ? new ConsumptionQuantity(7000m) : new ConsumptionQuantity(12000m);
        });

    [Fact]
    public void Tenant_change_without_interim_reading_splits_the_consumption_by_degree_days()
    {
        var result = Run(House(WithoutInterimReading()), Parameters);

        // Degree days (placeholder table 2026-stub, permille per month): January 170 + February 145 + March 120 = 435 ‰ for X (full months),
        // April to December 565 ‰ for Y. Wohnung 1 used 12,000 kWh: X 12,000 × 0.435 = 5,220 kWh, Y 12,000 × 0.565 = 6,780 kWh.
        // Heat quantities: X 5,220 + Y 6,780 + Z 8,000 = 20,000 kWh. Consumption part 70 % = 700.00 €:
        //   X 700 × 5,220/20,000 = 182.70    Y 700 × 6,780/20,000 = 237.30    Z 700 × 8,000/20,000 = 280.00
        // Base costs 30 % = 300.00 € by m² × days: X 60 × 90 = 5,400, Y 60 × 275 = 16,500, Z 60 × 365 = 21,900, Σ 43,800:
        //   X 300 × 5,400/43,800 = 36.9863    Y 300 × 16,500/43,800 = 113.0137    Z 300 × 21,900/43,800 = 150.00
        // Totals: X 219.6863   Y 350.3137   Z 430.00 → rounded down 219.68 + 350.31 + 430.00 = 999.99 → 1 cent to the largest remainder (X .63): 219.69
        Assert.Equal(219.69m, HeatingLine(result, X, HeatingCostCalculator.HeatKey).Share);
        Assert.Equal(350.31m, HeatingLine(result, Y, HeatingCostCalculator.HeatKey).Share);
        Assert.Equal(430.00m, HeatingLine(result, Z, HeatingCostCalculator.HeatKey).Share);
        Assert.False(result.HasErrors);
        Assert.Contains(result.Problems, p => p.Severity == ProblemSeverity.Warning && p.Text.Contains("Gradtagszahlen", StringComparison.Ordinal) && p.Text.Contains("Stand/prüfen", StringComparison.Ordinal));
        var note = HeatingLine(result, X, HeatingCostCalculator.HeatKey).Protocol.Single(e => e.Title.StartsWith("Verbrauchsanteil", StringComparison.Ordinal)).Note!;
        Assert.Contains("Gradtagszahlen 43,5 % von 12.000 kWh", note, StringComparison.Ordinal);
    }

    [Fact]
    public void Tenant_change_with_station_degree_days_uses_the_measured_months()
    {
        // Measured degree days 2026: January 600 + February 500 + March 400 = 1,500 of 3,200 = 46.875 % for X.
        var months = new[] { 600m, 500m, 400m, 300m, 100m, 0m, 0m, 0m, 100m, 200m, 400m, 600m };
        var table = new DegreeDayTable("DWD 01234", 2026, "Gradtage der Station Beispielstadt", "DWD", [.. Enumerable.Repeat(83, 12)])
        {
            Verified = true,
            Measured = months.Select((v, i) => (Month: D(2026, i + 1, 1), Value: v)).ToDictionary(m => m.Month, m => m.Value),
        };

        var result = Run(House(WithoutInterimReading()), Parameters with { DegreeDays = table });

        var note = HeatingLine(result, X, HeatingCostCalculator.HeatKey).Protocol.Single(e => e.Title.StartsWith("Verbrauchsanteil", StringComparison.Ordinal)).Note!;
        Assert.Contains("Gradtagszahlen 46,88 % von 12.000 kWh (Tabelle DWD 01234)", note, StringComparison.Ordinal);
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Tenant_change_without_any_degree_days_in_the_periods_falls_back_to_days()
    {
        var table = new DegreeDayTable("DWD 01234", 2026, "Gradtage", "DWD", [.. Enumerable.Repeat(83, 12)])
        {
            Measured = Enumerable.Range(1, 12).ToDictionary(m => D(2026, m, 1), _ => 0m),
        };

        var result = Run(House(WithoutInterimReading()), Parameters with { DegreeDays = table });

        // X 90 of 365 days = 24.66 %: the consumption is not lost.
        var note = HeatingLine(result, X, HeatingCostCalculator.HeatKey).Protocol.Single(e => e.Title.StartsWith("Verbrauchsanteil", StringComparison.Ordinal)).Note!;
        Assert.Contains("Gradtagszahlen 24,66 % von 12.000 kWh", note, StringComparison.Ordinal);
    }

    [Fact]
    public void Tenant_change_with_interim_reading_uses_the_measured_parts()
    {
        var result = Run(House(WithInterimReading()), Parameters);

        // X 5,000 + Y 7,000 + Z 8,000 = 20,000 kWh. Consumption part 700.00 €: X 175.00, Y 245.00, Z 280.00. Base costs as above:
        // X 175.00 + 36.9863 = 211.9863    Y 245.00 + 113.0137 = 358.0137    Z 430.00 → 211.98 + 358.01 + 430.00 = 999.99 → X .63 gets the cent: 211.99
        Assert.Equal(211.99m, HeatingLine(result, X, HeatingCostCalculator.HeatKey).Share);
        Assert.Equal(358.01m, HeatingLine(result, Y, HeatingCostCalculator.HeatKey).Share);
        Assert.Equal(430.00m, HeatingLine(result, Z, HeatingCostCalculator.HeatKey).Share);
        Assert.DoesNotContain(result.Problems, p => p.Text.Contains("Gradtagszahlen", StringComparison.Ordinal));
        var note = HeatingLine(result, X, HeatingCostCalculator.HeatKey).Protocol.Single(e => e.Title.StartsWith("Verbrauchsanteil", StringComparison.Ordinal)).Note!;
        Assert.Contains("Zwischenablesung", note, StringComparison.Ordinal);
    }

    [Fact]
    public void Degree_day_weight_of_a_period_inside_a_month_is_proportional_to_the_days()
    {
        var table = DegreeDayTables.For(2026);

        Assert.Equal(1000, table.PermillePerMonth.Sum());
        Assert.Equal(435m, table.Weight(D(2026, 1, 1), D(2026, 3, 31)));
        Assert.Equal(1000m, table.Weight(D(2026, 1, 1), D(2026, 12, 31)));

        // March 120 ‰ over 31 days: 15 days = 120 × 15/31 = 58.0645 ‰, the remaining 16 days = 61.9355 ‰
        Assert.Equal(58.0645m, decimal.Round(table.Weight(D(2026, 3, 1), D(2026, 3, 15)), 4));
        Assert.Equal(120m, table.Weight(D(2026, 3, 1), D(2026, 3, 15)) + table.Weight(D(2026, 3, 16), D(2026, 3, 31)));
        Assert.False(table.Verified);
        Assert.Contains("Stand/prüfen", table.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void Tenant_change_in_the_middle_of_a_month_weighs_the_days_of_the_month()
    {
        // X until 15.03. (74 days), Y from 16.03.: X = January + February + 15/31 of March = 170 + 145 + 58.0645 = 373.0645 ‰
        var input = Input(
            [Unit(Apartment1, "Wohnung 1", 60m)],
            [Lease(X, "X", Apartment1, D(2024, 1, 1), D(2026, 3, 15)), Lease(Y, "Y", Apartment1, D(2026, 3, 16))],
            [HeatCost(101, "betrkv-04", 1000m, HeatingCostKind.Other)],
            [],
            new RuleConsumption((_, _, period) => period == Year2026
                ? new ConsumptionQuantity(10000m)
                : new ConsumptionQuantity(10000m * period.Days / 365m, true, null, [D(2026, 3, 15)])));

        var result = Run(input, Parameters);

        // Heat quantity X = 10,000 × 0.3730645 = 3,730.645 kWh; Y = 6,269.355 kWh. 700.00 € ÷ 10,000 kWh = 0.07 €/kWh:
        // X 700 × 0.3730645 = 261.1452 + base 300 × 74/365 = 60.8219 → 321.9671;  Y 438.8548 + 239.1781 = 678.0329 → 321.97 and 678.03
        Assert.Equal(321.97m, HeatingLine(result, X, HeatingCostCalculator.HeatKey).Share);
        Assert.Equal(678.03m, HeatingLine(result, Y, HeatingCostCalculator.HeatKey).Share);
    }

    [Fact]
    public void Hot_water_without_interim_reading_is_split_by_days()
    {
        // Hot water only: 365 days, X 90 days (24.66 %), Y 275 days. Wohnung 1 used 30 m³: X 7.397 m³, Y 22.603 m³; Wohnung 2: 30 m³ (Z).
        var input = Input(
            [Unit(Apartment1, "Wohnung 1", 60m), Unit(Apartment2, "Wohnung 2", 60m)],
            [Lease(X, "X", Apartment1, D(2024, 1, 1), ChangeDay), Lease(Y, "Y", Apartment1, D(2026, 4, 1)), Lease(Z, "Z", Apartment2, D(2024, 1, 1))],
            [HeatCost(101, "betrkv-05", 1000m, HeatingCostKind.Other)],
            [],
            new RuleConsumption((unit, type, period) => type != MeterType.HotWater
                ? new ConsumptionQuantity(null, false, "Kein Zähler.")
                : period == Year2026 ? new ConsumptionQuantity(30m) : new ConsumptionQuantity(30m * period.Days / 365m, true, null, [ChangeDay])));

        var result = Run(input, Parameters);

        // Consumption part 700.00 € ÷ 60 m³ = 11.6667 €/m³: X 7.3973 m³ → 86.3014; Y 22.6027 m³ → 263.6986; Z 30 m³ → 350.00
        // Base part 300.00 € by m² × days: X 5,400 → 36.9863; Y 16,500 → 113.0137; Z 21,900 → 150.00
        // X 86.3014 + 36.9863 = 123.2877    Y 263.6986 + 113.0137 = 376.7123    Z 350.00 + 150.00 = 500.00
        // Rounded down 123.28 + 376.71 + 500.00 = 999.99 → the cent goes to the largest remainder (X .77): 123.29
        Assert.Equal(123.29m, HeatingLine(result, X, HeatingCostCalculator.HotWaterKey).Share);
        Assert.Equal(376.71m, HeatingLine(result, Y, HeatingCostCalculator.HotWaterKey).Share);
        Assert.Equal(500.00m, HeatingLine(result, Z, HeatingCostCalculator.HotWaterKey).Share);
        Assert.Contains(result.Problems, p => p.Text.Contains("nach Tagen", StringComparison.Ordinal));
    }
}
