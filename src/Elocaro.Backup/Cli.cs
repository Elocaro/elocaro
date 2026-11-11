using System.Globalization;

namespace Elocaro.Backup;

/// <summary>Console commands for the operator: <c>docker compose run --rm backup restore</c> (target from the environment).</summary>
internal static class Cli
{
    public static async Task<int> RunAsync(string command, string[] arguments, IServiceProvider services, BackupSettings settings)
    {
        var engine = services.GetRequiredService<BackupEngine>();
        var cancellation = CancellationToken.None;
        if (command == "restic")
        {
            var result = await services.GetRequiredService<IProcessRunner>().RunAsync("restic", arguments, settings.EnvironmentTarget?.ToEnvironment(), cancellation);
            await Console.Out.WriteAsync(result.Output);
            return result.ExitCode;
        }

        if (settings.EnvironmentTarget is not { } target)
        {
            await Console.Error.WriteLineAsync("RESTIC_REPOSITORY und RESTIC_PASSWORD müssen gesetzt sein.");
            return 2;
        }

        var errors = target.Validate();
        if (errors.Count > 0)
        {
            await Console.Error.WriteLineAsync(string.Join(' ', errors));
            return 2;
        }

        try
        {
            switch (command)
            {
                case "backup":
                    // Through the coordinator: like the scheduled run it writes the status file that the app monitors.
                    var coordinator = services.GetRequiredService<BackupCoordinator>();
                    coordinator.TryStartBackup(target, cancellation);
                    await coordinator.Running;
                    var result = coordinator.Status().Last!;
                    if (!result.Succeeded)
                    {
                        await Console.Error.WriteLineAsync("FEHLER: " + result.Message);
                        return 1;
                    }

                    await Console.Out.WriteLineAsync("Sicherung " + result.Snapshot + " abgeschlossen");
                    break;
                case "snapshots":
                    foreach (var snapshot in await engine.SnapshotsAsync(target, cancellation))
                    {
                        await Console.Out.WriteLineAsync($"{snapshot.ShortId}  {snapshot.Time.ToString("u", CultureInfo.InvariantCulture)}");
                    }

                    break;
                case "restore":
                    await engine.RestoreAsync(target, arguments.Length > 0 ? arguments[0] : "latest", force: Environment.GetEnvironmentVariable("FORCE") == "1", cancellation);
                    await Console.Out.WriteLineAsync("Wiederherstellung abgeschlossen. Jetzt die App starten: docker compose … start app");
                    break;
                case "verify":
                    await engine.VerifyAsync(target, Environment.GetEnvironmentVariable("BACKUP_VERIFY_SUBSET") ?? "10%", cancellation);
                    await Console.Out.WriteLineAsync("Repository in Ordnung");
                    break;
                default:
                    await Console.Error.WriteLineAsync($"Unbekannter Befehl '{command}' (serve, backup, snapshots, restore, verify, restic)");
                    return 2;
            }
        }
        catch (BackupException ex)
        {
            await Console.Error.WriteLineAsync("FEHLER: " + ex.Message);
            return 1;
        }

        return 0;
    }
}
