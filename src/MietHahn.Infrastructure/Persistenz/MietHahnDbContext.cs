using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MietHahn.Domain.Gemeinsam;
using MietHahn.Infrastructure.Identitaet;
using MietHahn.Infrastructure.Persistenz.Konventionen;

namespace MietHahn.Infrastructure.Persistenz;

/// <summary>
/// Zentraler DbContext. Entitäten werden über <see cref="IEntityTypeConfiguration{TEntity}"/> in diesem
/// Assembly konfiguriert; gemeinsame Regeln (Schlüssel, Version, Soft-Delete) setzt <see cref="EntitaetKonventionen"/>.
/// </summary>
public class MietHahnDbContext(DbContextOptions<MietHahnDbContext> options)
    : IdentityDbContext<Benutzer, Rolle, Guid>(options), IDataProtectionKeyContext
{
    /// <summary>ICU-Kollation für deutsche Sortierung von Namen (ä neben a, nicht hinter z).</summary>
    public const string DeutscheKollation = "german";

    /// <summary>Schlüssel für Cookies und Tokens (ASP.NET Core Data Protection), damit Anmeldungen Neustarts überstehen.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        base.OnModelCreating(builder);
        IdentityTabellen(builder);

        builder.HasPostgresExtension("btree_gist");
        builder.HasCollation(DeutscheKollation, locale: "de-DE", provider: "icu", deterministic: true);

        builder.ApplyConfigurationsFromAssembly(typeof(MietHahnDbContext).Assembly);
        EntitaetKonventionen.Anwenden(builder);
    }

    private static void IdentityTabellen(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Benutzer>(b =>
        {
            b.ToTable("benutzer");
            b.Property(x => x.Anzeigename).HasMaxLength(200);
        });
        modelBuilder.Entity<Rolle>().ToTable("rollen");
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("benutzer_rollen");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("benutzer_claims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("benutzer_anmeldungen_extern");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("benutzer_tokens");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("rollen_claims");
        modelBuilder.Entity<DataProtectionKey>().ToTable("data_protection_keys");
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
