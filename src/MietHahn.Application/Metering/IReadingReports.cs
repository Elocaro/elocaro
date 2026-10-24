using MietHahn.Application.Common;
using MietHahn.Domain.Metering;

namespace MietHahn.Application.Metering;

/// <summary>The landlord's side of meter readings that tenants report in the portal: check, accept (becomes a reading) or reject.</summary>
public interface IReadingReports
{
    /// <summary>Open reports, oldest first; of one property or of all.</summary>
    Task<IReadOnlyList<ReadingReportRow>> OpenAsync(Guid? propertyId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes the value over as a reading of the meter. For anomalies (backward running, outliers) it is not saved without
    /// <paramref name="confirmed"/>; the warnings come back instead.
    /// </summary>
    Task<ReadingResult> AcceptAsync(Guid id, bool confirmed = false, CancellationToken cancellationToken = default);

    Task<Result> RejectAsync(Guid id, string? reason, CancellationToken cancellationToken = default);
}

/// <param name="Assignment">Where the meter is (apartment, room, common).</param>
/// <param name="Warnings">Plausibility findings against the existing readings; empty = looks fine.</param>
public sealed record ReadingReportRow(
    Guid Id,
    Guid MeterId,
    Guid PropertyId,
    string PropertyName,
    string MeterName,
    string Number,
    MeasurementUnit Unit,
    string Assignment,
    DateOnly Date,
    decimal Value,
    decimal? PreviousValue,
    DateOnly? PreviousDate,
    string ReportedBy,
    string? Note,
    IReadOnlyList<string> Warnings);
