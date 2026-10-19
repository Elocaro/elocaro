using MietHahn.Application.Common;
using MietHahn.Domain.Common;
using MietHahn.Domain.Metering;

namespace MietHahn.Application.Metering;

/// <summary>Zähler eines Objekts, Zählerstände (mit Foto), Zählertausch und Verbrauch.</summary>
public interface IMeterService
{
    Task<IReadOnlyList<MeterRow>> ListAsync(Guid propertyId, bool includeRemoved = false, CancellationToken cancellationToken = default);

    Task<MeterDetails?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result> CreateAsync(Guid propertyId, MeterCreation creation, CancellationToken cancellationToken = default);

    Task<Result> UpdateAsync(Guid id, MeterInput data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Erfasst einen Stand. Bei Auffälligkeiten (Rückwärtslauf, Ausreißer) wird ohne <see cref="ReadingInput.Confirmed"/>
    /// nicht gespeichert, sondern die Warnungen kommen zurück.
    /// </summary>
    Task<ReadingResult> RecordReadingAsync(Guid id, ReadingInput input, Guid? photoFileId = null, CancellationToken cancellationToken = default);

    Task<Result> RemoveReadingAsync(Guid id, Guid readingId, CancellationToken cancellationToken = default);

    /// <summary>Baut den Zähler aus und legt den neuen Zähler der Messstelle an (liefert dessen Id).</summary>
    Task<Result> ReplaceAsync(Guid id, ReplacementInput input, CancellationToken cancellationToken = default);

    /// <summary>Aktive Zähler in Ablesereihenfolge, optional nur einer Einheit (z. B. bei Mieterwechsel).</summary>
    Task<IReadOnlyList<MeterRow>> RoundAsync(Guid propertyId, Guid? unitId = null, CancellationToken cancellationToken = default);

    /// <summary>Verbrauch der Messstelle (über Zählertausch hinweg) im Zeitraum.</summary>
    Task<Consumption?> ConsumptionAsync(Guid id, Period period, CancellationToken cancellationToken = default);
}

public sealed record ReadingResult(bool Succeeded, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public bool NeedsConfirmation => !Succeeded && Warnings.Count > 0 && Errors.Count == 0;
}

public sealed record ReadingView(Guid Id, DateOnly Date, decimal Value, ReadingType Kind, string? ReadBy, Guid? PhotoFileId, string? Note);

public sealed record MeterRow(
    Guid Id,
    string Name,
    MeterType Type,
    string Number,
    MeasurementUnit MeasurementUnit,
    string Assignment,
    Guid? UnitId,
    string? Location,
    ReadingView? LastReading,
    DateOnly? CalibrationDueDate,
    DateOnly InstalledOn,
    DateOnly? RemovedOn,
    Guid? MainMeterId,
    int Order);

public sealed record AnnualConsumption(int Year, Consumption Consumption);

/// <summary>Differenz Hauptzähler − Summe der Unterzähler (Allgemeinverbrauch, Schwund) für ein Jahr.</summary>
public sealed record SubMeterBalance(int Year, decimal? MainMeter, decimal? SubMeterTotal, decimal? Difference, IReadOnlyList<string> Hints);

public sealed record MeterDetails(
    MeterRow Meter,
    Guid PropertyId,
    string PropertyName,
    MeterInput Data,
    IReadOnlyList<ReadingView> Readings,
    IReadOnlyList<MeterRow> MeteringPoint,
    IReadOnlyList<AnnualConsumption> AnnualConsumptions,
    IReadOnlyList<MeterRow> SubMeters,
    SubMeterBalance? Balance);
