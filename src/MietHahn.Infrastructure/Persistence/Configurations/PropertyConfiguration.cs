using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MietHahn.Domain.Files;
using MietHahn.Domain.OperatingCosts;
using MietHahn.Domain.Properties;
using MietHahn.Infrastructure.Identity;

namespace MietHahn.Infrastructure.Persistence.Configurations;

internal sealed class PropertyConfiguration : IEntityTypeConfiguration<Property>
{
    public void Configure(EntityTypeBuilder<Property> builder)
    {
        builder.ToTable("properties");
        builder.Property(o => o.Name).HasMaxLength(200).UseCollation(MietHahnDbContext.GermanCollation);
        builder.Property(o => o.Street).HasMaxLength(200);
        builder.Property(o => o.PostalCode).HasMaxLength(10);
        builder.Property(o => o.City).HasMaxLength(100);
        builder.Property(o => o.CadastralDistrict).HasMaxLength(100);
        builder.Property(o => o.ParcelNumber).HasMaxLength(50);
        builder.Property(o => o.PropertyTaxReference).HasMaxLength(50);
        builder.Property(o => o.Notes).HasMaxLength(4000);
        builder.Property(o => o.HeatingType).HasConversion<string>().HasMaxLength(30);
        builder.Property(o => o.RentCapPercent).HasDefaultValue(20);
        builder.Property(o => o.EnergySource).HasConversion<string>().HasMaxLength(30);
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(o => o.ImageFileId).OnDelete(DeleteBehavior.Restrict);
        builder.Ignore(o => o.Address);

        // Children of the aggregate: removing from the list deletes (softly, see EntitySaveInterceptor).
        builder.HasMany(o => o.Units).WithOne().HasForeignKey(e => e.PropertyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(o => o.Areas).WithOne().HasForeignKey(f => f.PropertyId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UnitConfiguration : IEntityTypeConfiguration<Unit>
{
    public void Configure(EntityTypeBuilder<Unit> builder)
    {
        builder.ToTable("units");
        builder.Property(e => e.Name).HasMaxLength(100).UseCollation(MietHahnDbContext.GermanCollation);
        builder.Property(e => e.Location).HasMaxLength(100);
        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.SharedFlatBilling).HasConversion<string>().HasMaxLength(20).HasDefaultValue(SharedFlatBilling.FlatRate);
        builder.Property(e => e.RoomCount).HasPrecision(4, 1);
        builder.Property(e => e.Bathroom).HasMaxLength(100);
        builder.Property(e => e.EnergyStandard).HasMaxLength(50);
        builder.Property(e => e.Amenities).HasMaxLength(2000);
        builder.Ignore(e => e.IsSharedFlat);
        builder.Ignore(e => e.IsSelfRentable);

        builder.HasMany(e => e.Room).WithOne().HasForeignKey(z => z.UnitId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(e => e.Areas).WithOne().HasForeignKey(f => f.UnitId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class RoomConfiguration : IEntityTypeConfiguration<Room>
{
    public void Configure(EntityTypeBuilder<Room> builder)
    {
        builder.ToTable("rooms");
        builder.Property(z => z.Name).HasMaxLength(100).UseCollation(MietHahnDbContext.GermanCollation);
        builder.Property(z => z.Furnishing).HasConversion<string>().HasMaxLength(20);
        builder.Property(z => z.FurnishingList).HasMaxLength(4000);
        builder.Property(z => z.Notes).HasMaxLength(2000);
        builder.HasMany(z => z.Areas).WithOne().HasForeignKey(f => f.RoomId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PropertyAreaConfiguration : IEntityTypeConfiguration<PropertyArea>
{
    public void Configure(EntityTypeBuilder<PropertyArea> builder)
    {
        builder.ToTable("property_areas");
        builder.Property(f => f.TotalArea).HasPrecision(10, 2);
        builder.Property(f => f.CommonArea).HasPrecision(10, 2);
    }
}

internal sealed class UnitAreaConfiguration : IEntityTypeConfiguration<UnitArea>
{
    public void Configure(EntityTypeBuilder<UnitArea> builder)
    {
        builder.ToTable("unit_areas");
        builder.Property(f => f.LivingArea).HasPrecision(10, 2);
        builder.Property(f => f.HeatedArea).HasPrecision(10, 2);
    }
}

internal sealed class RoomAreaConfiguration : IEntityTypeConfiguration<RoomArea>
{
    public void Configure(EntityTypeBuilder<RoomArea> builder)
    {
        builder.ToTable("room_areas");
        builder.Property(f => f.Area).HasPrecision(10, 2);
        builder.Property(f => f.CommonAreaShare).HasPrecision(10, 2);
        builder.Ignore(f => f.AllocationArea);
    }
}

internal sealed class PropertyAssignmentConfiguration : IEntityTypeConfiguration<PropertyAssignment>
{
    public void Configure(EntityTypeBuilder<PropertyAssignment> builder)
    {
        builder.ToTable("property_assignments");
        builder.HasOne<Property>().WithMany().HasForeignKey(z => z.PropertyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(z => z.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(z => new { z.UserId, z.PropertyId }).IsUnique().HasFilter("NOT deleted");
    }
}

internal sealed class PropertyOwnerConfiguration : IEntityTypeConfiguration<PropertyOwner>
{
    public void Configure(EntityTypeBuilder<PropertyOwner> builder)
    {
        builder.ToTable("property_owners");
        builder.Property(o => o.SharePercent).HasPrecision(5, 2);
        builder.HasOne<Property>().WithMany().HasForeignKey(o => o.PropertyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MietHahn.Domain.People.Person>().WithMany().HasForeignKey(o => o.PersonId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(o => o.PropertyId).HasFilter("NOT deleted");
        builder.HasIndex(o => o.PersonId).HasFilter("NOT deleted");
    }
}
