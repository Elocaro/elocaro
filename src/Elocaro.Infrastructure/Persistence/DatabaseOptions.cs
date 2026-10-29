namespace Elocaro.Infrastructure.Persistence;

/// <summary>Configuration section <c>Database</c>.</summary>
public sealed class DatabaseOptions
{
    public const string Section = "Database";

    /// <summary>Apply pending migrations automatically on startup (default: yes).</summary>
    public bool AutoMigrate { get; set; } = true;
}
