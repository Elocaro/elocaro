using Elocaro.Domain.Prospects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elocaro.Infrastructure.Persistence.Configurations;

internal sealed class ProspectConfiguration : IEntityTypeConfiguration<Prospect>
{
    public void Configure(EntityTypeBuilder<Prospect> builder)
    {
        builder.ToTable("prospects");
        builder.Property(p => p.Name).HasMaxLength(Prospect.MaxName);
        builder.Property(p => p.Email).HasMaxLength(254);
        builder.Property(p => p.Phone).HasMaxLength(50);
        builder.Property(p => p.Notes).HasMaxLength(4000);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(p => p.RentalUnitId);
        builder.HasIndex(p => p.DeleteOn);
    }
}

internal sealed class SelfDisclosureConfiguration : IEntityTypeConfiguration<SelfDisclosure>
{
    public void Configure(EntityTypeBuilder<SelfDisclosure> builder)
    {
        builder.ToTable("self_disclosures");
        builder.Property(s => s.TokenHash).HasMaxLength(64);
        builder.Property(s => s.Occupation).HasMaxLength(100);
        builder.Property(s => s.NetIncome).HasPrecision(12, 2);
        builder.Property(s => s.Pets).HasMaxLength(200);
        builder.Property(s => s.Message).HasMaxLength(1000);
        // The database deletes the answers with the prospect, so a physical deletion of the prospect cannot leave them behind.
        builder.HasOne<Prospect>().WithMany().HasForeignKey(s => s.ProspectId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(s => s.ProspectId);
        builder.HasIndex(s => s.TokenHash).IsUnique();
    }
}
