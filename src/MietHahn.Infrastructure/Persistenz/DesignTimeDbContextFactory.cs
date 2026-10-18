using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MietHahn.Infrastructure.Persistenz;

/// <summary>Nur für <c>dotnet ef</c> (Migrationen erzeugen); zur Laufzeit wird der Context per DI erstellt.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<MietHahnDbContext>
{
    public MietHahnDbContext CreateDbContext(string[] args)
    {
        var verbindung = Environment.GetEnvironmentVariable("ConnectionStrings__MietHahn")
            ?? "Host=localhost;Database=miethahn;Username=miethahn;Password=miethahn";

        var optionen = new DbContextOptionsBuilder<MietHahnDbContext>();
        DependencyInjection.KonfiguriereNpgsql(optionen, verbindung);
        return new MietHahnDbContext(optionen.Options);
    }
}
