using Elocaro.Domain.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elocaro.Infrastructure.Persistence.Configurations;

internal sealed class EmergencyFolderConfiguration : IEntityTypeConfiguration<EmergencyFolder>
{
    public void Configure(EntityTypeBuilder<EmergencyFolder> builder)
    {
        builder.ToTable("emergency_folders");
        builder.Property(e => e.ImportantContacts).HasMaxLength(EmergencyFolder.MaxLength);
        builder.Property(e => e.DocumentLocation).HasMaxLength(EmergencyFolder.MaxLength);
        builder.Property(e => e.FurtherNotes).HasMaxLength(EmergencyFolder.MaxLength);
    }
}
