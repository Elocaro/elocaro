using Elocaro.Domain.Common;
using Elocaro.Infrastructure.Identity;
using Elocaro.Infrastructure.Persistence.Conventions;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Persistence;

/// <summary>
/// Central DbContext. Entities are configured through <see cref="IEntityTypeConfiguration{TEntity}"/> in this
/// assembly; shared rules (key, version, soft delete) are set by <see cref="EntityConventions"/>.
/// </summary>
public class ElocaroDbContext(DbContextOptions<ElocaroDbContext> options)
    : IdentityDbContext<ApplicationUser, Role, Guid>(options), IDataProtectionKeyContext
{
    /// <summary>ICU collation for German sorting of names (ä next to a, not after z).</summary>
    public const string GermanCollation = "german";

    /// <summary>Keys for cookies and tokens (ASP.NET Core Data Protection) so that sign-ins survive restarts.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        base.OnModelCreating(builder);
        IdentityTables(builder);

        builder.HasPostgresExtension("btree_gist");
        builder.HasCollation(GermanCollation, locale: "de-DE", provider: "icu", deterministic: true);

        builder.ApplyConfigurationsFromAssembly(typeof(ElocaroDbContext).Assembly);
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

        // Monetary amounts: commercial rounding to cents; override per property for quantities/factors with their own precision.
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);

        // Periods are mapped as a column pair <name>_from / <name>_to.
        configurationBuilder.ComplexProperties<Period>();
    }
}
