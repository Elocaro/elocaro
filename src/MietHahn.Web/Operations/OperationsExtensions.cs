using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MietHahn.Infrastructure.Operations;

namespace MietHahn.Web.Operations;

internal static class OperationsExtensions
{
    public const string LivenessPath = "/health/live";
    public const string ReadyPath = "/health";

    /// <summary>Hinter einem Reverse-Proxy (Caddy) Schema und Client-IP aus X-Forwarded-* übernehmen.</summary>
    public static IServiceCollection AddMietHahnOperations(this IServiceCollection services) =>
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            o.ForwardLimit = 1;

            // Die App ist nur im Docker-Netz erreichbar; dort sitzt ausschließlich der Proxy.
            o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("10.0.0.0/8"));
            o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("172.16.0.0/12"));
            o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("192.168.0.0/16"));
        });

    /// <summary>Sicherheits-Header für alle Antworten (CSP passend zu statischem Blazor-SSR).</summary>
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
                header.ContentSecurityPolicy =
                    "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
                    "font-src 'self'; connect-src 'self'; frame-ancestors 'none'; form-action 'self'; base-uri 'self'; object-src 'none'";
            }

            await next(context);
        });

    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Lebt: Prozess antwortet (für den Container-Healthcheck).
        endpoints.MapHealthChecks(LivenessPath, new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();

        // Bereit: Datenbank und Gotenberg erreichbar. Ausgabe ohne Fehlerdetails (keine Interna nach außen).
        endpoints.MapHealthChecks(ReadyPath, new HealthCheckOptions
        {
            Predicate = c => c.Tags.Contains(HealthTags.Ready),
            ResponseWriter = async (context, report) =>
            {
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    status = report.Status.ToString(),
                    checks = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString()),
                }));
            },
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status200OK,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
            },
        }).AllowAnonymous();

        return endpoints;
    }

    /// <summary>
    /// <c>dotnet MietHahn.Web.dll healthcheck</c>: prüft die laufende Instanz im selben Container (das chiseled Image
    /// hat weder Shell noch curl). Exit-Code 0 = gesund.
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
