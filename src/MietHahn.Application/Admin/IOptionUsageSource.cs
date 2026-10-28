namespace MietHahn.Application.Admin;

/// <summary>
/// A module reports how often its own data uses an <see cref="AppOption"/>. An option that is switched off stays visible where existing data
/// still uses it (<see cref="IModuleState.IsVisible"/>); the module state only collects the answers and knows none of the modules.
/// </summary>
public interface IOptionUsageSource
{
    /// <returns>The options this module's data uses with their count; options without use are left out.</returns>
    Task<IReadOnlyDictionary<AppOption, int>> CountAsync(CancellationToken cancellationToken = default);
}
