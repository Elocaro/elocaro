using Elocaro.Domain.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elocaro.Infrastructure.Persistence.Configurations;

internal sealed class BackupSettingConfiguration : IEntityTypeConfiguration<BackupSetting>
{
    public void Configure(EntityTypeBuilder<BackupSetting> builder)
    {
        builder.ToTable("backup_settings");
        builder.Property(v => v.Provider).HasConversion<string>().HasMaxLength(20);
        builder.Property(v => v.Location).HasMaxLength(BackupSetting.MaxLocation);
        builder.Property(v => v.Name).HasMaxLength(BackupSetting.MaxName);
    }
}
