using Elocaro.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elocaro.Infrastructure.Persistence.Configurations;

internal sealed class AccessTokenConfiguration : IEntityTypeConfiguration<AccessToken>
{
    public void Configure(EntityTypeBuilder<AccessToken> builder)
    {
        builder.ToTable("access_tokens");
        builder.Property(t => t.Name).HasMaxLength(100);
        builder.Property(t => t.TokenHash).HasMaxLength(64).IsFixedLength();
        builder.Property(t => t.TokenStart).HasMaxLength(16);
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.UserId);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
