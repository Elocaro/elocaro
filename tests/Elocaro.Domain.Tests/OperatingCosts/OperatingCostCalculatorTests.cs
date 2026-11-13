using Elocaro.Domain.Common;
using Elocaro.Domain.Leases;
using Elocaro.Domain.Metering;
using Elocaro.Domain.OperatingCosts;
using Elocaro.Domain.Receipts;
using static Elocaro.Domain.Tests.OperatingCosts.Scenario;

namespace Elocaro.Domain.Tests.OperatingCosts;

/// <summary>Individual rules of the calculation core on small examples.</summary>
public class OperatingCostCalculatorTests
{
    private static readonly int[] Both = [11, 12];
    private static readonly BillingUnit One = Unit(1, "Wohnung 1", 60m, 50m);
    private static readonly BillingUnit Two = Unit(2, "Wohnung 2", 40m, 50m);

    private static BillingResult Calculate(
        IReadOnlyList<BillingLease> leases,
        IReadOnlyList<BillingCost> costs,
        IReadOnlyList<BillingRule> rules,
        IReadOnlyList<BillingUnit>? units = null,
        IConsumptionSource? consumption = null,
        Period? period = null) =>
        OperatingCostCalculator.Calculate(Input(units ?? [One, Two], leases, costs, rules, consumption, period));

    private static BillingLease[] BothAllYear() =>
        [Lease(11, "Anna", 1, D(2025, 1, 1), persons: 3), Lease(12, "Ben", 2, D(2025, 1, 1), persons: 1)];

    [Fact]
    public void Living_area_key_is_proportional_to_the_area()
    {
        var result = Calculate(BothAllYear(), [Cost(1, "betrkv-01", 1000m)], [Rule("betrkv-01", AllocationKey.LivingArea)]);

        // 60 m² : 40 m² (all year each) → 600.00 € : 400.00 €
        Assert.Equal(600m, Line(result, 11, "betrkv-01").Share);
        Assert.Equal(400m, Line(result, 12, "betrkv-01").Share);
        Assert.Equal(0m, result.LandlordTotal);
    }

    [Fact]
    public void A_cost_type_the_tenants_pay_themselves_leaves_the_unit_out_of_the_allocation_without_a_landlord_share()
    {
        var selfPaying = Two with { SelfPaidCostTypes = new HashSet<string> { "betrkv-08" } };

        var result = Calculate(
            BothAllYear(),
            [Cost(1, "betrkv-08", 1000m), Cost(2, "betrkv-01", 1000m)],
            [Rule("betrkv-08", AllocationKey.LivingArea), Rule("betrkv-01", AllocationKey.LivingArea)],
            units: [One, selfPaying]);

        // Waste: Wohnung 2 pays directly, the whole bill is split among the other units (here only Wohnung 1); no share for Wohnung 2 or the landlord.
        Assert.Equal(1000m, Line(result, 11, "betrkv-08").Share);
        Assert.DoesNotContain(Statement(result, 12).Lines, l => l.CostType == "betrkv-08");
        Assert.DoesNotContain(result.Landlord, p => p.CostType == "betrkv-08");
        // Other cost types still include Wohnung 2: 60 m² : 40 m².
        Assert.Equal(600m, Line(result, 11, "betrkv-01").Share);
        Assert.Equal(400m, Line(result, 12, "betrkv-01").Share);
    }

    [Fact]
    public void Heated_area_key_uses_the_heated_area()
    {
        var result = Calculate(BothAllYear(), [Cost(1, "betrkv-11", 1000m)], [Rule("betrkv-11", AllocationKey.HeatedArea)]);

        // 50 m² : 50 m² → 500.00 € each
        Assert.Equal(500m, Line(result, 11, "betrkv-11").Share);
        Assert.Equal(500m, Line(result, 12, "betrkv-11").Share);
    }

