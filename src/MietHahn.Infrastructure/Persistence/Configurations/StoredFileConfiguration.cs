using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MietHahn.Domain.Files;

namespace MietHahn.Infrastructure.Persistence.Configurations;

internal sealed class StoredFileConfiguration : IEntityTypeConfiguration<StoredFile>
{
    public void Configure(EntityTypeBuilder<StoredFile> builder)
    {
        builder.ToTable("stored_files");
        builder.Property(d => d.Name).HasMaxLength(StoredFile.MaxNameLength);
        builder.Property(d => d.MimeType).HasMaxLength(100);
        builder.Property(d => d.Sha256).HasMaxLength(64).IsFixedLength();
        builder.HasIndex(d => d.Sha256);
        builder.Ignore(d => d.IsImage);
    }
}
