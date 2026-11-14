namespace Elocaro.Application.Metering;

/// <summary>
/// What metering needs to know from the heating cost settings. Defined here and implemented by the heating module, so that
/// metering does not depend on heating (the other direction exists: heating picks meters).
/// </summary>
public interface IHeatingReferences
{
    /// <summary>Degree days per month (first day of the month) of the weather station chosen for the property; empty without a station.</summary>
    Task<IReadOnlyDictionary<DateOnly, decimal>> DegreeDaysAsync(Guid propertyId, CancellationToken cancellationToken = default);
}
