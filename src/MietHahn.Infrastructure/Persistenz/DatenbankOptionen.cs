namespace MietHahn.Infrastructure.Persistenz;

/// <summary>Konfigurationsabschnitt <c>Datenbank</c>.</summary>
public sealed class DatenbankOptionen
{
    public const string Abschnitt = "Datenbank";

    /// <summary>Ausstehende Migrationen beim Start automatisch anwenden (Standard: ja).</summary>
    public bool AutomatischMigrieren { get; set; } = true;
}
