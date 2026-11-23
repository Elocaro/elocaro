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

internal sealed class ViewingSlotConfiguration : IEntityTypeConfiguration<ViewingSlot>
{
    public void Configure(EntityTypeBuilder<ViewingSlot> builder)
    {
        builder.ToTable("viewing_slots");
        // If the prospect (and so the personal data) is deleted, the date stays and becomes free.
        builder.HasOne<Prospect>().WithMany().HasForeignKey(s => s.ProspectId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(s => new { s.RentalUnitId, s.StartsAt });
        builder.HasIndex(s => s.ProspectId);
    }
}

internal sealed class ViewingLinkConfiguration : IEntityTypeConfiguration<ViewingLink>
{
    public void Configure(EntityTypeBuilder<ViewingLink> builder)
    {
        builder.ToTable("viewing_links");
        builder.Property(l => l.TokenHash).HasMaxLength(64);
        builder.HasIndex(l => l.RentalUnitId);
        builder.HasIndex(l => l.TokenHash).IsUnique();
    }
}
