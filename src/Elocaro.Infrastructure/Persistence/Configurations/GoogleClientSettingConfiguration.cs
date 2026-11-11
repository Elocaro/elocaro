using Elocaro.Domain.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elocaro.Infrastructure.Persistence.Configurations;

internal sealed class GoogleClientSettingConfiguration : IEntityTypeConfiguration<GoogleClientSetting>
{
    public void Configure(EntityTypeBuilder<GoogleClientSetting> builder)
    {
        builder.ToTable("google_client_settings");
        builder.Property(v => v.ClientId).HasMaxLength(GoogleClientSetting.MaxClientId);
    }
}
