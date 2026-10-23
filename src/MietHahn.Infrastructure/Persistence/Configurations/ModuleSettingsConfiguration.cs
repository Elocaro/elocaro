using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MietHahn.Domain.Admin;

namespace MietHahn.Infrastructure.Persistence.Configurations;

internal sealed class ModuleSettingsConfiguration : IEntityTypeConfiguration<ModuleSettings>
{
    public void Configure(EntityTypeBuilder<ModuleSettings> builder)
    {
        builder.ToTable("module_settings");
        builder.Property(m => m.DisabledModules).HasColumnType("text[]");
        builder.Property(m => m.DisabledOptions).HasColumnType("text[]").HasDefaultValueSql("'{}'");
    }
}
