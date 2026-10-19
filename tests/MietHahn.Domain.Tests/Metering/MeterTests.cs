using MietHahn.Domain.Common;
using MietHahn.Domain.Metering;
using ZaehlerEntitaet = MietHahn.Domain.Metering.Meter;

namespace MietHahn.Domain.Tests.Metering;

public class MeterTests
{
    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    [Fact]
    public void Installation_creates_initial_reading_and_calibration_due_date()
    {
        var meter = WaterMeter(D(2025, 12, 31), 100m);

        var entry = Assert.Single(meter.Readings);
        Assert.Equal((D(2025, 12, 31), 100m, ReadingType.Installation), (entry.Date, entry.Value, entry.Kind));
        Assert.Equal(D(2031, 12, 31), meter.CalibrationDueDate);
        Assert.Equal(MeasurementUnit.CubicMeters, meter.MeasurementUnit);
        Assert.Equal(MeasurementUnit.KilowattHours, ZaehlerEntitaet.DefaultUnit(MeterType.Heat));
        Assert.Equal(D(2030, 12, 31), ZaehlerEntitaet.DefaultCalibrationDue(MeterType.Heat, D(2025, 3, 1)));
    }

    [Fact]
    public void Annual_consumption_from_readings_at_year_end()
    {
        var meter = WaterMeter(D(2025, 12, 31), 100m);
        meter.RecordReading(D(2026, 12, 31), 465.5m, ReadingType.Reading, "Andy");

        var consumption = ConsumptionCalculator.Calculate([meter], Period.CalendarYear(2026));

        Assert.Equal(365.5m, consumption.Quantity);
        Assert.False(consumption.ContainsInterpolation);
        Assert.Null(consumption.MissingReason);
    }

    [Fact]
    public void Tenant_change_without_interim_reading_is_interpolated_and_flagged()
    {
        var meter = WaterMeter(D(2025, 12, 31), 100m);
        meter.RecordReading(D(2026, 12, 31), 465m, ReadingType.Reading, "Andy");

        var toMoveOut = ConsumptionCalculator.Calculate([meter], new Period(D(2026, 1, 1), D(2026, 6, 30)));
        var fromMoveIn = ConsumptionCalculator.Calculate([meter], new Period(D(2026, 7, 1), D(2026, 12, 31)));

        Assert.True(toMoveOut.ContainsInterpolation);
        Assert.Equal(181m, toMoveOut.Quantity); // 181 of 365 days
        Assert.Equal(184m, fromMoveIn.Quantity);
        Assert.Equal(365m, toMoveOut.Quantity + fromMoveIn.Quantity);
    }

    [Fact]
    public void Meter_replacement_within_year_sums_consumption_continuously()
    {
        var old = WaterMeter(D(2025, 12, 31), 100m);
        var newItem = old.Replace(D(2026, 6, 30), 280m, "NEU-2", 0m);
        newItem.RecordReading(D(2026, 12, 31), 190m, ReadingType.Reading, "Andy");

        var consumption = ConsumptionCalculator.Calculate([old, newItem], Period.CalendarYear(2026));

        Assert.Equal(370m, consumption.Quantity); // 180 old meter + 190 new meter
        Assert.Equal(D(2026, 6, 30), old.RemovedOn);
        Assert.Equal(old.Id, newItem.PredecessorId);
        Assert.Equal(ReadingType.Removal, old.Readings.MaxBy(s => s.Date)!.Kind);
        Assert.Equal(D(2032, 12, 31), newItem.CalibrationDueDate); // 6 years from installation in 2026
        Assert.Throws<RuleViolationException>(() => old.RecordReading(D(2026, 8, 1), 300m, ReadingType.Reading, "Andy"));
    }

    [Fact]
    public void Missing_final_reading_is_named_instead_of_extrapolated()
    {
        var meter = WaterMeter(D(2025, 12, 31), 100m);
        meter.RecordReading(D(2026, 10, 1), 380m, ReadingType.Reading, "Andy");

        var consumption = ConsumptionCalculator.Calculate([meter], Period.CalendarYear(2026));

        Assert.Null(consumption.Quantity);
        Assert.Contains("fehlt ein Stand um den 31.12.2026", consumption.MissingReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Plausibility_check_reports_backward_reading_and_outliers()
    {
        var meter = WaterMeter(D(2025, 12, 31), 100m);
        meter.RecordReading(D(2026, 12, 31), 465m, ReadingType.Reading, "Andy"); // 1 m³/day

        Assert.Contains("Rückwärtslauf", Assert.Single(meter.Validate(D(2027, 3, 1), 400m)), StringComparison.Ordinal);
        Assert.Empty(meter.Validate(D(2027, 3, 1), 524m)); // 59 days × 1 m³
        var outlier = Assert.Single(meter.Validate(D(2027, 3, 1), 700m));
        Assert.StartsWith("Ungewöhnlicher Verbrauch", outlier, StringComparison.Ordinal);
        Assert.Contains("statt bisher 1", outlier, StringComparison.Ordinal);
    }

    [Fact]
    public void Readings_before_installation_or_duplicates_are_rejected()
    {
        var meter = WaterMeter(D(2025, 12, 31), 100m);

        Assert.Throws<RuleViolationException>(() => meter.RecordReading(D(2025, 12, 1), 90m, ReadingType.Reading, "Andy"));
        Assert.Throws<RuleViolationException>(() => meter.RecordReading(D(2025, 12, 31), 100m, ReadingType.Reading, "Andy"));
        Assert.Throws<RuleViolationException>(() => meter.RecordReading(D(2026, 1, 31), -1m, ReadingType.Reading, "Andy"));
        Assert.Throws<RuleViolationException>(() => meter.RemoveReading(meter.Readings[0].Id));
    }

    [Fact]
    public void Reading_on_date()
    {
        var meter = WaterMeter(D(2026, 1, 1), 0m);
        meter.RecordReading(D(2026, 1, 11), 10m, ReadingType.Reading, "Andy");

        Assert.Equal(new ReadingOnDay(D(2026, 1, 6), 5m, true), ConsumptionCalculator.ReadingAt(meter.Readings, D(2026, 1, 6)));
        Assert.Equal(new ReadingOnDay(D(2026, 1, 11), 10m, false), ConsumptionCalculator.ReadingAt(meter.Readings, D(2026, 1, 11)));
        Assert.Null(ConsumptionCalculator.ReadingAt(meter.Readings, D(2026, 1, 12)));
    }

    private static ZaehlerEntitaet WaterMeter(DateOnly installation, decimal start) =>
        new(Guid.CreateVersion7(), MeterType.ColdWater, "Kaltwasser Bad", "WZ-1", installation, start);
}
