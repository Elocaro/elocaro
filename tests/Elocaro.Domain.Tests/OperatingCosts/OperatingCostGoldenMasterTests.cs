using Elocaro.Domain.Leases;
using Elocaro.Domain.Metering;
using Elocaro.Domain.OperatingCosts;
using Elocaro.Domain.Receipts;
using static Elocaro.Domain.Tests.OperatingCosts.Scenario;

namespace Elocaro.Domain.Tests.OperatingCosts;

/// <summary>
/// Golden master: house with four units, billing year 2026 (365 days), calculated by hand (see comments).
/// <code>
/// Wohnung 1  50 m²   A (2 persons) all year
/// Wohnung 2  70 m²   B (3 persons) until 14.06. = 165 days, C (1 person) from 15.06. = 200 days
/// Wohnung 3  80 m²   D (2 persons) until 28.02. = 59 days, vacant 01.03.–30.04. = 61 days, E (4 persons) from 01.05. = 245 days
/// WG        100 m²   flat rate (excluded): room 1 = 25 m² (R1 all year), room 2 = 25 m² (vacant until 30.06. = 181 days,
///                    R2 from 01.07. = 184 days), room 3 = 50 m² (R3 all year); R1 1 person, R2 1 person, R3 2 persons
/// </code>
/// Leases are sorted by the start of the term (B, A, R3, D, R1, E, C, R2), vacancies come last: this order decides
/// who receives a remaining cent when fractional parts are equal.
/// </summary>
public class OperatingCostGoldenMasterTests
{
    private const int Apartment1 = 1;
    private const int Apartment2 = 2;
    private const int Apartment3 = 3;
    private const int SharedFlat = 4;
    private const int Room1 = 41;
    private const int Room2 = 42;
    private const int Room3 = 43;
    private const int A = 11;
    private const int B = 12;
    private const int C = 13;
    private const int TenantD = 14;
    private const int E = 15;
    private const int R1 = 21;
    private const int R2 = 22;
    private const int R3 = 23;

    private static BillingResult Run(IEnumerable<ICostGroupCalculator>? calculators = null) =>
        OperatingCostCalculator.Calculate(House(), calculators);