    [Fact]
    public void Person_key_uses_person_days_and_the_change_of_persons_during_the_year()
    {
        var anna = Lease(11, "Anna", 1, D(2025, 1, 1), persons: 1) with
        {
            Persons = [new PersonsState(new Period(D(2025, 1, 1), D(2026, 6, 30)), 1), new PersonsState(new Period(D(2026, 7, 1)), 3)],
        };

        var result = Calculate([anna, Lease(12, "Ben", 2, D(2025, 1, 1), persons: 2)], [Cost(1, "betrkv-08", 1460m)], [Rule("betrkv-08", AllocationKey.Persons)]);

        // Anna: 1 × 181 + 3 × 184 = 733 person days; Ben: 2 × 365 = 730; Σ = 1,463 → 1,460 € × 733 ÷ 1,463 = 731.4968 and 1,460 € × 730 ÷ 1,463 = 728.5032;
        // rounded down 731.49 + 728.50 = 1,459.99 € → the missing cent goes to the larger fractional part (Anna)
        Assert.Equal(731.50m, Line(result, 11, "betrkv-08").Share);
        Assert.Equal(728.50m, Line(result, 12, "betrkv-08").Share);
        Assert.Equal("2,01 von 4,01 Personen (Ø 365 Tage) = 50,10 %", Line(result, 11, "betrkv-08").Basis);
    }

    [Fact]
    public void Unit_key_gives_every_unit_the_same_share()
    {
        var result = Calculate(BothAllYear(), [Cost(1, "betrkv-10", 301m)], [Rule("betrkv-10", AllocationKey.Units)]);

        // 150.50 € each; no cent is lost
        Assert.Equal(150.50m, Line(result, 11, "betrkv-10").Share);
        Assert.Equal(150.50m, Line(result, 12, "betrkv-10").Share);
    }

    [Fact]
    public void Consumption_key_uses_the_meter_type_and_marks_interpolated_readings()
    {
        var consumption = new FixedConsumption(new Dictionary<Guid, ConsumptionQuantity>
        {
            [Id(1)] = new(30m, true, "Zähler W-1: 100 → 130"),
            [Id(2)] = new(10m),
        });

        var result = Calculate(BothAllYear(), [Cost(1, "betrkv-02", 400m)], [Rule("betrkv-02", AllocationKey.Consumption, meter: MeterType.ColdWater)], consumption: consumption);

        // 30 m³ : 10 m³ → 300.00 € : 100.00 €
        Assert.Equal(300m, Line(result, 11, "betrkv-02").Share);
        Assert.Equal(100m, Line(result, 12, "betrkv-02").Share);
        Assert.Contains("interpolierter Zählerstand", Line(result, 11, "betrkv-02").Protocol[1].Note, StringComparison.Ordinal);
        Assert.All(consumption.Calls, c => Assert.Equal(MeterType.ColdWater, c.Type));
    }

    [Fact]
    public void Missing_consumption_is_an_error_and_the_share_is_not_guessed()
    {
        var consumption = new FixedConsumption(new Dictionary<Guid, ConsumptionQuantity>
        {
            [Id(1)] = new(30m),
            [Id(2)] = new(null, false, "Für Zähler W-2 fehlt ein Stand um den 31.12.2026."),
        });

        var result = Calculate(BothAllYear(), [Cost(1, "betrkv-02", 400m)], [Rule("betrkv-02", AllocationKey.Consumption, meter: MeterType.ColdWater)], consumption: consumption);

        Assert.True(result.HasErrors);
        Assert.Contains(result.Problems, p => p.Severity == ProblemSeverity.Error && p.Text.Contains("W-2 fehlt ein Stand", StringComparison.Ordinal));
        Assert.Equal(400m, Line(result, 11, "betrkv-02").Share); // with the missing value counted as 0, the sum still adds up
        Assert.Equal(0m, Line(result, 12, "betrkv-02").Share);
    }

