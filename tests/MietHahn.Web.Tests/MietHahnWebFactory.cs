using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MietHahn.Web.Tests;

/// <summary>Startet die Web-App ohne Datenbankzugriff (für reine UI-/Routing-Tests).</summary>
public sealed class MietHahnWebFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting("ConnectionStrings:MietHahn", "Host=nicht-verwendet;Database=nicht-verwendet");
        builder.UseSetting("Datenbank:AutomatischMigrieren", "false");
        builder.UseSetting("Jobs:Aktiv", "false");
    }
}