    private static BillingInput House()
    {
        var units = new[]
        {
            Unit(Apartment1, "Wohnung 1", 50m),
            Unit(Apartment2, "Wohnung 2", 70m),
            Unit(Apartment3, "Wohnung 3", 80m),
            Unit(SharedFlat, "WG", 100m, null, SharedFlatBilling.FlatRate, Room(Room1, "Zimmer 1", 25m), Room(Room2, "Zimmer 2", 25m), Room(Room3, "Zimmer 3", 50m)),
        };
        var leases = new[]
        {
            Lease(A, "A", Apartment1, D(2024, 4, 1), persons: 2, prepayments: [.. Months(2026, 1, 12, 150m), .. Months(2026, 1, 12, 50m, RentComponentType.HeatingCostPrepayment)]),
            Lease(B, "B", Apartment2, D(2024, 1, 1), D(2026, 6, 14), 3, prepayments: [.. Months(2026, 1, 5, 150m), Prepayment(2026, 6, 70m, 70m)]),
            Lease(C, "C", Apartment2, D(2026, 6, 15), persons: 1, prepayments: [Prepayment(2026, 6, 80m, 80m), .. Months(2026, 7, 11, 150m), Prepayment(2026, 12, 150m, 0m)]),
            Lease(TenantD, "D", Apartment3, D(2025, 3, 1), D(2026, 2, 28), 2, prepayments: Months(2026, 1, 2, 100m)),
            Lease(E, "E", Apartment3, D(2026, 5, 1), persons: 4, prepayments: Months(2026, 5, 12, 300m)),
            Lease(R1, "R1", SharedFlat, D(2025, 7, 1), persons: 1, room: Room1, statement: false, prepayments: Months(2026, 1, 12, 40m, RentComponentType.OperatingCostFlatRate)),
            Lease(R2, "R2", SharedFlat, D(2026, 7, 1), persons: 1, room: Room2, statement: false, prepayments: Months(2026, 7, 12, 40m, RentComponentType.OperatingCostFlatRate)),
            Lease(R3, "R3", SharedFlat, D(2025, 1, 1), persons: 2, room: Room3, statement: false, prepayments: Months(2026, 1, 12, 60m, RentComponentType.OperatingCostFlatRate)),
        };
        var costs = new[]
        {
            Cost(101, "betrkv-01", 3650m, description: "Grundsteuer 2026"),
            Cost(102, "betrkv-02", 2555m, description: "Wasser 2026"),
            Cost(103, "betrkv-08", 4044m, description: "Müllabfuhr 2026"),
            Cost(104, "betrkv-10", 730m, description: "Gartenpflege 2026"),

            // Insurance runs from July to June: 181 of 365 days of the first invoice and 184 of 365 days of the second fall into 2026.
            Cost(105, "betrkv-13", 1460m, Span(D(2025, 7, 1), D(2026, 6, 30)), description: "Versicherung 07/2025–06/2026"),
            Cost(106, "betrkv-13", 4015m, Span(D(2026, 7, 1), D(2027, 6, 30)), description: "Versicherung 07/2026–06/2027"),
            Cost(107, "betrkv-17", 240m, unit: Apartment2, description: "Rauchmelderwartung Wohnung 2"),
            Cost(108, "betrkv-17", 365m, unit: Apartment3, description: "Rauchmelderwartung Wohnung 3"),
            Cost(109, "verwaltung", 360m, allocable: false, description: "Hausverwaltung"),
            Cost(110, "betrkv-04", 5000m, description: "Gas 2026"),
        };
        var rules = new[]
        {
            Rule("betrkv-01", AllocationKey.LivingArea),
            Rule("betrkv-02", AllocationKey.Consumption, meter: MeterType.ColdWater),
            Rule("betrkv-08", AllocationKey.Persons),
            Rule("betrkv-10", AllocationKey.Units),

            // The key changes on 01.07.: area before, persons afterwards.
            Rule("betrkv-13", AllocationKey.LivingArea, to: D(2026, 6, 30)),
            Rule("betrkv-13", AllocationKey.Persons, from: D(2026, 7, 1)),
            Rule("betrkv-17", AllocationKey.Direct),
        };

        // Water: Wohnung 1 0.10 m³/day, Wohnung 2 0.20, Wohnung 3 0.15, WG 0.25 (linear, as with interpolated readings).
        return Input(units, leases, costs, rules, new RateConsumption((Apartment1, 0.10m), (Apartment2, 0.20m), (Apartment3, 0.15m), (SharedFlat, 0.25m)));
    }

