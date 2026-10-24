using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MietHahn.Domain.Leases;
using MietHahn.Domain.People;
using MietHahn.Domain.Properties;

namespace MietHahn.Infrastructure.Persistence.Configurations;

internal sealed class LeaseConfiguration : IEntityTypeConfiguration<Lease>
{
    public void Configure(EntityTypeBuilder<Lease> builder)
    {
        builder.ToTable("leases");
        builder.Property(v => v.TerminatedBy).HasConversion<string>().HasMaxLength(20);
        builder.Property(v => v.BillingType).HasConversion<string>().HasMaxLength(20);
        builder.Property(v => v.PaymentMethod).HasConversion<string>().HasMaxLength(20);
        builder.Property(v => v.DepositNote).HasMaxLength(1000);
        builder.Property(v => v.Notes).HasMaxLength(4000);
        builder.Property(v => v.RentModel).HasConversion<string>().HasMaxLength(20);
        builder.Property(v => v.IndexBaseValue).HasPrecision(6, 1);
        builder.Property(v => v.ExcludedCostTypes).HasColumnType("text[]").HasDefaultValueSql("'{}'");
        builder.Ignore(v => v.Start);
        builder.Ignore(v => v.End);
        builder.Ignore(v => v.Tenant);
        builder.Ignore(v => v.ReceivesOperatingCostStatement);
        builder.Ignore(v => v.ReceivesHeatingCostStatement);
        builder.HasIndex(v => v.RentalUnitId);

        builder.HasOne<Unit>().WithMany().HasForeignKey(v => v.UnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Room>().WithMany().HasForeignKey(v => v.RoomId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(v => v.Parties).WithOne().HasForeignKey(p => p.LeaseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(v => v.Components).WithOne().HasForeignKey(b => b.LeaseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(v => v.People).WithOne().HasForeignKey(p => p.LeaseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(v => v.RentSteps).WithOne().HasForeignKey(s => s.LeaseId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class RentStepConfiguration : IEntityTypeConfiguration<RentStep>
{
    public void Configure(EntityTypeBuilder<RentStep> builder)
    {
        builder.ToTable("rent_steps");
        builder.HasIndex(s => new { s.LeaseId, s.From }).IsUnique().HasFilter("NOT deleted");
    }
}

internal sealed class CpiValueConfiguration : IEntityTypeConfiguration<CpiValue>
{
    public void Configure(EntityTypeBuilder<CpiValue> builder)
    {
        builder.ToTable("cpi_values");
        builder.Property(v => v.Value).HasPrecision(6, 1);
        builder.Property(v => v.Source).HasMaxLength(50);
        builder.HasIndex(v => v.Month).IsUnique().HasFilter("NOT deleted");
    }
}

internal sealed class LeasePartyConfiguration : IEntityTypeConfiguration<LeaseParty>
{
    public void Configure(EntityTypeBuilder<LeaseParty> builder)
    {
        builder.ToTable("lease_parties");
        builder.Property(p => p.Role).HasConversion<string>().HasMaxLength(20);
        builder.HasOne<Person>().WithMany().HasForeignKey(p => p.PersonId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => new { p.LeaseId, p.PersonId }).IsUnique().HasFilter("NOT deleted");
    }
}

internal sealed class RentComponentConfiguration : IEntityTypeConfiguration<RentComponent>
{
    public void Configure(EntityTypeBuilder<RentComponent> builder)
    {
        builder.ToTable("rent_components");
        builder.Property(b => b.Kind).HasConversion<string>().HasMaxLength(40);
        builder.Property(b => b.Name).HasMaxLength(100);
    }
}

internal sealed class OccupantCountConfiguration : IEntityTypeConfiguration<OccupantCount>
{
    public void Configure(EntityTypeBuilder<OccupantCount> builder) => builder.ToTable("occupant_counts");
}

internal sealed class LeaseContractConfiguration : IEntityTypeConfiguration<LeaseContract>
{
    public void Configure(EntityTypeBuilder<LeaseContract> builder)
    {
        builder.ToTable("lease_contracts");
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasOne<Lease>().WithMany().HasForeignKey(c => c.LeaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MietHahn.Domain.Documents.DocumentTemplateRevision>().WithMany().HasForeignKey(c => c.TemplateRevisionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MietHahn.Domain.Files.StoredFile>().WithMany().HasForeignKey(c => c.DraftFileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MietHahn.Domain.Documents.Document>().WithMany().HasForeignKey(c => c.SignedDocumentId).OnDelete(DeleteBehavior.Restrict);

        // At most one open draft per lease.
        builder.HasIndex(c => c.LeaseId).IsUnique().HasFilter("status = 'Draft' AND NOT deleted").HasDatabaseName("ux_lease_contracts_open_draft");
    }
}

internal sealed class RentIncreaseConfiguration : IEntityTypeConfiguration<RentIncrease>
{
    public void Configure(EntityTypeBuilder<RentIncrease> builder)
    {
        builder.ToTable("rent_increases");
        builder.Property(r => r.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Basis).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Summary).HasMaxLength(500);
        builder.Property(r => r.Note).HasMaxLength(2000);
        builder.Property(r => r.CurrentAmount).HasPrecision(12, 2);
        builder.Property(r => r.NewAmount).HasPrecision(12, 2);
        builder.Property(r => r.ComparativeRentAmount).HasPrecision(12, 2);
        builder.Property(r => r.CapLimit).HasPrecision(12, 2);
        builder.Property(r => r.ModernisationCosts).HasPrecision(12, 2);
        builder.Property(r => r.MaintenanceShare).HasPrecision(12, 2);
        builder.Property(r => r.IndexOldValue).HasPrecision(8, 1);
        builder.Property(r => r.IndexNewValue).HasPrecision(8, 1);
        builder.HasOne<Lease>().WithMany().HasForeignKey(r => r.LeaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MietHahn.Domain.Documents.DocumentTemplateRevision>().WithMany().HasForeignKey(r => r.TemplateRevisionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MietHahn.Domain.Documents.Document>().WithMany().HasForeignKey(r => r.DocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => new { r.LeaseId, r.Status });
    }
}

internal sealed class HandoverProtocolConfiguration : IEntityTypeConfiguration<HandoverProtocol>
{
    private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<HandoverProtocol> builder)
    {
        builder.ToTable("handover_protocols");
        builder.Property(p => p.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.SignedByName).HasMaxLength(200);

        // The filled-in part is one JSON document; the comparer looks at the serialized text so that edits are noticed.
        var comparer = new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<HandoverContent>(
            (a, b) => System.Text.Json.JsonSerializer.Serialize(a, Json) == System.Text.Json.JsonSerializer.Serialize(b, Json),
            c => System.Text.Json.JsonSerializer.Serialize(c, Json).GetHashCode(StringComparison.Ordinal),
            c => System.Text.Json.JsonSerializer.Deserialize<HandoverContent>(System.Text.Json.JsonSerializer.Serialize(c, Json), Json)!);
        builder.Property(p => p.Content)
            .HasConversion(c => System.Text.Json.JsonSerializer.Serialize(c, Json), s => System.Text.Json.JsonSerializer.Deserialize<HandoverContent>(s, Json)!, comparer)
            .HasColumnType("jsonb");

        builder.HasOne<Lease>().WithMany().HasForeignKey(p => p.LeaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MietHahn.Domain.Files.StoredFile>().WithMany().HasForeignKey(p => p.TenantSignatureFileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MietHahn.Domain.Files.StoredFile>().WithMany().HasForeignKey(p => p.LandlordSignatureFileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MietHahn.Domain.Files.StoredFile>().WithMany().HasForeignKey(p => p.DocumentFileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MietHahn.Domain.Documents.Document>().WithMany().HasForeignKey(p => p.DocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => new { p.LeaseId, p.Type }).IsUnique().HasFilter("NOT deleted");
    }
}

internal sealed class SignatureRequestConfiguration : IEntityTypeConfiguration<SignatureRequest>
{
    public void Configure(EntityTypeBuilder<SignatureRequest> builder)
    {
        builder.ToTable("signature_requests");
        builder.Property(s => s.Role).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.SignerName).HasMaxLength(200);
        builder.Property(s => s.Email).HasMaxLength(254);
        builder.Property(s => s.TokenHash).HasMaxLength(64);
        builder.Property(s => s.TypedName).HasMaxLength(200);
        builder.Property(s => s.DocumentHash).HasMaxLength(64);
        builder.Property(s => s.IpAddress).HasMaxLength(64);
        builder.Property(s => s.UserAgent).HasMaxLength(300);
        builder.HasOne<LeaseContract>().WithMany().HasForeignKey(s => s.ContractId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MietHahn.Domain.Files.StoredFile>().WithMany().HasForeignKey(s => s.SignatureFileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => s.TokenHash);
        builder.HasIndex(s => s.ContractId);
    }
}
