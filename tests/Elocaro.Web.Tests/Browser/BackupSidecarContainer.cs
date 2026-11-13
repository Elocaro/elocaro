using System.Net;
using System.Net.Sockets;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Npgsql;

namespace Elocaro.Web.Tests.Browser;

/// <summary>
/// The real backup container (image <c>ELOCARO_BACKUP_IMAGE</c>, built from <c>deploy/backup/Dockerfile</c>) next to the app under test:
/// same database (the test database, through the host network), the file storage of the app as <c>/data</c>, a local repository.
/// </summary>
internal sealed class BackupSidecarContainer : IAsyncDisposable
{
    public const string ImageVariable = "ELOCARO_BACKUP_IMAGE";

    private readonly IContainer container;
    private readonly string workDirectory;

    private BackupSidecarContainer(IContainer container, string workDirectory)
    {
        this.container = container;
        this.workDirectory = workDirectory;
    }

    public static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>Image for the tests; <c>null</c> if not set (then the tests are skipped).</summary>
    public static string? Image => Environment.GetEnvironmentVariable(ImageVariable);

    public static async Task<BackupSidecarContainer> StartAsync(string image, int port, string token, string connection, string filesDirectory, string repositoryDirectory, string statusDirectory, string workDirectory)
    {
        var database = new NpgsqlConnectionStringBuilder(connection);
        Directory.CreateDirectory(filesDirectory);
        Directory.CreateDirectory(repositoryDirectory);
        Directory.CreateDirectory(statusDirectory);
        var created = new ContainerBuilder(image)
            // Host network: reaches the database container through its published port and is reachable by the app under test.
            .WithCreateParameterModifier(parameters => parameters.HostConfig!.NetworkMode = "host")
            .WithEnvironment("ASPNETCORE_URLS", $"http://127.0.0.1:{port}")
            .WithEnvironment("ASPNETCORE_HTTP_PORTS", string.Empty)
            .WithEnvironment("BACKUP_API_TOKEN", token)
            .WithEnvironment("PGHOST", database.Host!)
            .WithEnvironment("PGPORT", database.Port.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .WithEnvironment("PGUSER", database.Username!)
            .WithEnvironment("PGPASSWORD", database.Password!)
            .WithEnvironment("PGDATABASE", database.Database!)
            .WithBindMount(filesDirectory, "/data")
            .WithBindMount(repositoryDirectory, "/repo")
            .WithBindMount(statusDirectory, "/status")
            .Build();
        await created.StartAsync(TestContext.Current.CancellationToken);

        using var http = new HttpClient();
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (true)
        {
            try
            {
                using var response = await http.GetAsync(new Uri($"http://127.0.0.1:{port}/health"), TestContext.Current.CancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    break;
                }
            }
            catch (HttpRequestException)
            {
                // not up yet
            }

            if (DateTime.UtcNow > deadline)
            {
                var (stdout, stderr) = await created.GetLogsAsync(ct: TestContext.Current.CancellationToken);
                throw new InvalidOperationException($"Der Backup-Container wurde nicht bereit.\n{stdout}\n{stderr}");
            }

            await Task.Delay(500, TestContext.Current.CancellationToken);
        }

        return new BackupSidecarContainer(created, workDirectory);
    }

    /// <summary>Runs a command in the container (as root, like the backup itself).</summary>
    public async Task<string> ExecAsync(params string[] command)
    {
        var result = await container.ExecAsync(command, TestContext.Current.CancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"{string.Join(' ', command)}: {result.Stderr}");
        }

        return result.Stdout;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            // For the diagnosis of a test that hangs: what the service wrote (and what it was doing), in the temp directory
            var (stdout, stderr) = await container.GetLogsAsync(ct: CancellationToken.None);
            var processes = await container.ExecAsync(["sh", "-c", "ps -o pid,etime,args 2>/dev/null | head -20; true"], CancellationToken.None);
            await File.WriteAllTextAsync(Path.Combine(Path.GetTempPath(), $"elocaro-sidecar-{container.Id[..12]}.log"), $"{stdout}\n{stderr}\n--- ps ---\n{processes.Stdout}", CancellationToken.None);
        }
#pragma warning disable CA1031 // Diagnosis only
        catch (Exception)
#pragma warning restore CA1031
        {
            // nothing to learn then
        }

        try
        {
            // The container wrote as root (restic repository, files with the owner of the app): clean up from inside so that the
            // test directories can be deleted afterwards.
            await container.ExecAsync(["sh", "-c", "rm -rf /data/* /data/.[!.]* /repo/* /repo/.[!.]* /status/* 2>/dev/null; true"], CancellationToken.None);
        }
        finally
        {
            await container.DisposeAsync();
            if (Directory.Exists(workDirectory))
            {
                Directory.Delete(workDirectory, recursive: true);
            }
        }
    }
}