    [Fact]
    public void Property_tax_by_living_area_per_day_with_tenant_change_vacancy_and_shared_flat_rooms()
    {
        var result = Run();

        // Σ area × days = (50 + 70 + 80 + 100) m² × 365 days = 109,500 m²·days; 3,650.00 € ÷ 109,500 = 1/30 € per m²·day.
        // A     50 × 365 = 18,250 → 608.3333    B  70 × 165 = 11,550 → 385.0000    C  70 × 200 = 14,000 → 466.6667
        // D     80 × 59  = 4,720  → 157.3333    E  80 × 245 = 19,600 → 653.3333    vacancy Wohnung 3: 80 × 61 = 4,880 → 162.6667
        // WG rooms by area 25 : 25 : 50 of 100 m²: R1 25 × 365 = 9,125 → 304.1667; Z2 vacant 25 × 181 = 4,525 → 150.8333;
        // R2 25 × 184 = 4,600 → 153.3333; R3 50 × 365 = 18,250 → 608.3333
        // Rounded down: 608.33 + 385.00 + 466.66 + 157.33 + 653.33 + 162.66 + 304.16 + 150.83 + 153.33 + 608.33 = 3,649.96 € → 4 cents missing.
        // Largest fractional parts: .6667 for C, vacancy Wohnung 3 and R1 (3 cents); then .3333 is a tie → first in order = A (1 cent).
        var type = result.CostTypes.Single(t => t.CostType == "betrkv-01");
        Assert.Equal(3650.00m, type.Total);
        Assert.Equal(608.34m, Line(result, A, "betrkv-01").Share);
        Assert.Equal(385.00m, Line(result, B, "betrkv-01").Share);
        Assert.Equal(466.67m, Line(result, C, "betrkv-01").Share);
        Assert.Equal(157.33m, Line(result, TenantD, "betrkv-01").Share);
        Assert.Equal(653.33m, Line(result, E, "betrkv-01").Share);
        Assert.Equal(2270.67m, type.TenantTotal);

        // Landlord: vacancy Wohnung 3 and the WG rooms (flat rate: 304.17 + 153.33 + 608.33) and the vacant room 2
        var landlord = type.Landlord.ToDictionary(p => p.Label, p => p.Amount);
        Assert.Equal(162.67m, landlord["Leerstand Wohnung 3"]);
        Assert.Equal(150.83m, landlord["Leerstand WG, Zimmer 2"]);
        Assert.Equal(304.17m, landlord["R1"]);
        Assert.Equal(153.33m, landlord["R2"]);
        Assert.Equal(608.33m, landlord["R3"]);
        Assert.Equal(1379.33m, type.LandlordTotal); // 3,650.00 − 2,270.67
    }

    [Fact]
    public void Water_by_consumption_splits_the_tenant_change_and_the_vacancy_by_the_consumed_quantity()
    {
        var result = Run();

        // Σ consumption: 0.10×365 + 0.20×365 + 0.15×365 + 0.25×365 = 36.5 + 73 + 54.75 + 91.25 = 255.5 m³; 2,555.00 € ÷ 255.5 = 10.00 €/m³.
        // A 36.5 m³ → 365.00    B 0.20×165 = 33 m³ → 330.00    C 0.20×200 = 40 m³ → 400.00
        // D 0.15×59 = 8.85 m³ → 88.50    vacancy 0.15×61 = 9.15 m³ → 91.50    E 0.15×245 = 36.75 m³ → 367.50
        // WG 0.25 m³/day: the consumption was caused by the occupants, a vacant room takes none of it. Until 30.06. (181 days) Zimmer 2 is vacant:
        // R1 and R3 share by room area 25:50 = 1/3 : 2/3, from 01.07. (184 days) all three rooms 25 %/25 %/50 %.
        // R1 0.25×181×1/3 = 15.0833 + 0.0625×184 = 11.5 → 26.5833 m³ → 265.83    R2 0.0625×184 = 11.5 m³ → 115.00
        // R3 0.25×181×2/3 = 30.1667 + 0.125×184 = 23 → 53.1667 m³ → 531.67      Σ WG = 91.25 m³ → 912.50; vacant Zimmer 2: nothing
        Assert.Equal(365.00m, Line(result, A, "betrkv-02").Share);
        Assert.Equal(330.00m, Line(result, B, "betrkv-02").Share);
        Assert.Equal(400.00m, Line(result, C, "betrkv-02").Share);
        Assert.Equal(88.50m, Line(result, TenantD, "betrkv-02").Share);
        Assert.Equal(367.50m, Line(result, E, "betrkv-02").Share);
        var landlord = result.CostTypes.Single(t => t.CostType == "betrkv-02").Landlord.ToDictionary(p => p.Label, p => p.Amount);
        Assert.Equal(91.50m, landlord["Leerstand Wohnung 3"]);
        Assert.DoesNotContain("Leerstand WG, Zimmer 2", landlord.Keys);
        Assert.Equal(265.83m, landlord["R1"]);
        Assert.Equal(531.67m, landlord["R3"]);
        Assert.Contains("Verbrauch (Kaltwasser)", result.CostTypes.Single(t => t.CostType == "betrkv-02").Key, StringComparison.Ordinal);
    }

