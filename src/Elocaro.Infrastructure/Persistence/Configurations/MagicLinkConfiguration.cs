using Elocaro.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elocaro.Infrastructure.Persistence.Configurations;

internal sealed class MagicLinkConfiguration : IEntityTypeConfiguration<MagicLink>
{
    public void Configure(EntityTypeBuilder<MagicLink> builder)
    {
        builder.ToTable("magic_links");
        builder.Property(l => l.TokenHash).HasMaxLength(64).IsFixedLength();
        builder.HasIndex(l => l.TokenHash).IsUnique();
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(l => l.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
