using Microsoft.Extensions.Diagnostics.HealthChecks;
using MietHahn.Infrastructure.Pdf;

namespace MietHahn.Infrastructure.Betrieb;

/// <summary>Prüft, ob Gotenberg (PDF-Erzeugung) erreichbar ist.</summary>
internal sealed class GotenbergHealthCheck(IHttpClientFactory httpFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var http = httpFactory.CreateClient(nameof(GotenbergKonverter));
            using var antwort = await http.GetAsync(new Uri("health", UriKind.Relative), cancellationToken);
            return antwort.IsSuccessStatusCode
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"Gotenberg antwortet mit {(int)antwort.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return HealthCheckResult.Unhealthy("Gotenberg ist nicht erreichbar.", ex);
        }
    }
}
