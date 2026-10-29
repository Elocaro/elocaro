using Elocaro.Domain.Common;

namespace Elocaro.Domain.Admin;

/// <summary>
/// Which optional modules are switched off and whether the setup wizard has been completed (exactly one record).
/// A missing record means: all modules on, wizard not yet completed.
/// </summary>
public sealed class ModuleSettings : Entity
{
    /// <summary>Names of the switched-off modules; everything not listed is on, so new modules default to on.</summary>
    public List<string> DisabledModules { get; set; } = [];

    /// <summary>Names of the switched-off options within modules (rent models, billing types); everything not listed is on.</summary>
    public List<string> DisabledOptions { get; set; } = [];

    /// <summary>When the setup wizard was completed or skipped for good; null = still to be offered.</summary>
    public DateTimeOffset? SetupCompletedAt { get; set; }
}
