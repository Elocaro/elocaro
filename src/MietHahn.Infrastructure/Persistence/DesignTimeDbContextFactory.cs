using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MietHahn.Infrastructure.Persistence;

/// <summary>Nur für <c>dotnet ef</c> (Migrationen erzeugen); zur Laufzeit wird der Context per DI erstellt.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<MietHahnDbContext>
{
    public MietHahnDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__MietHahn")
            ?? "Host=localhost;Database=miethahn;Username=miethahn;Password=miethahn";

        var options = new DbContextOptionsBuilder<MietHahnDbContext>();
        DependencyInjection.ConfigureNpgsql(options, connection);
        return new MietHahnDbContext(options.Options);
    }
}
