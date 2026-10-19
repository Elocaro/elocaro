using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MietHahn.Infrastructure.Files;

namespace MietHahn.Infrastructure.Operations;

/// <summary>Prüft, ob die Dateiablage existiert und beschreibbar ist (z. B. Rechte des Docker-Volumes).</summary>
internal sealed class FileStoreHealthCheck(IOptions<FileStoreOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var path = Path.GetFullPath(options.Value.Path);
            Directory.CreateDirectory(path);
            var probe = Path.Combine(path, $".schreibprobe-{Guid.NewGuid():N}");
            await File.WriteAllTextAsync(probe, "ok", cancellationToken);
            File.Delete(probe);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return HealthCheckResult.Unhealthy("Die Dateiablage ist nicht beschreibbar.", ex);
        }
    }
}
