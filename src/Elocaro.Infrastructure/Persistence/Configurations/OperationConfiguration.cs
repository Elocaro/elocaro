using Elocaro.Infrastructure.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elocaro.Infrastructure.Persistence.Configurations;

internal sealed class OperationConfiguration : IEntityTypeConfiguration<Operation>
{
    public void Configure(EntityTypeBuilder<Operation> builder)
    {
        builder.ToTable("operations");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.User).HasMaxLength(256);
        builder.Property(o => o.Channel).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.Label).HasMaxLength(200);
        builder.HasIndex(o => o.Timestamp);
    }
}
