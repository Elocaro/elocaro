using Elocaro.Domain.Common;
using Elocaro.Domain.Deposits;
using Elocaro.Domain.Leases;
using Elocaro.Domain.Metering;
using Elocaro.Domain.OperatingCosts;
using Elocaro.Domain.Receipts;

namespace Elocaro.Domain.Tests.Corrections;

/// <summary>Records can be corrected in place (typing errors) instead of only being deleted and entered again.</summary>
public class CorrectionTests
{
    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    private static Lease NewLease(DateOnly? fixedUntil = null)
    {
        var lease = new Lease(Guid.CreateVersion7(), Guid.CreateVersion7(), null, D(2026, 1, 1), fixedUntil);
        lease.SetComponent(RentComponentType.BaseRent, D(2026, 1, 1), 650m);
        lease.SetOccupantCount(D(2026, 1, 1), 2);
        return lease;
    }

    [Fact]
    public void Lease_start_correction_moves_components_and_persons_that_began_with_the_lease()
    {
        var lease = NewLease();
        lease.SetComponent(RentComponentType.BaseRent, D(2027, 1, 1), 700m);

        lease.CorrectTerm(D(2025, 12, 1), null);

        Assert.Equal(D(2025, 12, 1), lease.Start);
        Assert.Equal(650m, lease.AmountAt(RentComponentType.BaseRent, D(2025, 12, 1)));
        Assert.Equal(700m, lease.AmountAt(RentComponentType.BaseRent, D(2027, 1, 1)));
        Assert.Equal(2, lease.OccupantCountOn(D(2025, 12, 15)));
    }

    [Fact]
    public void Lease_end_correction_moves_the_components_that_ended_with_the_fixed_term()
    {
        var lease = NewLease(D(2026, 12, 31));

        lease.CorrectTerm(D(2026, 1, 1), D(2027, 6, 30));

        Assert.Equal(D(2027, 6, 30), lease.End);
        Assert.Equal(D(2027, 6, 30), lease.FixedTermUntil);
        Assert.Equal(650m, lease.AmountAt(RentComponentType.BaseRent, D(2027, 6, 30)));
    }

    [Fact]
    public void Lease_term_correction_refuses_contradictions()
    {
        var lease = NewLease();
        lease.SetComponent(RentComponentType.BaseRent, D(2026, 3, 1), 700m);

        Assert.Throws<RuleViolationException>(() => lease.CorrectTerm(D(2026, 6, 1), null)); // a component starts before the new start
        Assert.Throws<RuleViolationException>(() => lease.CorrectTerm(D(2026, 1, 1), D(2025, 12, 31)));

        var terminated = NewLease();
        terminated.Terminate(D(2026, 2, 1), TerminatingParty.Tenant, D(2026, 5, 31));
        Assert.Throws<RuleViolationException>(() => terminated.CorrectTerm(D(2026, 1, 1), D(2030, 1, 1)));
    }

    [Fact]
    public void An_unapplied_rent_step_can_be_corrected_an_applied_one_not()
    {
        var lease = NewLease();
        lease.SetRentModel(RentModel.Graduated);
        var step = lease.AddRentStep(D(2027, 1, 1), 700m);

        lease.UpdateRentStep(step.Id, D(2027, 3, 1), 710m);

        Assert.Equal((D(2027, 3, 1), 710m), (step.From, step.BaseRent));
        Assert.Throws<RuleViolationException>(() => lease.UpdateRentStep(step.Id, D(2026, 6, 1), 710m)); // less than a year after the start
        lease.ApplyDueSteps(D(2027, 3, 1), DateTimeOffset.UtcNow);
        Assert.Throws<RuleViolationException>(() => lease.UpdateRentStep(step.Id, D(2027, 4, 1), 720m));
    }

    [Fact]
    public void A_deposit_entry_is_corrected_with_the_rules_of_a_new_one()
    {
        var entry = new DepositEntry(Guid.CreateVersion7(), DepositEntryKind.Received, D(2026, 1, 1), 500m);

        entry.Correct(DepositEntryKind.Received, D(2026, 1, 3), 450m, "Rate 1");

        Assert.Equal((D(2026, 1, 3), 450m, "Rate 1"), (entry.Date, entry.Amount, entry.Note));
        Assert.Throws<RuleViolationException>(() => entry.Correct(DepositEntryKind.Received, D(2026, 1, 3), 0m, null));
        Assert.Throws<RuleViolationException>(() => entry.Correct(DepositEntryKind.Deduction, D(2026, 1, 3), 10m, null));
    }

    [Fact]
    public void A_reading_can_be_corrected_but_not_moved_onto_another_reading_or_out_of_the_installed_time()
    {
        var meter = new Meter(Guid.CreateVersion7(), MeterType.ColdWater, "Kaltwasser", "K-1", D(2026, 1, 1), 10m);
        var first = meter.RecordReading(D(2026, 6, 1), 55m, ReadingType.Reading, "A");
        meter.RecordReading(D(2026, 12, 31), 100m, ReadingType.Reading, "A");

        meter.UpdateReading(first.Id, D(2026, 6, 2), 60m, ReadingType.Estimate, "korrigiert");

        Assert.Equal((D(2026, 6, 2), 60m, ReadingType.Estimate, "korrigiert"), (first.Date, first.Value, first.Kind, first.Note));
        Assert.Throws<RuleViolationException>(() => meter.UpdateReading(first.Id, D(2026, 12, 31), 60m, ReadingType.Reading, null));
        Assert.Throws<RuleViolationException>(() => meter.UpdateReading(first.Id, D(2025, 6, 1), 60m, ReadingType.Reading, null));

        // The installation reading keeps date and kind, only its value can change.
        var installation = meter.Readings.Single(r => r.Kind == ReadingType.Installation);
        meter.UpdateReading(installation.Id, D(2026, 3, 3), 12m, ReadingType.Reading, null);
        Assert.Equal((D(2026, 1, 1), 12m, ReadingType.Installation), (installation.Date, installation.Value, installation.Kind));

        // The plausibility check ignores the reading that is being corrected.
        Assert.Empty(meter.Validate(D(2026, 6, 2), 60m, ignoring: first.Id));
    }

