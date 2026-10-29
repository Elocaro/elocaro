using Elocaro.Domain.Documents;
using Elocaro.Domain.Files;
using Elocaro.Domain.Leases;
using Elocaro.Domain.People;
using Elocaro.Domain.Properties;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elocaro.Infrastructure.Persistence.Configurations;

internal sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("documents");
        builder.Property(d => d.Title).HasMaxLength(200).UseCollation(ElocaroDbContext.GermanCollation);
        builder.Property(d => d.Category).HasConversion<string>().HasMaxLength(30);
        builder.Property(d => d.Note).HasMaxLength(2000);
        builder.Property(d => d.TenantsNotifiedAt);
        builder.Property(d => d.Tags).HasColumnType("text[]");
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(d => d.FileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Property>().WithMany().HasForeignKey(d => d.PropertyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Unit>().WithMany().HasForeignKey(d => d.UnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Lease>().WithMany().HasForeignKey(d => d.LeaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Person>().WithMany().HasForeignKey(d => d.PersonId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DocumentTemplateRevision>().WithMany().HasForeignKey(d => d.TemplateRevisionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(d => d.ExpiresOn).HasFilter("expires_on IS NOT NULL AND NOT deleted");
    }
}

internal sealed class DocumentReadConfiguration : IEntityTypeConfiguration<DocumentRead>
{
    public void Configure(EntityTypeBuilder<DocumentRead> builder)
    {
        builder.ToTable("document_reads");
        builder.HasOne<Document>().WithMany().HasForeignKey(l => l.DocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(l => new { l.DocumentId, l.UserId }).IsUnique().HasFilter("NOT deleted");
    }
}

internal sealed class DocumentTemplateConfiguration : IEntityTypeConfiguration<DocumentTemplate>
{
    public void Configure(EntityTypeBuilder<DocumentTemplate> builder)
    {
        builder.ToTable("document_templates");
        builder.Property(t => t.Name).HasMaxLength(100).UseCollation(ElocaroDbContext.GermanCollation);
        builder.Property(t => t.Kind).HasConversion<string>().HasMaxLength(30);
        builder.Property(t => t.Description).HasMaxLength(500);
        builder.Property(t => t.Subject).HasMaxLength(200);
        builder.Property(t => t.Body).HasMaxLength(DocumentTemplate.MaxBodyLength);
        builder.HasIndex(t => t.Name).IsUnique().HasFilter("NOT deleted");
    }
}

internal sealed class DocumentTemplateRevisionConfiguration : IEntityTypeConfiguration<DocumentTemplateRevision>
{
    public void Configure(EntityTypeBuilder<DocumentTemplateRevision> builder)
    {
        builder.ToTable("document_template_revisions");
        builder.Property(r => r.Subject).HasMaxLength(200);
        builder.Property(r => r.Body).HasMaxLength(DocumentTemplate.MaxBodyLength);
        builder.HasOne<DocumentTemplate>().WithMany().HasForeignKey(r => r.TemplateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => new { r.TemplateId, r.Number }).IsUnique();
    }
}
