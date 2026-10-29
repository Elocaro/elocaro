using Elocaro.Application.Accounts;
using Elocaro.Application.Admin;
using Elocaro.Application.Setup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elocaro.Infrastructure.Setup;

public static partial class DemoMode
{
    /// <summary>On an empty demo instance: creates the demo administrator and loads the example data.</summary>
    public static async Task PrepareDemoAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<DemoOptions>>().Value;
        if (!options.Enabled)
        {
            return;
        }

        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DemoMode));
        if (string.IsNullOrWhiteSpace(options.AdminPassword))
        {
            throw new InvalidOperationException("Demo:AdminPassword fehlt (Demo__AdminPassword).");
        }

        var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
        if (!await accounts.InitialSetupPossibleAsync(cancellationToken))
        {
            return;
        }

        var created = await accounts.CreateFirstAdminAsync(options.AdminEmail, "Max Mustermann", options.AdminPassword, cancellationToken);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException("Der Demo-Benutzer konnte nicht angelegt werden: " + string.Join(" ", created.Errors));
        }

        var loaded = await scope.ServiceProvider.GetRequiredService<IDemoData>().LoadAsync(cancellationToken);

        // Show everything and skip the setup wizard: visitors want to see the whole range of functions.
        await scope.ServiceProvider.GetRequiredService<IModuleState>().SaveAsync(Enum.GetValues<AppModule>(), true, Enum.GetValues<AppOption>(), cancellationToken);
        DemoIsReady(logger, loaded.Succeeded);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Demo-Instanz eingerichtet (Beispieldaten geladen: {Loaded})")]
    private static partial void DemoIsReady(ILogger logger, bool loaded);
}
