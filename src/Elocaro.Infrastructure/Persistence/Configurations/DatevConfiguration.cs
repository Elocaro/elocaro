using Elocaro.Domain.Datev;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elocaro.Infrastructure.Persistence.Configurations;

internal sealed class DatevSettingsConfiguration : IEntityTypeConfiguration<DatevSettings>
{
    public void Configure(EntityTypeBuilder<DatevSettings> builder)
    {
        builder.ToTable("datev_settings");
        builder.Property(s => s.Chart).HasConversion<string>().HasMaxLength(10);
        builder.Property(s => s.DictationCode).HasMaxLength(2);
        builder.Ignore(s => s.IsComplete);
    }
}

internal sealed class DatevAccountConfiguration : IEntityTypeConfiguration<DatevAccount>
{
    public void Configure(EntityTypeBuilder<DatevAccount> builder)
    {
        builder.ToTable("datev_accounts");
        builder.Property(a => a.Key).HasMaxLength(100);
        builder.HasIndex(a => a.Key).IsUnique().HasFilter("NOT deleted");
    }
}

internal sealed class DatevExportConfiguration : IEntityTypeConfiguration<DatevExport>
{
    public void Configure(EntityTypeBuilder<DatevExport> builder)
    {
        builder.ToTable("datev_exports");
        builder.Property(e => e.FileName).HasMaxLength(200);
        builder.Property(e => e.Hash).HasMaxLength(64);
        builder.Property(e => e.Total).HasPrecision(14, 2);
    }
}

internal sealed class DatevExportedItemConfiguration : IEntityTypeConfiguration<DatevExportedItem>
{
    public void Configure(EntityTypeBuilder<DatevExportedItem> builder)
    {
        builder.ToTable("datev_exported_items");
        builder.Property(i => i.SourceKey).HasMaxLength(100);
        builder.HasOne<DatevExport>().WithMany().HasForeignKey(i => i.ExportId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(i => i.SourceKey).IsUnique().HasFilter("NOT deleted");
    }
}
