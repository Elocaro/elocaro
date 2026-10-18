using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MietHahn.Infrastructure.Jobs;

namespace MietHahn.Infrastructure.Persistenz.Konfigurationen;

internal sealed class JobLaufKonfiguration : IEntityTypeConfiguration<JobLauf>
{
    public void Configure(EntityTypeBuilder<JobLauf> builder)
    {
        builder.ToTable("job_laeufe");
        builder.Property(l => l.JobName).HasMaxLength(200);
        builder.Property(l => l.Fehler).HasMaxLength(2000);
        builder.HasIndex(l => new { l.JobName, l.GestartetAm });
    }
}
