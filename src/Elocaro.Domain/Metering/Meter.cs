using Elocaro.Domain.Common;

namespace Elocaro.Domain.Metering;

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

    /// <summary>Additional reading, e.g. on tenant change.</summary>
    InterimReading = 2,

    /// <summary>Initial reading at installation.</summary>
    Installation = 3,

    /// <summary>Final reading at removal (meter replacement).</summary>
    Removal = 4,
}

/// <summary>
/// Physical meter. On replacement a new meter is created that refers to the old one (<see cref="PredecessorId"/>);
/// together they form a measuring point over which consumption is calculated continuously.
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

    /// <summary>Assigned unit (apartment meter); <c>null</c> = house meter (main or common meter).</summary>
    public Guid? UnitId { get; private set; }

    public Guid? RoomId { get; private set; }

    public MeterType Type { get; private set; }

    public MeasurementUnit MeasurementUnit { get; set; }

    /// <summary>E.g. "Kaltwasser Bad" or "Hauptwasserzähler".</summary>
    public string Name { get; set; }

    public string Number { get; set; }

    /// <summary>Where the meter is installed, e.g. "Keller, Raum 2".</summary>
    public string? Location { get; set; }

    /// <summary>Parent meter (e.g. main water meter); difference = common consumption/loss.</summary>
    public Guid? MainMeterId { get; set; }

    /// <summary>Old meter of the same measuring point (meter replacement).</summary>
    public Guid? PredecessorId { get; private set; }

    public DateOnly InstalledOn { get; private set; }

    public DateOnly? RemovedOn { get; private set; }

    /// <summary>End of calibration validity (water 6 y, hot water/heat 5 y, gas/electricity 8 y from installation).</summary>
    public DateOnly? CalibrationDueDate { get; set; }

    /// <summary>The meter can be read remotely (radio, M-Bus); heat and hot water meters in the apartments must be (<see cref="RemoteReadingRule"/>).</summary>
    public bool RemoteReadable { get; set; }

    /// <summary>Order in the reading round.</summary>
    public int Order { get; set; }

    public string? Notes { get; set; }

    public List<MeterReading> Readings { get; private set; } = [];

    /// <summary>
    /// Calculated meter (e.g. cold water of the 1st floor = main meter of 1st and 2nd floor minus meter of the 2nd floor): its consumption is
    /// the signed sum of the consumption of the source meters; it has no readings of its own.
    /// </summary>
    public List<MeterTerm> Terms { get; private set; } = [];

    public bool IsDerived => Terms.Count > 0;

    /// <summary>Makes this a calculated meter from the source meters (<c>Sign</c> +1 adds, −1 subtracts).</summary>
    public void SetTerms(IEnumerable<(Meter Source, int Sign)> terms)
    {
        var list = terms.ToList();
        if (list.Count < 2)
        {
            throw new RuleViolationException("Ein Rechenzähler braucht mindestens zwei Zähler, aus denen er berechnet wird.");
        }

        if (list.Select(t => t.Source.Id).Distinct().Count() != list.Count)
        {
            throw new RuleViolationException("Ein Zähler darf nur einmal vorkommen.");
        }

        if (list.All(t => t.Sign < 0))
        {
            throw new RuleViolationException("Mindestens ein Zähler muss addiert werden.");
        }

        foreach (var (source, sign) in list)
        {
            if (source.Id == Id || source.PropertyId != PropertyId)
            {
                throw new RuleViolationException("Die Zähler gehören nicht zum Objekt.");
            }

            if (source.Type != Type)
            {
                throw new RuleViolationException($"Der Zähler „{source.Name}“ hat nicht die Zählerart des Rechenzählers.");
            }

            if (source.IsDerived)
            {
                throw new RuleViolationException($"Der Zähler „{source.Name}“ ist selbst ein Rechenzähler. Bitte dessen Quellzähler verwenden.");
            }

            if (sign is not (1 or -1))
            {
                throw new RuleViolationException("Das Vorzeichen eines Zählers ist + oder −.");
            }
        }

        Terms = [.. list.Select(t => new MeterTerm(Id, t.Source.Id, t.Sign))];
    }

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
    /// Records a reading. Errors (e.g. before installation) throw; anomalies (backward running, outliers) are returned by
    /// <see cref="Validate"/> beforehand so that the UI can ask for confirmation.
    /// </summary>
    public MeterReading RecordReading(DateOnly date, decimal value, ReadingType kind, string? readBy, Guid? photoFileId = null, string? note = null)
    {
        if (kind is ReadingType.Installation or ReadingType.Removal)
        {
            throw new RuleViolationException("Einbau- und Ausbaustände entstehen beim Ein- bzw. Ausbau des Zählers.");
        }

        if (IsDerived)
        {
            throw new RuleViolationException("Ein Rechenzähler hat keine eigenen Stände: Sein Verbrauch ergibt sich aus den Zählern, aus denen er berechnet wird.");
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

    /// <summary>Corrects a reading (typing errors): date, value, kind and note. Installation and removal readings keep their date and kind.</summary>
    public MeterReading UpdateReading(Guid readingId, DateOnly date, decimal value, ReadingType kind, string? note)
    {
        var entry = Readings.SingleOrDefault(s => s.Id == readingId) ?? throw new RuleViolationException("Der Stand wurde nicht gefunden.");
        if (IsDerived)
        {
            throw new RuleViolationException("Ein Rechenzähler hat keine eigenen Stände.");
        }

        if (entry.Kind is ReadingType.Installation or ReadingType.Removal)
        {
            entry.Correct(entry.Date, EnsureValid(value), entry.Kind, note);
            return entry;
        }

        if (kind is ReadingType.Installation or ReadingType.Removal)
        {
            throw new RuleViolationException("Einbau- und Ausbaustände entstehen beim Ein- bzw. Ausbau des Zählers.");
        }

        if (!IsActiveOn(date))
        {
            throw new RuleViolationException($"Der Zähler war am {date:dd.MM.yyyy} nicht eingebaut.");
        }

        if (Readings.Any(s => s.Id != readingId && s.Date == date))
        {
            throw new RuleViolationException($"Für den {date:dd.MM.yyyy} gibt es bereits einen Stand.");
        }

        entry.Correct(date, EnsureValid(value), kind, note);
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

    /// <summary>Plausibility check of a new value against the existing readings.</summary>
    /// <param name="outlierPercent">Deviation of the daily consumption from the previous average above which a warning is raised.</param>
    public IReadOnlyList<string> Validate(DateOnly date, decimal value, int outlierPercent = 50, Guid? ignoring = null)
    {
        var warnings = new List<string>();
        var others = Readings.Where(s => s.Id != ignoring).ToList();
        var before = others.Where(s => s.Date < date).MaxBy(s => s.Date);
        var after = others.Where(s => s.Date > date).MinBy(s => s.Date);
        if (before is not null && value < before.Value)
        {
            warnings.Add(Text($"Rückwärtslauf: Der Wert ist kleiner als der Stand vom {before.Date:dd.MM.yyyy} ({before.Value:#,##0.###}). Zählertausch?"));
        }

        if (after is not null && value > after.Value)
        {
            warnings.Add(Text($"Der Wert ist größer als der spätere Stand vom {after.Date:dd.MM.yyyy} ({after.Value:#,##0.###})."));
        }

        // Outlier: daily consumption since the last reading compared to the average before it
        var first = others.Where(s => s.Date < date).MinBy(s => s.Date);
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

    /// <summary>Removes the meter (final reading) and returns the new meter of the measuring point with its initial reading.</summary>
    public Meter Replace(DateOnly date, decimal finalReading, string newNumber, decimal initialReading, DateOnly? calibrationDue = null, bool? remoteReadable = null)
    {
        if (RemovedOn is not null)
        {
            throw new RuleViolationException("Der Zähler ist bereits ausgebaut.");
        }

        if (IsDerived)
        {
            throw new RuleViolationException("Ein Rechenzähler lässt sich nicht tauschen. Bitte den Zähler tauschen, aus dem er berechnet wird.");
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
            RemoteReadable = remoteReadable ?? RemoteReadable,
            PredecessorId = Id,
        };
        newItem.Assign(UnitId, RoomId);
        return newItem;
    }

    public static MeasurementUnit DefaultUnit(MeterType type) => type switch
    {
        MeterType.Heat or MeterType.Electricity => MeasurementUnit.KilowattHours,
        MeterType.OilTank => MeasurementUnit.Liters,
        _ => MeasurementUnit.CubicMeters, // Water, gas
    };

    /// <summary>Calibration periods under Anlage 7 MessEV (cold water 6, hot water and heat 5, gas and electricity 8 years).</summary>
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

    /// <summary>Photo of the meter reading as proof.</summary>
    public Guid? PhotoFileId { get; private set; }

    public string? Note { get; private set; }

    internal void Correct(DateOnly date, decimal value, ReadingType kind, string? note)
    {
        Date = date;
        Value = value;
        Kind = kind;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }
}