    [Fact]
    public void Waste_by_person_days_counts_a_vacant_rental_object_with_one_person()
    {
        var result = Run();

        // Person days: A 2×365 = 730; B 3×165 = 495; C 1×200 = 200; D 2×59 = 118; vacancy Wohnung 3 1×61 = 61; E 4×245 = 980;
        // WG: R1 1×365 = 365, Z2 vacant 1×181 = 181, R2 1×184 = 184, R3 2×365 = 730 → Σ = 4,044 person days; 4,044.00 € ÷ 4,044 = 1.00 € per person day.
        Assert.Equal(730.00m, Line(result, A, "betrkv-08").Share);
        Assert.Equal(495.00m, Line(result, B, "betrkv-08").Share);
        Assert.Equal(200.00m, Line(result, C, "betrkv-08").Share);
        Assert.Equal(118.00m, Line(result, TenantD, "betrkv-08").Share);
        Assert.Equal(980.00m, Line(result, E, "betrkv-08").Share);
        var type = result.CostTypes.Single(t => t.CostType == "betrkv-08");
        Assert.Equal(61.00m, type.Landlord.Single(p => p.Label == "Leerstand Wohnung 3").Amount);
        Assert.Equal(181.00m, type.Landlord.Single(p => p.Label == "Leerstand WG, Zimmer 2").Amount);
        Assert.Equal("2 von 11,08 Personen (Ø 365 Tage) = 18,05 %", Line(result, A, "betrkv-08").Basis);
    }

    [Fact]
    public void Gardening_by_units_gives_every_unit_the_same_share_per_day()
    {
        var result = Run();

        // 4 units × 365 days = 1,460 unit days; 730.00 € ÷ 1,460 = 0.50 € per unit day.
        // A 365 → 182.50; B 165 → 82.50; C 200 → 100.00; D 59 → 29.50; vacancy 61 → 30.50; E 245 → 122.50.
        // WG 182.50 € by room area 25/25/50 %: R1 0.125×365 = 45.625; Z2 vacant 0.125×181 = 22.625; R2 0.125×184 = 23.00; R3 0.25×365 = 91.25.
        // Rounded down 729.99 € → 1 cent to R1 (equal fractional parts .5, lease before vacancy): 45.63 and 22.62.
        Assert.Equal(182.50m, Line(result, A, "betrkv-10").Share);
        Assert.Equal(82.50m, Line(result, B, "betrkv-10").Share);
        Assert.Equal(100.00m, Line(result, C, "betrkv-10").Share);
        Assert.Equal(29.50m, Line(result, TenantD, "betrkv-10").Share);
        Assert.Equal(122.50m, Line(result, E, "betrkv-10").Share);
        var landlord = result.CostTypes.Single(t => t.CostType == "betrkv-10").Landlord.ToDictionary(p => p.Label, p => p.Amount);
        Assert.Equal(30.50m, landlord["Leerstand Wohnung 3"]);
        Assert.Equal(45.63m, landlord["R1"]);
        Assert.Equal(22.62m, landlord["Leerstand WG, Zimmer 2"]);
    }

