using MietHahn.Domain.Common;

namespace MietHahn.Domain.Metering;

public enum MeterType
{
    ColdWater = 0,
    HotWater = 1,
    Heat = 2,
    Electricity = 3,
    Gas = 4,
    OilTank = 5,
}

public enum MeasurementUnit
{
    CubicMeters = 0,
    KilowattHours = 1,
    MegawattHours = 2,
    Liters = 3,
}

public enum ReadingType
{
    Reading = 0,
    Estimate = 1,

    /// <summary>Zusätzliche Ablesung, z. B. bei Mieterwechsel.</summary>
    InterimReading = 2,

    /// <summary>Anfangsstand beim Einbau.</summary>
    Installation = 3,

    /// <summary>Endstand beim Ausbau (Zählertausch).</summary>
    Removal = 4,
}

/// <summary>
/// Physischer Zähler. Beim Tausch wird ein neuer Zähler mit Verweis auf den alten (<see cref="PredecessorId"/>) angelegt;
/// zusammen bilden sie eine Messstelle, über die der Verbrauch durchgängig berechnet wird.
/// </summary>
public sealed class Meter : Entity
{
    private Meter()
    {
        Name = "";
        Number = "";
    }

    public Meter(Guid propertyId, MeterType type, string name, string number, DateOnly installedAt, decimal initialReading)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(number);
        PropertyId = propertyId;
        Type = type;
        MeasurementUnit = DefaultUnit(type);
        Name = name.Trim();
        Number = number.Trim();
        InstalledOn = installedAt;
        CalibrationDueDate = DefaultCalibrationDue(type, installedAt);
        Readings.Add(new MeterReading(Id, installedAt, EnsureValid(initialReading), ReadingType.Installation, null, null, null));
    }

    public Guid PropertyId { get; private set; }

    /// <summary>Zugeordnete Einheit (Wohnungszähler); <c>null</c> = Hauszähler (Haupt-, Allgemeinzähler).</summary>
    public Guid? UnitId { get; private set; }

    public Guid? RoomId { get; private set; }

    public MeterType Type { get; private set; }

    public MeasurementUnit MeasurementUnit { get; set; }

    /// <summary>z. B. „Kaltwasser Bad“ oder „Hauptwasserzähler“.</summary>
    public string Name { get; set; }

    public string Number { get; set; }

    /// <summary>Wo der Zähler hängt, z. B. „Keller, Raum 2“.</summary>
    public string? Location { get; set; }

    /// <summary>Übergeordneter Zähler (z. B. Hauptwasserzähler); Differenz = Allgemeinverbrauch/Schwund.</summary>
    public Guid? MainMeterId { get; set; }

    /// <summary>Alter Zähler derselben Messstelle (Zählertausch).</summary>
    public Guid? PredecessorId { get; private set; }

    public DateOnly InstalledOn { get; private set; }

    public DateOnly? RemovedOn { get; private set; }

    /// <summary>Ende der Eichgültigkeit (Wasser 6 J., Warmwasser/Wärme 5 J., Gas/Strom 8 J. ab Einbau).</summary>
    public DateOnly? CalibrationDueDate { get; set; }

    /// <summary>Reihenfolge in der Ablese-Runde.</summary>
    public int Order { get; set; }

    public string? Notes { get; set; }

    public List<MeterReading> Readings { get; private set; } = [];

    public bool IsActiveOn(DateOnly day) => day >= InstalledOn && (RemovedOn is null || day <= RemovedOn);

    public void Assign(Guid? unitId, Guid? roomId)
    {
        if (roomId is not null && unitId is null)
        {
            throw new RuleViolationException("Ein Zimmerzähler gehört zu einer Einheit.");
        }

        UnitId = unitId;
        RoomId = roomId;
    }

    /// <summary>
    /// Erfasst einen Stand. Fehler (z. B. vor dem Einbau) werfen; Auffälligkeiten (Rückwärtslauf, Ausreißer) liefert
    /// <see cref="Validate"/> vorab, damit die Oberfläche nachfragen kann.
    /// </summary>
    public MeterReading RecordReading(DateOnly date, decimal value, ReadingType kind, string? readBy, Guid? photoFileId = null, string? note = null)
    {
        if (kind is ReadingType.Installation or ReadingType.Removal)
        {
            throw new RuleViolationException("Einbau- und Ausbaustände entstehen beim Ein- bzw. Ausbau des Zählers.");
        }

        if (!IsActiveOn(date))
        {
            throw new RuleViolationException($"Der Zähler war am {date:dd.MM.yyyy} nicht eingebaut ({InstalledOn:dd.MM.yyyy} – {(RemovedOn is { } removal ? removal.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture) : "heute")}).");
        }

        if (Readings.Any(s => s.Date == date))
        {
            throw new RuleViolationException($"Für den {date:dd.MM.yyyy} gibt es bereits einen Stand.");
        }

        var entry = new MeterReading(Id, date, EnsureValid(value), kind, readBy, photoFileId, note);
        Readings.Add(entry);
        return entry;
    }

    public void RemoveReading(Guid readingId)
    {
        var entry = Readings.SingleOrDefault(s => s.Id == readingId) ?? throw new RuleViolationException("Der Stand wurde nicht gefunden.");
        if (entry.Kind is ReadingType.Installation or ReadingType.Removal)
        {
            throw new RuleViolationException("Einbau- und Ausbaustände lassen sich nicht entfernen.");
        }

        Readings.Remove(entry);
    }

    /// <summary>Plausibilitätsprüfung eines neuen Werts gegen die vorhandenen Stände.</summary>
    /// <param name="outlierPercent">Ab welcher Abweichung des Tagesverbrauchs vom bisherigen Durchschnitt gewarnt wird.</param>
    public IReadOnlyList<string> Validate(DateOnly date, decimal value, int outlierPercent = 50)
    {
        var warnings = new List<string>();
        var before = Readings.Where(s => s.Date < date).MaxBy(s => s.Date);
        var after = Readings.Where(s => s.Date > date).MinBy(s => s.Date);
        if (before is not null && value < before.Value)
        {
            warnings.Add(Text($"Rückwärtslauf: Der Wert ist kleiner als der Stand vom {before.Date:dd.MM.yyyy} ({before.Value:#,##0.###}). Zählertausch?"));
        }

        if (after is not null && value > after.Value)
        {
            warnings.Add(Text($"Der Wert ist größer als der spätere Stand vom {after.Date:dd.MM.yyyy} ({after.Value:#,##0.###})."));
        }

        // Ausreißer: Tagesverbrauch seit der letzten Ablesung im Vergleich zum Durchschnitt davor
        var first = Readings.Where(s => s.Date < date).MinBy(s => s.Date);
        if (before is not null && first is not null && before != first && value >= before.Value && date > before.Date)
        {
            var previous = (before.Value - first.Value) / (before.Date.DayNumber - first.Date.DayNumber);
            var current = (value - before.Value) / (date.DayNumber - before.Date.DayNumber);
            if (previous > 0 && Math.Abs(current - previous) / previous * 100 > outlierPercent)
            {
                warnings.Add(Text($"Ungewöhnlicher Verbrauch: {current:#,##0.###} pro Tag statt bisher {previous:#,##0.###} ({(current > previous ? "+" : "")}{(current - previous) / previous * 100:0} %)."));
            }
        }

        return warnings;
    }

    private static string Text(FormattableString text) => text.ToString(System.Globalization.CultureInfo.GetCultureInfo("de-DE"));

    /// <summary>Baut den Zähler aus (Endstand) und liefert den neuen Zähler der Messstelle mit Anfangsstand.</summary>
    public Meter Replace(DateOnly date, decimal finalReading, string newNumber, decimal initialReading, DateOnly? calibrationDue = null)
    {
        if (RemovedOn is not null)
        {
            throw new RuleViolationException("Der Zähler ist bereits ausgebaut.");
        }

        if (date < InstalledOn || Readings.Any(s => s.Date > date))
        {
            throw new RuleViolationException("Nach dem Tauschdatum gibt es noch Stände dieses Zählers.");
        }

        var lastReading = Readings.MaxBy(s => s.Date)!;
        if (finalReading < lastReading.Value)
        {
            throw new RuleViolationException($"Der Endstand ist kleiner als der letzte Stand ({lastReading.Value:0.###}).");
        }

        if (lastReading.Date == date)
        {
            Readings.Remove(lastReading);
        }

        Readings.Add(new MeterReading(Id, date, EnsureValid(finalReading), ReadingType.Removal, null, null, null));
        RemovedOn = date;

        var newItem = new Meter(PropertyId, Type, Name, newNumber, date, initialReading)
        {
            MeasurementUnit = MeasurementUnit,
            Location = Location,
            MainMeterId = MainMeterId,
            Order = Order,
            CalibrationDueDate = calibrationDue ?? DefaultCalibrationDue(Type, date),
            PredecessorId = Id,
        };
        newItem.Assign(UnitId, RoomId);
        return newItem;
    }

    public static MeasurementUnit DefaultUnit(MeterType type) => type switch
    {
        MeterType.Heat or MeterType.Electricity => MeasurementUnit.KilowattHours,
        MeterType.OilTank => MeasurementUnit.Liters,
        _ => MeasurementUnit.CubicMeters, // Wasser, Gas
    };

    /// <summary>Eichfristen nach Anlage 7 MessEV (Kaltwasser 6, Warmwasser und Wärme 5, Gas und Strom 8 Jahre).</summary>
    public static DateOnly? DefaultCalibrationDue(MeterType type, DateOnly installedAt) => type switch
    {
        MeterType.ColdWater => new DateOnly(installedAt.Year + 6, 12, 31),
        MeterType.HotWater or MeterType.Heat => new DateOnly(installedAt.Year + 5, 12, 31),
        MeterType.Gas or MeterType.Electricity => new DateOnly(installedAt.Year + 8, 12, 31),
        _ => null,
    };

    private static decimal EnsureValid(decimal value) =>
        value < 0 ? throw new RuleViolationException("Zählerstände sind nicht negativ.")
        : decimal.Round(value, 3) != value ? throw new RuleViolationException("Zählerstände haben höchstens drei Nachkommastellen.")
        : value;
}

public sealed class MeterReading : Entity
{
    private MeterReading()
    {
    }

    internal MeterReading(Guid meterId, DateOnly date, decimal value, ReadingType kind, string? readBy, Guid? photoFileId, string? note)
    {
        MeterId = meterId;
        Date = date;
        Value = value;
        Kind = kind;
        ReadBy = readBy;
        PhotoFileId = photoFileId;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    public Guid MeterId { get; private set; }

    public DateOnly Date { get; private set; }

    public decimal Value { get; private set; }

    public ReadingType Kind { get; private set; }

    public string? ReadBy { get; private set; }

    /// <summary>Foto des Zählerstands als Nachweis.</summary>
    public Guid? PhotoFileId { get; private set; }

    public string? Note { get; private set; }
}
