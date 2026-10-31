using System.Net;
using System.Text.Json;
using Elocaro.Application.Accounts;
using Elocaro.Infrastructure.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Elocaro.Web.Operations;

internal static class OperationsExtensions
{
    /// <summary>Key in <c>HttpContext.Items</c> of the script nonce of the current request (see <c>App.razor</c>).</summary>
    public const string CspNonceKey = "csp-nonce";

    public const string LivenessPath = "/health/live";
    public const string ReadyPath = "/health";

    /// <summary>Behind a reverse proxy (Caddy), take scheme and client IP from X-Forwarded-*.</summary>
    public static IServiceCollection AddElocaroOperations(this IServiceCollection services) =>
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            o.ForwardLimit = 1;

            // The app is only reachable within the Docker network; only the proxy sits there.
            o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("10.0.0.0/8"));
            o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("172.16.0.0/12"));
            o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("192.168.0.0/16"));
        });

    /// <summary>Security headers for all responses (CSP suited to static Blazor SSR).</summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var header = context.Response.Headers;
            header.XContentTypeOptions = "nosniff";
            header.XFrameOptions = "DENY";
            header["Referrer-Policy"] = "strict-origin-when-cross-origin";
            header["Permissions-Policy"] = "camera=(self), microphone=(), geolocation=(), payment=()";
            header["Cross-Origin-Opener-Policy"] = "same-origin";
            if (!header.ContainsKey("Content-Security-Policy"))
            {
                // The inline import map of the fingerprinted assets is allowed by a nonce per request (App.razor), not by unsafe-inline.
                var nonce = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
                context.Items[CspNonceKey] = nonce;
                header.ContentSecurityPolicy =
                    $"default-src 'self'; script-src 'self' 'nonce-{nonce}'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
                    "font-src 'self'; connect-src 'self'; frame-ancestors 'none'; form-action 'self'; base-uri 'self'; object-src 'none'";
            }

            await next(context);
        });

    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Live: the process responds (for the container health check).
        endpoints.MapHealthChecks(LivenessPath, new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();

        // Ready: database, Gotenberg, file store and backup. The public only gets the overall status (no internals exposed).
        endpoints.MapHealthChecks(ReadyPath, new HealthCheckOptions
        {
            Predicate = c => c.Tags.Contains(HealthTags.Ready),
            ResponseWriter = WriteReadinessAsync,
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status200OK,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
            },
        }).AllowAnonymous();

        return endpoints;
    }

    /// <summary>Accounts with the system permission also see the status of each check, never error details.</summary>
    private static async Task WriteReadinessAsync(HttpContext context, HealthReport report)
    {
        var authorization = context.RequestServices.GetRequiredService<IAuthorizationService>();
        var status = report.Status.ToString();
        var body = (await authorization.AuthorizeAsync(context.User, Policies.System)).Succeeded
            ? JsonSerializer.Serialize(new { status, checks = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString()) })
            : JsonSerializer.Serialize(new { status });

        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(body);
    }

    /// <summary>
    /// <c>dotnet Elocaro.Web.dll healthcheck</c>: checks the running instance in the same container (the chiseled image
    /// has neither a shell nor curl). Exit code 0 = healthy.
    /// </summary>
    public static async Task<int> SelfCheckAsync(string[] args)
    {
        var port = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS")?.Split(';')[0] ?? "8080";
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            var path = args.Contains("--bereit") ? ReadyPath : LivenessPath;
            using var response = await http.GetAsync(new Uri($"http://127.0.0.1:{port}{path}"));
            return response.StatusCode == HttpStatusCode.OK ? 0 : 1;
        }
        catch (HttpRequestException)
        {
            return 1;
        }
        catch (TaskCanceledException)
        {
            return 1;
        }
    }
}
