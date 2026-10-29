using Elocaro.Infrastructure.Pdf;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Elocaro.Infrastructure.Operations;

/// <summary>Checks that Gotenberg (PDF generation) is reachable.</summary>
internal sealed class GotenbergHealthCheck(IHttpClientFactory httpFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var http = httpFactory.CreateClient(nameof(GotenbergConverter));
            using var response = await http.GetAsync(new Uri("health", UriKind.Relative), cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"Gotenberg antwortet mit {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return HealthCheckResult.Unhealthy("Gotenberg ist nicht erreichbar.", ex);
        }
    }
}
