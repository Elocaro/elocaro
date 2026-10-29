using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Elocaro.Infrastructure.Persistence;

/// <summary>Only for <c>dotnet ef</c> (generating migrations); at runtime the context is created via DI.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ElocaroDbContext>
{
    public ElocaroDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Elocaro")
            ?? "Host=localhost;Database=elocaro;Username=elocaro;Password=elocaro";

        var options = new DbContextOptionsBuilder<ElocaroDbContext>();
        DependencyInjection.ConfigureNpgsql(options, connection);
        return new ElocaroDbContext(options.Options);
    }
}
