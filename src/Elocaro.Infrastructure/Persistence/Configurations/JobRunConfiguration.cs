using Elocaro.Infrastructure.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elocaro.Infrastructure.Persistence.Configurations;

internal sealed class JobRunConfiguration : IEntityTypeConfiguration<JobRun>
{
    public void Configure(EntityTypeBuilder<JobRun> builder)
    {
        builder.ToTable("job_runs");
        builder.Property(l => l.JobName).HasMaxLength(200);
        builder.Property(l => l.Errors).HasMaxLength(2000);
        builder.HasIndex(l => new { l.JobName, l.StartedAt });
    }
}
