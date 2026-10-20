using Microsoft.Extensions.Caching.Memory;
using MietHahn.Application.Accounts;

namespace MietHahn.Web.Documents;

/// <summary>Holds a rendered preview PDF for a few minutes so that the editor can show it in a frame.</summary>
internal sealed class TemplatePreviews(IMemoryCache cache)
{
    public Guid Add(byte[] pdf)
    {
        var id = Guid.NewGuid();
        cache.Set(id, pdf, TimeSpan.FromMinutes(10));
        return id;
    }

    public byte[]? Find(Guid id) => cache.TryGetValue(id, out byte[]? pdf) ? pdf : null;
}

internal static class TemplatePreviewEndpoint
{
    public static IServiceCollection AddTemplatePreviews(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton<TemplatePreviews>();
        return services;
    }

    public static IEndpointRouteBuilder MapTemplatePreviewEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/verwaltung/einstellungen/vorlagen/vorschau/{id:guid}.pdf", (Guid id, TemplatePreviews previews, HttpContext context) =>
        {
            if (previews.Find(id) is not { } pdf)
            {
                return Results.NotFound();
            }

            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; frame-ancestors 'self'";
            context.Response.Headers.XFrameOptions = "SAMEORIGIN";
            context.Response.Headers.CacheControl = "private, no-store";
            return Results.File(pdf, "application/pdf");
        }).RequireAuthorization(Policies.System);
        return endpoints;
    }
}