    [Fact]
    public void A_cost_item_is_corrected_in_place_within_the_invoice_amount()
    {
        var receipt = new Receipt(D(2026, 2, 1), 1000m);
        var property = Guid.CreateVersion7();
        var item = receipt.AddCostItem("betrkv-01", 600m, property, new Period(D(2026, 1, 1), D(2026, 12, 31)));
        var other = receipt.AddCostItem("betrkv-13", 300m, property, new Period(D(2026, 1, 1), D(2026, 12, 31)));
        var id = item.Id;

        receipt.ChangeCostItem(id, "betrkv-02", 700m, property, new Period(D(2026, 1, 1), D(2026, 6, 30)));

        Assert.Equal(id, item.Id);
        Assert.Equal(("betrkv-02", 700m, D(2026, 6, 30)), (item.CostType, item.Amount, item.BillingPeriod.To!.Value));
        Assert.Throws<RuleViolationException>(() => receipt.ChangeCostItem(id, "betrkv-02", 800m, property, new Period(D(2026, 1, 1), D(2026, 6, 30)))); // 800 + 300 > 1,000
        Assert.Equal(300m, other.Amount);
    }

    [Fact]
    public void A_supply_contract_and_its_tariffs_can_be_corrected()
    {
        var meter = Guid.CreateVersion7();
        var contract = new SupplyContract(Guid.CreateVersion7(), "Wasser", null, "betrkv-02", MeterType.ColdWater, SupplyBasis.MainMeter, PriceUnit.CubicMeter, [meter]);
        var first = contract.AddTariff(D(2024, 1, 1), 3m, 5m);
        var second = contract.AddTariff(D(2025, 1, 1), 4m, 5m);

        contract.Update("Wasser Stadtwerke", "Stadtwerke", "betrkv-02", MeterType.ColdWater, SupplyBasis.MainMeter, PriceUnit.CubicMeter, [meter], null, DeviationHandling.Landlord);
        contract.UpdateTariff(second.Id, D(2025, 2, 1), 4.2m, 6m, "Preiserhöhung");

        Assert.Equal(("Wasser Stadtwerke", DeviationHandling.Landlord), (contract.Name, contract.Deviation));
        Assert.Equal((D(2025, 2, 1), 4.2m, 6m), (second.Validity.From, second.UnitPrice, second.BaseFeePerMonth));
        Assert.Equal(D(2025, 1, 31), first.Validity.To);
        Assert.Throws<RuleViolationException>(() => contract.UpdateTariff(second.Id, D(2023, 1, 1), 4m, 5m, null)); // before the previous tariff starts
        Assert.Throws<RuleViolationException>(() => contract.Update(" ", null, "betrkv-02", MeterType.ColdWater, SupplyBasis.MainMeter, PriceUnit.CubicMeter, [meter], null, DeviationHandling.Landlord));
    }

    [Fact]
    public void A_rent_history_entry_can_be_corrected_in_place_but_not_into_an_overlap()
    {
        var lease = NewLease();
        lease.SetComponent(RentComponentType.BaseRent, D(2026, 7, 1), 700m);
        var first = lease.Components.Single(b => b.Validity.To is not null);
        var second = lease.Components.Single(b => b.Validity.To is null);

        lease.CorrectComponent(first.Id, D(2026, 1, 1), D(2026, 6, 30), 660m, null);
        Assert.Equal(660m, lease.AmountAt(RentComponentType.BaseRent, D(2026, 3, 1)));

        Assert.Throws<RuleViolationException>(() => lease.CorrectComponent(first.Id, D(2026, 1, 1), D(2026, 8, 31), 660m, null)); // overlaps the later entry
        Assert.Throws<RuleViolationException>(() => lease.CorrectComponent(first.Id, D(2025, 12, 1), D(2026, 6, 30), 660m, null)); // before the lease
        Assert.Throws<RuleViolationException>(() => lease.CorrectComponent(first.Id, D(2026, 1, 1), D(2026, 6, 30), -1m, null));

        lease.RemoveComponent(second.Id);
        Assert.Null(lease.AmountAt(RentComponentType.BaseRent, D(2026, 9, 1)));
    }

    [Fact]
    public void A_persons_entry_can_be_corrected_and_the_last_one_stays()
    {
        var lease = NewLease();
        var entry = lease.People.Single();

        lease.CorrectOccupantCount(entry.Id, D(2026, 1, 1), null, 3);
        Assert.Equal(3, lease.OccupantCountOn(D(2026, 5, 1)));

        Assert.Throws<RuleViolationException>(() => lease.CorrectOccupantCount(entry.Id, D(2026, 1, 1), null, 0));
        Assert.Throws<RuleViolationException>(() => lease.RemoveOccupantCount(entry.Id));
    }
}
