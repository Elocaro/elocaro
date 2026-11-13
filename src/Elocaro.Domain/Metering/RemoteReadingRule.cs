namespace Elocaro.Domain.Metering;

/// <summary>
/// The duty to equip heat and hot water meters in the apartments with remote reading (§ 5 HeizkostenV, existing equipment until the end of 2026).
/// Only a hint for the landlord: nothing in the calculation depends on it.
/// </summary>
public static class RemoteReadingRule
{
    /// <summary>Last day on which existing meters had to be replaced by remotely readable ones.</summary>
    public static readonly DateOnly Deadline = new(2026, 12, 31);

    /// <summary>The rule covers heat and hot water meters.</summary>
    public static bool Covers(MeterType type) => type is MeterType.Heat or MeterType.HotWater;

    /// <summary>An installed meter in an apartment or room that is not remotely readable.</summary>
    public static bool IsMissing(Meter meter)
    {
        ArgumentNullException.ThrowIfNull(meter);
        return Covers(meter.Type) && meter.UnitId is not null && meter.RemovedOn is null && !meter.IsDerived && !meter.RemoteReadable;
    }

    /// <summary>Text for the hint in the statement check.</summary>
    public const string StatementHint = "nicht fernablesbar. Die HeizkostenV verlangt fernablesbare Zähler (Frist für den Bestand: 31.12.2026); ohne Umrüstung kann den Mietern ein Kürzungsrecht nach § 12 HeizkostenV zustehen. Bitte den Rechtsstand prüfen.";
}
