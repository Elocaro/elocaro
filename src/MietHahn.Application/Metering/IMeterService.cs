using MietHahn.Application.Common;
using MietHahn.Domain.Common;
using MietHahn.Domain.Metering;

namespace MietHahn.Application.Metering;

/// <summary>Meters of a property, meter readings (with photo), meter replacement and consumption.</summary>
public interface IMeterService
{
    Task<IReadOnlyList<MeterRow>> ListAsync(Guid propertyId, bool includeRemoved = false, CancellationToken cancellationToken = default);

    Task<MeterDetails?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result> CreateAsync(Guid propertyId, MeterCreation creation, CancellationToken cancellationToken = default);

    /// <summary>Deletes a meter (logically, kept in the change history); refused while another meter, the heating settings or a supply contract uses it.</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result> UpdateAsync(Guid id, MeterInput data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a reading. For anomalies (backward running, outliers) it is not saved without <see cref="ReadingInput.Confirmed"/>;
    /// instead the warnings are returned.
    /// </summary>
    Task<ReadingResult> RecordReadingAsync(Guid id, ReadingInput input, Guid? photoFileId = null, CancellationToken cancellationToken = default);

    /// <summary>Corrects a reading (date, value, kind, note); anomalies need <see cref="ReadingInput.Confirmed"/> like when recording.</summary>
    Task<ReadingResult> UpdateReadingAsync(Guid id, Guid readingId, ReadingInput input, CancellationToken cancellationToken = default);

    Task<Result> RemoveReadingAsync(Guid id, Guid readingId, CancellationToken cancellationToken = default);

    /// <summary>Removes the meter and creates the new meter of the measuring point (returns its id).</summary>
    Task<Result> ReplaceAsync(Guid id, ReplacementInput input, CancellationToken cancellationToken = default);

    /// <summary>Active meters in reading order, optionally only of one unit (e.g. on tenant change).</summary>
    Task<IReadOnlyList<MeterRow>> RoundAsync(Guid propertyId, Guid? unitId = null, CancellationToken cancellationToken = default);

    /// <summary>Consumption of the measuring point (across meter replacements) in the period.</summary>
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
    int Order,
    bool IsDerived = false);

public sealed record AnnualConsumption(int Year, Consumption Consumption);

/// <summary>Difference main meter − sum of sub-meters (common consumption, loss) for a year.</summary>
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
    SubMeterBalance? Balance,
    IReadOnlyList<MeterTermView>? Terms = null);

/// <summary>A source meter of a calculated meter; <paramref name="Subtract"/> = its consumption is subtracted.</summary>
public sealed record MeterTermView(Guid MeterId, string Label, bool Subtract);
