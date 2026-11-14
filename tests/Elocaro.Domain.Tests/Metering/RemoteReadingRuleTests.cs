using Elocaro.Domain.Metering;

namespace Elocaro.Domain.Tests.Metering;

public class RemoteReadingRuleTests
{
    private static Meter HeatMeter(Guid? unitId)
    {
        var meter = new Meter(Guid.NewGuid(), MeterType.Heat, "Wärme", "W1", new DateOnly(2020, 1, 1), 0m);
        meter.Assign(unitId, null);
        return meter;
    }

    [Fact]
    public void Heat_meter_in_apartment_without_remote_reading_is_missing()
    {
        Assert.True(RemoteReadingRule.IsMissing(HeatMeter(Guid.NewGuid())));
    }

    [Fact]
    public void Remotely_readable_house_or_other_meters_are_not_missing()
    {
        var readable = HeatMeter(Guid.NewGuid());
        readable.RemoteReadable = true;
        Assert.False(RemoteReadingRule.IsMissing(readable));
        Assert.False(RemoteReadingRule.IsMissing(HeatMeter(null)));

        var cold = new Meter(Guid.NewGuid(), MeterType.ColdWater, "Kalt", "K1", new DateOnly(2020, 1, 1), 0m);
        cold.Assign(Guid.NewGuid(), null);
        Assert.False(RemoteReadingRule.IsMissing(cold));
    }

    [Fact]
    public void Replacement_meter_takes_over_or_sets_remote_reading()
    {
        var meter = HeatMeter(Guid.NewGuid());
        var replacement = meter.Replace(new DateOnly(2026, 6, 1), 10m, "W2", 0m, remoteReadable: true);

        Assert.True(replacement.RemoteReadable);
    }
}