    [Fact]
    public void Fixed_percent_key_leaves_the_rest_with_the_landlord()
    {
        var result = Calculate(
            BothAllYear(),
            [Cost(1, "betrkv-14", 1000m)],
            [Rule("betrkv-14", AllocationKey.FixedPercent, shares: [(1, 40m), (2, 35m)])]);

        // 40 % and 35 % of 1,000.00 €; 25 % stay with the landlord
        Assert.Equal(400m, Line(result, 11, "betrkv-14").Share);
        Assert.Equal(350m, Line(result, 12, "betrkv-14").Share);
        var rest = Assert.Single(result.Landlord);
        Assert.Equal((LandlordReason.ResidualPercent, 250m), (rest.Reason, rest.Amount));
    }

    [Fact]
    public void Fixed_percent_in_a_vacant_unit_goes_to_the_landlord_for_the_vacant_days()
    {
        var result = Calculate(
            [Lease(11, "Anna", 1, D(2026, 4, 1), persons: 1)],
            [Cost(1, "betrkv-14", 1000m)],
            [Rule("betrkv-14", AllocationKey.FixedPercent, shares: [(1, 50m)])],
            units: [One]);

        // 50 % = 500.00 € for Wohnung 1: Anna 275 of 365 days = 376.7123, vacancy 90 days = 123.2877; plus 500.00 € residual
        Assert.Equal(376.71m, Line(result, 11, "betrkv-14").Share);
        Assert.Contains(result.Landlord, p => p.Reason == LandlordReason.Vacancy && p.Amount == 123.29m);
        Assert.Contains(result.Landlord, p => p.Reason == LandlordReason.ResidualPercent && p.Amount == 500m);
    }

    [Fact]
    public void Pre_deduction_in_percent_and_as_amount_stays_with_the_landlord()
    {
        var percent = Calculate(BothAllYear(), [Cost(1, "betrkv-10", 1000m)], [Rule("betrkv-10", AllocationKey.LivingArea, percent: 20m)]);
        var amount = Calculate(BothAllYear(), [Cost(1, "betrkv-10", 1000m)], [Rule("betrkv-10", AllocationKey.LivingArea, amount: 150m)]);

        // 20 % of 1,000.00 € = 200.00 € pre-deduction; 800.00 € by area 60 : 40 → 480.00 and 320.00
        Assert.Equal([480m, 320m], Both.Select(l => Line(percent, l, "betrkv-10").Share));
        Assert.Equal(200m, percent.Landlord.Single(p => p.Reason == LandlordReason.PreDeduction).Amount);

        // 150.00 € fixed amount for the year; 850.00 € by area → 510.00 and 340.00
        Assert.Equal([510m, 340m], Both.Select(l => Line(amount, l, "betrkv-10").Share));
        Assert.Contains("Vorwegabzug von 150,00 €", Line(amount, 11, "betrkv-10").Protocol[1].Values, StringComparison.Ordinal);
    }

    [Fact]
    public void Annual_pre_deduction_amount_is_prorated_for_a_billing_period_shorter_than_a_year()
    {
        var halfYear = Span(D(2026, 1, 1), D(2026, 6, 30));

        var result = Calculate(BothAllYear(), [Cost(1, "betrkv-10", 1000m, halfYear)], [Rule("betrkv-10", AllocationKey.LivingArea, amount: 150m)], period: halfYear);

        // 150.00 € per year × 181/365 days = 74.38 €; 925.62 € by area 60 : 40 → 555.37 and 370.25
        Assert.Equal(74.38m, result.Landlord.Single(p => p.Reason == LandlordReason.PreDeduction).Amount);
        Assert.Equal([555.37m, 370.25m], Both.Select(l => Line(result, l, "betrkv-10").Share));
    }

    [Fact]
    public void Annual_pre_deduction_amount_is_deducted_once_when_direct_costs_belong_to_several_units()
    {
        var result = Calculate(
            BothAllYear(),
            [Cost(1, "betrkv-17", 300m, unit: 1), Cost(2, "betrkv-17", 300m, unit: 2)],
            [Rule("betrkv-17", AllocationKey.Direct, amount: 100m)]);

        // 100.00 € for the cost type and year, not per unit: 50.00 € of each unit's 300.00 €
        Assert.Equal(100m, result.Landlord.Where(p => p.Reason == LandlordReason.PreDeduction).Sum(p => p.Amount));
        Assert.Equal([250m, 250m], Both.Select(l => Line(result, l, "betrkv-17").Share));
    }

