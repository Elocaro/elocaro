using Microsoft.EntityFrameworkCore;
using MietHahn.Domain.Gemeinsam;
using MietHahn.Infrastructure.Persistenz.Konventionen;

namespace MietHahn.Infrastructure.Persistenz;

/// <summary>
/// Zentraler DbContext. Entitäten werden über <see cref="IEntityTypeConfiguration{TEntity}"/> in diesem
/// Assembly konfiguriert; gemeinsame Regeln (Schlüssel, Version, Soft-Delete) setzt <see cref="EntitaetKonventionen"/>.
/// </summary>
public class MietHahnDbContext(DbContextOptions<MietHahnDbContext> options) : DbContext(options)
{
    /// <summary>ICU-Kollation für deutsche Sortierung von Namen (ä neben a, nicht hinter z).</summary>
    public const string DeutscheKollation = "german";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasPostgresExtension("btree_gist");
        modelBuilder.HasCollation(DeutscheKollation, locale: "de-DE", provider: "icu", deterministic: true);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MietHahnDbContext).Assembly);
        EntitaetKonventionen.Anwenden(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        // Geldbeträge: kaufmännisch auf Cent, Mengen/Faktoren mit eigener Präzision je Property überschreiben.
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);

        // Zeiträume werden als Spaltenpaar <name>_von / <name>_bis abgebildet.
        configurationBuilder.ComplexProperties<Zeitraum>();
    }
}
