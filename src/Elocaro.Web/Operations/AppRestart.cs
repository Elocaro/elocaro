namespace Elocaro.Web.Operations;

/// <summary>
/// Stops the application a few seconds from now; the container restarts it (<c>restart: unless-stopped</c>). Needed after a restore:
/// the database was replaced, so the data protection keys and everything else cached in memory must be loaded again.
/// </summary>
public sealed partial class AppRestart(IHostApplicationLifetime lifetime, TimeProvider clock, ILogger<AppRestart> logger)
{
    private int scheduled;

    /// <summary>Schedules the stop once; the delay lets the page show its message first.</summary>
    public void Schedule()
    {
        if (Interlocked.Exchange(ref scheduled, 1) == 1)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3), clock);
            Restarting(logger);
            lifetime.StopApplication();
        });
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Neustart nach der Wiederherstellung einer Datensicherung")]
    private static partial void Restarting(ILogger logger);
}
