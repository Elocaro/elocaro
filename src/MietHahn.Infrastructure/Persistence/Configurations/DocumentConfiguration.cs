using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MietHahn.Domain.Documents;
using MietHahn.Domain.Files;
using MietHahn.Domain.Leases;
using MietHahn.Domain.People;
using MietHahn.Domain.Properties;

namespace MietHahn.Infrastructure.Persistence.Configurations;

internal sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("documents");
        builder.Property(d => d.Title).HasMaxLength(200).UseCollation(MietHahnDbContext.GermanCollation);
        builder.Property(d => d.Category).HasConversion<string>().HasMaxLength(30);
        builder.Property(d => d.Note).HasMaxLength(2000);
        builder.Property(d => d.Tags).HasColumnType("text[]");
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(d => d.FileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Property>().WithMany().HasForeignKey(d => d.PropertyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Unit>().WithMany().HasForeignKey(d => d.UnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Lease>().WithMany().HasForeignKey(d => d.LeaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Person>().WithMany().HasForeignKey(d => d.PersonId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(d => d.ExpiresOn).HasFilter("expires_on IS NOT NULL AND NOT deleted");
    }
}