    [Fact]
    public void Cost_type_that_is_not_agreed_in_the_lease_is_not_charged()
    {
        var leases = new[] { Lease(11, "Anna", 1, D(2025, 1, 1), excluded: "betrkv-10"), Lease(12, "Ben", 2, D(2025, 1, 1)) };

        var result = Calculate(leases, [Cost(1, "betrkv-10", 1000m)], [Rule("betrkv-10", AllocationKey.LivingArea)]);

        // Anna's 60 % = 600.00 € is not agreed → landlord
        var line = Line(result, 11, "betrkv-10");
        Assert.Equal((0m, 600m), (line.Share, line.NotAgreed));
        Assert.Equal(400m, Line(result, 12, "betrkv-10").Share);
        Assert.Equal((LandlordReason.NotAgreed, 600m), (result.Landlord.Single().Reason, result.Landlord.Single().Amount));
    }

    [Fact]
    public void Inclusive_rent_lease_gets_no_statement_but_is_compared()
    {
        var leases = new[]
        {
            Lease(11, "Anna", 1, D(2025, 1, 1), statement: false, prepayments: Months(2026, 1, 12, 20m, RentComponentType.OperatingCostFlatRate)),
            Lease(12, "Ben", 2, D(2025, 1, 1)),
        };

        var result = Calculate(leases, [Cost(1, "betrkv-01", 1000m)], [Rule("betrkv-01", AllocationKey.LivingArea)]);

        Assert.Single(result.Tenants);
        var comparison = Assert.Single(result.FlatRates);
        Assert.Equal((600m, 240m, 360m), (comparison.ActualCosts, comparison.Paid, comparison.Difference));
        Assert.Equal(LandlordReason.FlatRate, result.Landlord.Single().Reason);
    }

    [Fact]
    public void Shared_flat_by_room_distributes_the_flat_over_the_rooms_by_area()
    {
        var flat = Unit(3, "WG", 90m, null, SharedFlatBilling.ByRoom, Room(31, "Zimmer 1", 30m), Room(32, "Zimmer 2", 60m));
        var leases = new[]
        {
            Lease(21, "Mia", 3, D(2025, 1, 1), room: 31),
            Lease(22, "Noah", 3, D(2025, 1, 1), room: 32),
        };

        var result = Calculate(leases, [Cost(1, "betrkv-01", 900m)], [Rule("betrkv-01", AllocationKey.LivingArea)], units: [flat]);

        // The whole house is the WG (90 m²): rooms share 30 : 60 → 300.00 € and 600.00 €
        Assert.Equal(300m, Line(result, 21, "betrkv-01").Share);
        Assert.Equal(600m, Line(result, 22, "betrkv-01").Share);
        Assert.Empty(result.FlatRates);
    }

    [Fact]
    public void Vacant_shared_flat_room_counts_for_the_landlord_even_with_the_person_key()
    {
        var flat = Unit(3, "WG", 90m, null, SharedFlatBilling.ByRoom, Room(31, "Zimmer 1", 30m), Room(32, "Zimmer 2", 60m));

        var result = Calculate([Lease(21, "Mia", 3, D(2025, 1, 1), persons: 2, room: 31)], [Cost(1, "betrkv-08", 300m)], [Rule("betrkv-08", AllocationKey.Persons)], units: [flat]);

        // Mia 2 persons, vacant Zimmer 2 counts as 1 person → 200.00 € : 100.00 €
        Assert.Equal(200m, Line(result, 21, "betrkv-08").Share);
        Assert.Equal(100m, result.Landlord.Single(p => p.Reason == LandlordReason.Vacancy).Amount);
    }