    [Fact]
    public void Insurance_across_the_year_is_cut_by_service_period_and_the_key_changes_on_July_1()
    {
        var result = Run();

        // Invoice 1 (01.07.2025–30.06.2026, 1,460.00 €): 181 of 365 days in 2026 → 1,460 × 181 ÷ 365 = 724.00 €.
        // Invoice 2 (01.07.2026–30.06.2027, 4,015.00 €): 184 of 365 days in 2026 → 4,015 × 184 ÷ 365 = 2,024.00 €. Total 2,748.00 €.
        var type = result.CostTypes.Single(t => t.CostType == "betrkv-13");
        Assert.Equal(2748.00m, type.Total);
        Assert.Equal(724.00m, type.Items[0].Prorated);
        Assert.Equal(2024.00m, type.Items[1].Prorated);
        Assert.Equal("Wohnfläche (01.01.2026 – 30.06.2026), Personen (Personentage) (01.07.2026 – 31.12.2026)", type.Key);

        // Part 1 by area, 01.01.–30.06. (181 days): Σ = 300 m² × 181 = 54,300 m²·days; 724.00 € ÷ 54,300 = 1/75 € per m²·day.
        // A 50×181 = 9,050 → 120.6667; B 70×165 = 11,550 → 154.00; C 70×16 = 1,120 → 14.9333; D 80×59 = 4,720 → 62.9333;
        // E 80×61 = 4,880 → 65.0667; vacancy Wohnung 3 80×61 = 4,880 → 65.0667; R1 25×181 → 60.3333; Z2 vacant 25×181 → 60.3333; R3 50×181 → 120.6667.
        // Rounded down 723.96 € → 4 cents for the fractional parts .6667: A, E, vacancy Wohnung 3, R3.
        // Part 2 by persons, 01.07.–31.12. (184 days): A 2×184 = 368; C 1×184 = 184; E 4×184 = 736; R1 184; R2 184; R3 2×184 = 368 → Σ = 2,024; 1.00 € each.
        Assert.Equal(120.67m + 368m, Line(result, A, "betrkv-13").Share);
        Assert.Equal(154.00m, Line(result, B, "betrkv-13").Share);
        Assert.Equal(14.93m + 184m, Line(result, C, "betrkv-13").Share);
        Assert.Equal(62.93m, Line(result, TenantD, "betrkv-13").Share);
        Assert.Equal(65.07m + 736m, Line(result, E, "betrkv-13").Share);
        Assert.Equal(2, Line(result, A, "betrkv-13").Protocol.Count(p => p.Title.StartsWith("Anteil ", StringComparison.Ordinal)));
    }

    [Fact]
    public void Direct_costs_go_to_the_unit_and_are_split_by_days_between_the_tenants()
    {
        var result = Run();

        // Wohnung 2: 240.00 € by days: B 165, C 200 of 365 → 108.4932 and 131.5068; rounded down 239.99 € → 1 cent to the larger fractional part (C).
        Assert.Equal(108.49m, Line(result, B, "betrkv-17").Share);
        Assert.Equal(131.51m, Line(result, C, "betrkv-17").Share);

        // Wohnung 3: 365.00 € = 1.00 € per day: D 59, vacancy 61, E 245.
        Assert.Equal(59.00m, Line(result, TenantD, "betrkv-17").Share);
        Assert.Equal(245.00m, Line(result, E, "betrkv-17").Share);
        Assert.Equal(61.00m, result.CostTypes.Single(t => t.CostType == "betrkv-17").Landlord.Single().Amount);
        Assert.DoesNotContain(Statement(result, A).Lines, l => l.CostType == "betrkv-17");
    }

