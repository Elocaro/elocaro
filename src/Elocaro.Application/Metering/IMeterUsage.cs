namespace Elocaro.Application.Metering;

/// <summary>
/// Extension point for modules that point at meters (heating settings, supply contracts ...): a meter they still use cannot be
/// removed. Defined here and implemented by those modules, so that metering does not depend on them.
/// </summary>
public interface IMeterUsage
{
    /// <summary>What still uses the meter, as shown to the user (e.g. „Versorgungsvertrag „Fernwärme““); empty if nothing.</summary>
    Task<IReadOnlyList<string>> UsersAsync(Guid meterId, CancellationToken cancellationToken = default);
}