    [Fact]
    public void Vacant_rooms_can_be_left_out_of_the_key_so_that_the_rented_rooms_carry_the_whole_share()
    {
        var flat = Unit(3, "WG", 90m, null, SharedFlatBilling.ByRoom, Room(31, "Zimmer 1", 30m), Room(32, "Zimmer 2", 60m));
        var lease = Lease(21, "Mia", 3, D(2025, 1, 1), persons: 2, room: 31);

        var persons = Calculate([lease], [Cost(1, "betrkv-08", 300m)], [Rule("betrkv-08", AllocationKey.Persons) with { ExcludeVacantRooms = true }], units: [flat]);
        Assert.Equal(300m, Line(persons, 21, "betrkv-08").Share);
        Assert.DoesNotContain(persons.Landlord, p => p.Reason == LandlordReason.Vacancy);

        var area = Calculate([lease], [Cost(1, "betrkv-01", 300m)], [Rule("betrkv-01", AllocationKey.LivingArea) with { ExcludeVacantRooms = true }], units: [flat]);
        Assert.Equal(300m, Line(area, 21, "betrkv-01").Share);
        Assert.DoesNotContain(area.Landlord, p => p.Reason == LandlordReason.Vacancy);
    }

    [Fact]
    public void Costs_outside_the_billing_period_are_ignored_and_straddling_ones_are_cut()
    {
        var costs = new[]
        {
            Cost(1, "betrkv-01", 1000m, Span(D(2025, 1, 1), D(2025, 12, 31))),
            Cost(2, "betrkv-01", 730m, Span(D(2026, 7, 1), D(2027, 6, 30))), // 184 of 365 days in 2026
        };

        var result = Calculate(BothAllYear(), costs, [Rule("betrkv-01", AllocationKey.LivingArea)]);

        // 730.00 € × 184 ÷ 365 = 368.00 €; the invoice for 2025 does not belong here
        Assert.Equal(368m, result.CostTypes.Single().Total);
        Assert.Equal("730,00 € × 184/365 Tage", result.CostTypes.Single().Items.Single().Calculation);
    }

    [Fact]
    public void Credit_note_reduces_the_costs()
    {
        var result = Calculate(BothAllYear(), [Cost(1, "betrkv-01", 1000m), Cost(2, "betrkv-01", -100m, description: "Gutschrift")], [Rule("betrkv-01", AllocationKey.LivingArea)]);

        Assert.Equal(900m, result.CostTypes.Single().Total);
        Assert.Equal(540m, Line(result, 11, "betrkv-01").Share);
        Assert.Equal(360m, Line(result, 12, "betrkv-01").Share);
    }