    [Fact]
    public void Tenant_totals_prepayments_and_balances_use_what_was_actually_paid()
    {
        var result = Run();

        // Σ shares: A 608.34 + 365.00 + 730.00 + 182.50 + 488.67 = 2,374.51; B 385.00 + 330.00 + 495.00 + 82.50 + 154.00 + 108.49 = 1,554.99;
        // C 466.67 + 400.00 + 200.00 + 100.00 + 198.93 + 131.51 = 1,497.11; D 157.33 + 88.50 + 118.00 + 29.50 + 62.93 + 59.00 = 515.26;
        // E 653.33 + 367.50 + 980.00 + 122.50 + 801.07 + 245.00 = 3,169.40
        Assert.Equal(2374.51m, Statement(result, A).Share);
        Assert.Equal(1554.99m, Statement(result, B).Share);
        Assert.Equal(1497.11m, Statement(result, C).Share);
        Assert.Equal(515.26m, Statement(result, TenantD).Share);
        Assert.Equal(3169.40m, Statement(result, E).Share);

        // Prepayments: A 12 × 150 = 1,800; B 5 × 150 + 70 (June 14/30 of 150) = 820; C 80 (June 16/30) + 5 × 150 (July–November) + 0 (December unpaid) = 830;
        // D 2 × 100 = 200; E 8 × 300 = 2,400
        Assert.Equal([1800m, 820m, 830m, 200m, 2400m], new[] { A, B, C, TenantD, E }.Select(l => Statement(result, l).Operating!.PrepaymentsPaid));
        Assert.Equal(574.51m, Statement(result, A).Balance); // 2,374.51 − 1,800.00 (heating prepayments are not offset: heating costs follow separately)
        Assert.Equal(734.99m, Statement(result, B).Balance); // 1,554.99 − 820.00
        Assert.Equal(667.11m, Statement(result, C).Balance); // 1,497.11 − 830.00
        Assert.Equal(315.26m, Statement(result, TenantD).Balance); // 515.26 − 200.00
        Assert.Equal(769.40m, Statement(result, E).Balance); // 3,169.40 − 2,400.00

        // December prepayment of C is not paid: only the payments count, with a hint.
        Assert.Equal(980m, Statement(result, C).Operating!.PrepaymentsDue);
        Assert.Contains(result.Problems, p => p.Severity == ProblemSeverity.Warning && p.Text.Contains("C: Von den Vorauszahlungen", StringComparison.Ordinal) && p.Text.Contains("150,00 €", StringComparison.Ordinal));
        Assert.Equal(600m, Statement(result, A).Groups.Single(g => g.Group == CostGroup.Heating).PrepaymentsPaid);
        Assert.False(Statement(result, A).Groups.Single(g => g.Group == CostGroup.Heating).Calculated);
    }

    [Fact]
    public void Shared_flat_rooms_get_no_statement_but_are_compared_with_the_flat_rate()
    {
        var result = Run();

        // R1 304.17 + 265.83 + 365.00 + 45.63 + 60.33 + 184.00 = 1,224.96; R2 153.33 + 115.00 + 184.00 + 23.00 + 184.00 = 659.33;
        // R3 608.33 + 531.67 + 730.00 + 91.25 + 120.67 + 368.00 = 2,449.92
        Assert.Equal([1224.96m, 659.33m, 2449.92m], new[] { R1, R2, R3 }.Select(l => result.FlatRates.Single(f => f.LeaseId == Id(l)).ActualCosts));
        Assert.DoesNotContain(result.Tenants, t => t.LeaseId == Id(R1));

        // Flat rate paid: R1 12 × 40 = 480; R2 6 × 40 = 240; R3 12 × 60 = 720
        Assert.Equal([480m, 240m, 720m], new[] { R1, R2, R3 }.Select(l => result.FlatRates.Single(f => f.LeaseId == Id(l)).Paid));
        Assert.Equal(744.96m, result.FlatRates.Single(f => f.LeaseId == Id(R1)).Difference); // 1,224.96 − 480.00
        Assert.Equal(1729.92m, result.FlatRates.Single(f => f.LeaseId == Id(R3)).Difference); // 2,449.92 − 720.00
    }

