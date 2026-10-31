using System.Data.Common;
using Elocaro.Application.Accounts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Elocaro.Infrastructure.Setup;

public static partial class SetupCodeAnnouncement
{
    /// <summary>
    /// On an empty installation without a configured code, the log is the only place where the code can be read. As soon as an
    /// account exists, nothing is written.
    /// </summary>
    public static async Task AnnounceSetupCodeAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var code = scope.ServiceProvider.GetRequiredService<ISetupCode>();
        if (!code.IsGenerated)
        {
            return;
        }

        bool setupPossible;
        try
        {
            setupPossible = await scope.ServiceProvider.GetRequiredService<IAccountService>().InitialSetupPossibleAsync(cancellationToken);
        }
        catch (DbException)
        {
            // Not migrated yet (Database__AutoMigrate=false) or not reachable: the code is of no use once an account exists, so it does no harm.
            setupPossible = true;
        }

        if (setupPossible)
        {
            var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(SetupCodeAnnouncement));
            SetupCodeGenerated(logger, code.Value);
        }
    }

    // Warning: must also be visible with a stricter log level.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Einrichtungscode für die Ersteinrichtung (/konto/einrichtung): {SetupCode}. Er gilt bis zum nächsten Neustart; danach steht hier ein neuer. Fest einstellen mit Setup__Code.")]
    private static partial void SetupCodeGenerated(ILogger logger, string setupCode);
}