    [Fact]
    public void Without_a_key_the_living_area_is_used_with_a_warning()
    {
        var result = Calculate(BothAllYear(), [Cost(1, "betrkv-01", 1000m)], []);

        Assert.Equal(600m, Line(result, 11, "betrkv-01").Share);
        Assert.Contains(result.Problems, p => p.Severity == ProblemSeverity.Warning && p.Text.Contains("Standard: Wohnfläche", StringComparison.Ordinal));
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Direct_cost_without_a_unit_cannot_be_assigned()
    {
        var result = Calculate(BothAllYear(), [Cost(1, "betrkv-17", 100m)], [Rule("betrkv-17", AllocationKey.Direct)]);

        Assert.True(result.HasErrors);
        Assert.Equal(100m, result.Landlord.Single(p => p.Reason == LandlordReason.Unassigned).Amount);
        Assert.DoesNotContain(Statement(result, 11).Lines, l => l.CostType == "betrkv-17");
    }

    [Fact]
    public void Missing_area_and_missing_person_count_are_errors()
    {
        var withoutArea = new BillingUnit(Id(1), "Wohnung 1", [], [], SharedFlatBilling.FlatRate);
        var noPersons = Lease(11, "Anna", 1, D(2025, 1, 1)) with { Persons = [] };

        var area = Calculate([Lease(11, "Anna", 1, D(2025, 1, 1)), Lease(12, "Ben", 2, D(2025, 1, 1))], [Cost(1, "betrkv-01", 100m)], [Rule("betrkv-01", AllocationKey.LivingArea)], units: [withoutArea, Two]);
        var persons = Calculate([noPersons], [Cost(1, "betrkv-08", 100m)], [Rule("betrkv-08", AllocationKey.Persons)], units: [One]);

        Assert.Contains(area.Problems, p => p.Severity == ProblemSeverity.Error && p.Text.Contains("keine Fläche hinterlegt", StringComparison.Ordinal));
        Assert.Contains(persons.Problems, p => p.Severity == ProblemSeverity.Error && p.Text.Contains("keine Personenanzahl", StringComparison.Ordinal));
        Assert.Equal(100m, Line(persons, 11, "betrkv-08").Share); // assumed: 1 person
    }

    [Fact]
    public void Area_change_during_the_year_is_weighted_by_days()
    {
        var changing = new BillingUnit(
            Id(1),
            "Wohnung 1",
            [new AreaState(new Period(D(2000, 1, 1), D(2026, 6, 30)), 50m, 50m), new AreaState(new Period(D(2026, 7, 1)), 70m, 70m)],
            [],
            SharedFlatBilling.FlatRate);

        var result = Calculate([Lease(11, "Anna", 1, D(2025, 1, 1)), Lease(12, "Ben", 2, D(2025, 1, 1))], [Cost(1, "betrkv-01", 4000m)], [Rule("betrkv-01", AllocationKey.LivingArea)], units: [changing, Unit(2, "Wohnung 2", 50m)]);

        // Anna: 50 × 181 + 70 × 184 = 9,050 + 12,880 = 21,930 m²·days; Ben: 50 × 365 = 18,250; Σ = 40,180 → 4,000 € × 21,930 ÷ 40,180 = 2,183.1757
        Assert.Equal(2183.18m, Line(result, 11, "betrkv-01").Share);
        Assert.Equal(1816.82m, Line(result, 12, "betrkv-01").Share);
        Assert.Contains("50 m² × 181 Tage = 9.050 m²·Tage; ", Line(result, 11, "betrkv-01").Protocol[1].Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Billing_period_longer_than_twelve_months_is_rejected()
    {
        var input = Input([One], [], [], [], period: Span(D(2026, 1, 1), D(2027, 1, 1)));

        var error = Assert.Throws<RuleViolationException>(() => OperatingCostCalculator.Calculate(input));

        Assert.Contains("höchstens 12 Monate", error.Message, StringComparison.Ordinal);
        Assert.Throws<RuleViolationException>(() => OperatingCostCalculator.Calculate(Input([One], [], [], [], period: Span(D(2026, 1, 1)))));
    }

    [Fact]
    public void Billing_period_of_twelve_months_that_starts_on_a_leap_day_is_accepted()
    {
        // 29.02.2024 – 28.02.2025 is exactly twelve months (§ 188 para. 2 and 3 BGB)
        var period = Span(D(2024, 2, 29), D(2025, 2, 28));

        var result = Calculate([Lease(11, "Anna", 1, D(2020, 1, 1))], [Cost(1, "betrkv-01", 366m, period)], [Rule("betrkv-01", AllocationKey.LivingArea)], units: [One], period: period);

        Assert.Equal(366m, Line(result, 11, "betrkv-01").Share);
        Assert.Throws<RuleViolationException>(() => Calculate([], [], [], units: [One], period: Span(D(2024, 2, 29), D(2025, 3, 1))));
    }

    [Fact]
    public void Prepayments_of_the_boundary_months_are_prorated_by_days_for_a_billing_period_that_is_not_a_calendar_year()
    {
        var period = Span(D(2026, 3, 15), D(2027, 3, 14));
        var prepayments = new[]
        {
            Prepayment(2026, 2, 100m, 100m), // outside
            Prepayment(2026, 3, 100m, 100m), // 15.–31.03.: 17 of 31 days
            Prepayment(2026, 4, 100m, 100m),
            Prepayment(2027, 3, 100m, 100m), // 01.–14.03.: 14 of 31 days
            Prepayment(2027, 4, 100m, 100m), // outside
        };

        var result = Calculate([Lease(11, "Anna", 1, D(2025, 1, 1), prepayments: prepayments)], [Cost(1, "betrkv-01", 100m, Span(D(2026, 3, 15), D(2027, 3, 14)))], [Rule("betrkv-01", AllocationKey.LivingArea)], units: [One], period: period);

        // 100 × 17/31 = 54.84; 100; 100 × 14/31 = 45.16 → 200.00 (April 2026 full)
        Assert.Equal(54.84m + 100m + 45.16m, Statement(result, 11).Operating!.PrepaymentsPaid);
        Assert.Equal(365, Statement(result, 11).Days);
    }

    [Fact]
    public void Section_35a_labor_costs_are_split_by_the_share_of_the_tenant()
    {
        var gardening = new BillingCost(Id(1), "betrkv-10", "Gärtner", 1000m, Year2026, true, null, Section35a.HouseholdService, 800m);
        var chimney = new BillingCost(Id(2), "betrkv-12", "Schornsteinfeger", 200m, Year2026, true, null, Section35a.TradesmanService, 100m);

        var result = Calculate(BothAllYear(), [gardening, chimney], [Rule("betrkv-10", AllocationKey.LivingArea), Rule("betrkv-12", AllocationKey.LivingArea)]);

        // Anna 60 %: household 800 × 0.6 = 480.00; tradesman 100 × 0.6 = 60.00. Ben 40 %: 320.00 and 40.00.
        Assert.Equal((480m, 60m), (Statement(result, 11).HouseholdLabor, Statement(result, 11).TradesmanLabor));
        Assert.Equal((320m, 40m), (Statement(result, 12).HouseholdLabor, Statement(result, 12).TradesmanLabor));
    }

    [Fact]
    public void Own_subtype_appears_in_the_item_description()
    {
        var cost = Cost(1, "betrkv-13", 500m) with { Subtype = "Gebäudeversicherung", Description = "Police 4711" };

        var result = Calculate(BothAllYear(), [cost], [Rule("betrkv-13", AllocationKey.LivingArea)]);

        Assert.Equal("Gebäudeversicherung: Police 4711", result.CostTypes.Single().Items.Single().Description);
    }

    [Fact]
    public void Heating_costs_can_be_plugged_in_as_a_separate_cost_group()
    {
        var stub = new HeatingStub();
        var leases = new[] { Lease(11, "Anna", 1, D(2025, 1, 1), prepayments: Months(2026, 1, 12, 80m, RentComponentType.HeatingCostPrepayment)), Lease(12, "Ben", 2, D(2025, 1, 1)) };

        var result = OperatingCostCalculator.Calculate(
            Input([One, Two], leases, [Cost(1, "betrkv-04", 3000m), Cost(2, "betrkv-01", 1000m)], [Rule("betrkv-01", AllocationKey.LivingArea)]),
            [stub]);

        Assert.Empty(result.UncalculatedGroups);
        Assert.Equal(["betrkv-04"], stub.ReceivedCostTypes);
        var anna = Statement(result, 11);
        Assert.Equal(600m, anna.Share); // operating costs only
        var heating = anna.Groups.Single(g => g.Group == CostGroup.Heating);
        Assert.Equal((true, 1800m, 960m, 840m), (heating.Calculated, heating.Share, heating.PrepaymentsPaid, heating.Balance));
        Assert.Equal(1440m, anna.Balance); // 600 (operating) + 840 (heating)
        Assert.Contains(result.CostTypes, t => t.CostType == "betrkv-04" && t.Group == CostGroup.Heating);
    }

    [Fact]
    public void Shares_always_add_up_to_the_total_costs_for_arbitrary_data()
    {
        var random = new Random(2026);
        for (var run = 0; run < 300; run++)
        {
            var units = Enumerable.Range(1, random.Next(1, 5)).Select(i => Unit(i, $"W{i}", random.Next(20, 120) + (random.Next(0, 100) / 100m))).ToList();
            var leases = new List<BillingLease>();
            var id = 100;
            foreach (var unit in units)
            {
                var day = D(2025, 6, 1);
                while (day < D(2027, 1, 1))
                {
                    var end = day.AddDays(random.Next(20, 300));
                    if (random.Next(0, 4) > 0)
                    {
                        leases.Add(Lease(id++, $"M{id}", (int)unit.Id.ToByteArray()[0], day, end, random.Next(1, 5)));
                    }

                    day = end.AddDays(1 + random.Next(0, 40));
                }
            }

            var costs = Enumerable.Range(1, random.Next(1, 6))
                .Select(i => Cost(i, $"betrkv-{random.Next(7, 14):00}", (random.Next(-2000, 900000) + 1) / 100m, Span(D(2025, 1, 1).AddDays(random.Next(0, 700)), null)))
                .Select(c => c with { BillingPeriod = new Period(c.BillingPeriod.From, c.BillingPeriod.From.AddDays(random.Next(30, 400))) })
                .ToList();
            var keys = new[] { AllocationKey.LivingArea, AllocationKey.HeatedArea, AllocationKey.Persons, AllocationKey.Units };
            var rules = costs.Select(c => c.CostType).Distinct().Select(t => Rule(t, keys[random.Next(keys.Length)])).ToList();
            var consumption = new RateConsumption(units.Select((u, i) => (i + 1, 0.01m * (i + 1))).ToArray());

            var result = OperatingCostCalculator.Calculate(Input(units, leases, costs, rules, consumption));
            var again = OperatingCostCalculator.Calculate(Input(units, leases, costs, rules, consumption));

            foreach (var type in result.CostTypes)
            {
                var tenants = result.Tenants.Sum(t => t.Lines.Where(l => l.CostType == type.CostType).Sum(l => l.Share));
                var landlord = type.Landlord.Sum(p => p.Amount);
                Assert.Equal(type.Total, tenants + landlord);
                Assert.Equal(decimal.Round(type.Total, 2), type.Total);
            }

            Assert.Equal(result.CostTypes.Select(t => t.Total), again.CostTypes.Select(t => t.Total));
            Assert.Equal(
                result.Tenants.SelectMany(t => t.Lines.Select(l => l.Share)),
                again.Tenants.SelectMany(t => t.Lines.Select(l => l.Share)));
        }
    }

    private sealed class FixedConsumption(Dictionary<Guid, ConsumptionQuantity> values) : IConsumptionSource
    {
        public List<(Guid Unit, MeterType Type, Period Period)> Calls { get; } = [];

        public ConsumptionQuantity Quantity(Guid unitId, MeterType type, Period period)
        {
            Calls.Add((unitId, type, period));
            return values[unitId];
        }
    }

    /// <summary>Stand-in for the heating cost core: Anna pays 60 % of the heating costs.</summary>
    private sealed class HeatingStub : ICostGroupCalculator
    {
        public CostGroup Group => CostGroup.Heating;

        public List<string> ReceivedCostTypes { get; } = [];

        public GroupResult Calculate(BillingInput input, IReadOnlyList<BillingCost> costs)
        {
            ReceivedCostTypes.AddRange(costs.Select(c => c.CostType));
            var total = costs.Sum(c => c.Amount);
            var line = new TenantCostLine("betrkv-04", "Heizung", CostGroup.Heating, total, "Verbrauch", "60 %", 1800m, 1800m, 0m, 0m, 0m, []);
            return new GroupResult(
                [new CostTypeResult("betrkv-04", "Heizung", CostGroup.Heating, total, "Verbrauch", [], 1800m, 1200m, [new LandlordPosition("betrkv-04", LandlordReason.Vacancy, "Leerstand", 1200m)])],
                [new GroupTenantLine(Id(11), line)],
                [new LandlordPosition("betrkv-04", LandlordReason.Vacancy, "Leerstand", 1200m)],
                []);
        }
    }
}