    [Fact]
    public void Tenant_shares_plus_landlord_shares_are_exactly_the_total_costs_of_every_cost_type()
    {
        var result = Run();

        Assert.All(result.CostTypes, t => Assert.Equal(t.Total, t.TenantTotal + t.LandlordTotal));
        Assert.All(result.CostTypes, t => Assert.Equal(t.Total, result.Tenants.Sum(s => s.Lines.Where(l => l.CostType == t.CostType).Sum(l => l.Share)) + t.Landlord.Sum(p => p.Amount)));

        // 3,650.00 + 2,555.00 + 4,044.00 + 730.00 + 2,748.00 + 605.00 = 14,332.00 €
        Assert.Equal(14332.00m, result.TotalCosts);
        // Tenants 9,111.27 € (A–E); landlord: flat-rate rooms 4,334.21 € + vacancy Wohnung 3 471.74 € + vacancy WG room 2 414.78 € = 5,220.73 €
        // (the vacant room takes no water consumption: 113.12 € less vacancy, the same more for the occupied rooms)
        Assert.Equal(9111.27m, result.TenantTotal);
        Assert.Equal(5220.73m, result.LandlordTotal);
        Assert.Equal(471.74m, result.Landlord.Where(p => p.Reason == LandlordReason.Vacancy && p.Label == "Leerstand Wohnung 3").Sum(p => p.Amount));
        Assert.Equal(414.78m, result.Landlord.Where(p => p.Reason == LandlordReason.Vacancy && p.Label == "Leerstand WG, Zimmer 2").Sum(p => p.Amount));
        Assert.Equal(4334.21m, result.Landlord.Where(p => p.Reason == LandlordReason.SharedFlatFlatRate).Sum(p => p.Amount));
    }

    [Fact]
    public void Non_allocable_costs_are_excluded_and_heating_costs_wait_for_their_own_core()
    {
        var result = Run();

        var excluded = Assert.Single(result.NonAllocable);
        Assert.Equal(360m, excluded.Amount);
        Assert.DoesNotContain(result.CostTypes, t => t.CostType == "verwaltung");

        var heating = Assert.Single(result.UncalculatedGroups);
        Assert.Equal((CostGroup.Heating, 5000m), (heating.Group, heating.Total));
        Assert.DoesNotContain(result.CostTypes, t => t.CostType == "betrkv-04");
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Result_is_deterministic_and_reproducible()
    {
        var first = Run();
        var second = Run();

        Assert.Equal(
            first.Tenants.SelectMany(t => t.Lines.Select(l => (t.LeaseId, l.CostType, l.Share, l.Exact))),
            second.Tenants.SelectMany(t => t.Lines.Select(l => (t.LeaseId, l.CostType, l.Share, l.Exact))));
        Assert.Equal(first.Landlord, second.Landlord);
        Assert.Equal(
            first.Tenants.SelectMany(t => t.Lines.SelectMany(l => l.Protocol)),
            second.Tenants.SelectMany(t => t.Lines.SelectMany(l => l.Protocol)));
    }

    [Fact]
    public void Protocol_shows_formula_values_and_result_for_each_step()
    {
        var line = Line(Run(), A, "betrkv-01");

        var costs = line.Protocol[0];
        Assert.Equal("Gesamtkosten Laufende öffentliche Lasten (Grundsteuer)", costs.Title);
        Assert.Equal(3650m, costs.Result);
        Assert.Contains("Grundsteuer 2026: 3.650,00 € (vollständig im Zeitraum)", costs.Values, StringComparison.Ordinal);

        var share = line.Protocol[1];
        Assert.Equal("Kosten × Σ(Wohnfläche × Tage) des Mieters ÷ Σ(Wohnfläche × Tage) aller Einheiten", share.Formula);
        Assert.Equal("3.650,00 € × 18.250 ÷ 109.500 m²·Tage", share.Values);
        Assert.Equal(608.3333m, share.Result);
        Assert.Contains("01.01.2026 – 31.12.2026: 50 m² × 365 Tage = 18.250 m²·Tage", share.Note, StringComparison.Ordinal);

        var rounding = line.Protocol[2];
        Assert.Equal("608,33 € → 608,34 €", rounding.Values);
        Assert.Equal(608.34m, rounding.Result);
        Assert.Contains("Restcent", rounding.Note, StringComparison.Ordinal);

        // The tenant change is traceable: B pays 165 days, C 200 days.
        Assert.Contains("01.01.2026 – 14.06.2026: 70 m² × 165 Tage = 11.550 m²·Tage", Line(Run(), B, "betrkv-01").Protocol[1].Note, StringComparison.Ordinal);
        Assert.Equal(165, Statement(Run(), B).Days);
        Assert.Equal(200, Statement(Run(), C).Days);
    }
}
