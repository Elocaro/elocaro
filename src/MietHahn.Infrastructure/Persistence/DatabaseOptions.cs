namespace MietHahn.Infrastructure.Persistence;

/// <summary>Konfigurationsabschnitt <c>Datenbank</c>.</summary>
public sealed class DatabaseOptions
{
    public const string Section = "Database";

    /// <summary>Ausstehende Migrationen beim Start automatisch anwenden (Standard: ja).</summary>
    public bool AutoMigrate { get; set; } = true;
}
