namespace Elocaro.Application.Admin;

/// <summary>
/// Which modules are on. Held in memory (singleton) so that navigation, todo sources and jobs can ask synchronously;
/// <see cref="EnsureLoadedAsync"/> fills it on first use and after every change.
/// </summary>
public interface IModuleState
{
    /// <summary>True until the settings have been loaded, so a failed load never hides functions.</summary>
    bool IsEnabled(AppModule module);

    /// <summary>Whether the landlord switched the option on (the profile answer, ignoring existing data).</summary>
    bool IsOptionEnabled(AppOption option);

    /// <summary>Whether the option is offered: switched on, or still used by existing leases.</summary>
    bool IsVisible(AppOption option);

    /// <summary>Number of leases that use the option.</summary>
    int UsageCount(AppOption option);

    /// <summary>Reloads how many leases use which rent model and billing type (after leases changed).</summary>
    Task RefreshUsageAsync(CancellationToken cancellationToken = default);

    Task EnsureLoadedAsync(CancellationToken cancellationToken = default);

    /// <summary>Whether the setup wizard was completed; true before loading.</summary>
    bool SetupCompleted { get; }

    /// <summary>The selection as stored, with defaults when nothing is stored yet.</summary>
    Task<ModuleSelection> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the selection (required modules are switched on as well) and, if requested, marks the wizard as completed.
    /// <paramref name="options"/> null leaves the options as they are.
    /// </summary>
    Task SaveAsync(IEnumerable<AppModule> enabled, bool completeSetup, IEnumerable<AppOption>? options = null, CancellationToken cancellationToken = default);

    /// <summary>Marks the wizard as dealt with without changing the selection.</summary>
    Task SkipSetupAsync(CancellationToken cancellationToken = default);
}

public sealed record ModuleSelection(IReadOnlySet<AppModule> Enabled, IReadOnlySet<AppOption> EnabledOptions, DateTimeOffset? SetupCompletedAt);
