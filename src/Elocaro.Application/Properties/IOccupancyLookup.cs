namespace Elocaro.Application.Properties;

/// <summary>
/// Who rented a rental object (unit or room) on a day. Provided by the lease module;
/// if an entry is missing, the rental object is vacant.
/// </summary>
public interface IOccupancyLookup
{
    Task<IReadOnlyDictionary<Guid, Occupancy>> AtAsync(IReadOnlyCollection<Guid> rentalUnitIds, DateOnly day, CancellationToken cancellationToken = default);

    /// <summary>Are or were there leases for the rental object (then it must not be deleted)?</summary>
    Task<bool> HasLeasesAsync(Guid rentalUnitId, CancellationToken cancellationToken = default);
}

/// <param name="Tenant">Display name(s) of the tenants, e.g. "Anna Schmidt, Ben Weber".</param>
public sealed record Occupancy(Guid LeaseId, string Tenant, DateOnly Start, DateOnly? End);
