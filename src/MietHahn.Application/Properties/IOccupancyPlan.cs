using MietHahn.Domain.Common;

namespace MietHahn.Application.Properties;

/// <summary>Occupation of the rental objects (units and shared-flat rooms) of a property over time: leases and vacancy.</summary>
public interface IOccupancyPlan
{
    /// <summary><c>null</c> if the property does not exist. <paramref name="range"/> must be closed.</summary>
    Task<OccupancyPlan?> GetAsync(Guid propertyId, Period range, CancellationToken cancellationToken = default);
}

public sealed record OccupancyPlan(Guid PropertyId, string PropertyName, Period Range, IReadOnlyList<OccupancyRow> Rows);

/// <param name="Group">Unit name for the rooms of a shared flat (shown as group), otherwise <c>null</c>.</param>
public sealed record OccupancyRow(Guid RentalUnitId, string Label, string? Group, bool IsRoom, IReadOnlyList<OccupancyBar> Bars);

/// <param name="Period">The part of the lease inside the range.</param>
/// <param name="Rent">Monthly rent in total (rent components) at the start of the visible part.</param>
/// <param name="Ended">The lease has ended (before today).</param>
/// <param name="Open">Open-ended lease without a termination.</param>
public sealed record OccupancyBar(Guid LeaseId, Period Period, DateOnly LeaseStart, DateOnly? LeaseEnd, string Tenant, decimal Rent, bool Ended, bool Open);
