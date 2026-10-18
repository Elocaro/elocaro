using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MietHahn.Infrastructure.Dateien;

namespace MietHahn.Infrastructure.Betrieb;

/// <summary>Prüft, ob die Dateiablage existiert und beschreibbar ist (z. B. Rechte des Docker-Volumes).</summary>
internal sealed class DateiablageHealthCheck(IOptions<DateiablageOptionen> optionen) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var pfad = Path.GetFullPath(optionen.Value.Pfad);
            Directory.CreateDirectory(pfad);
            var probe = Path.Combine(pfad, $".schreibprobe-{Guid.NewGuid():N}");
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
