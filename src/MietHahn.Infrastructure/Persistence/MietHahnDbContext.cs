using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MietHahn.Domain.Common;
using MietHahn.Infrastructure.Identity;
using MietHahn.Infrastructure.Persistence.Conventions;

namespace MietHahn.Infrastructure.Persistence;

/// <summary>
/// Zentraler DbContext. Entitäten werden über <see cref="IEntityTypeConfiguration{TEntity}"/> in diesem
/// Assembly konfiguriert; gemeinsame Regeln (Schlüssel, Version, Soft-Delete) setzt <see cref="EntityConventions"/>.
/// </summary>
public class MietHahnDbContext(DbContextOptions<MietHahnDbContext> options)
    : IdentityDbContext<ApplicationUser, Role, Guid>(options), IDataProtectionKeyContext
{
    /// <summary>ICU-Kollation für deutsche Sortierung von Namen (ä neben a, nicht hinter z).</summary>
    public const string GermanCollation = "german";

    /// <summary>Schlüssel für Cookies und Tokens (ASP.NET Core Data Protection), damit Anmeldungen Neustarts überstehen.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        base.OnModelCreating(builder);
        IdentityTables(builder);

        builder.HasPostgresExtension("btree_gist");
        builder.HasCollation(GermanCollation, locale: "de-DE", provider: "icu", deterministic: true);

        builder.ApplyConfigurationsFromAssembly(typeof(MietHahnDbContext).Assembly);
        EntityConventions.Apply(builder);
    }

    private static void IdentityTables(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApplicationUser>(b =>
        {
            b.ToTable("users");
            b.Property(x => x.DisplayName).HasMaxLength(200);
        });
        modelBuilder.Entity<Role>().ToTable("roles");
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");
        modelBuilder.Entity<DataProtectionKey>().ToTable("data_protection_keys");
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        // Geldbeträge: kaufmännisch auf Cent, Mengen/Faktoren mit eigener Präzision je Property überschreiben.
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);

        // Zeiträume werden als Spaltenpaar <name>_von / <name>_bis abgebildet.
        configurationBuilder.ComplexProperties<Period>();
    }
}
