using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using MietHahn.Infrastructure.Identitaet;

namespace MietHahn.Web.Konto;

internal static class KontoErweiterungen
{
    public const string KontoPfad = "/konto";
    public const string ZweiFaktorEinrichtenPfad = "/konto/sicherheit/zwei-faktor";

    public static IServiceCollection AddMietHahnAnmeldung(this IServiceCollection services, IConfiguration konfiguration)
    {
        var anmeldung = konfiguration.GetSection(AnmeldungOptionen.Abschnitt).Get<AnmeldungOptionen>() ?? new AnmeldungOptionen();
        services.Configure<AnmeldungOptionen>(konfiguration.GetSection(AnmeldungOptionen.Abschnitt));

        services.AddCascadingAuthenticationState();
        services.AddScoped<Weiterleitung>();

        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        services.ConfigureApplicationCookie(o =>
        {
            o.Cookie.Name = "miethahn";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            o.LoginPath = "/konto/anmelden";
            o.LogoutPath = "/konto/abmelden";
            o.AccessDeniedPath = "/konto/zugriff-verweigert";
            o.ExpireTimeSpan = TimeSpan.FromDays(anmeldung.SitzungTage);
            o.SlidingExpiration = true;
        });

        // Gesperrte Konten und geänderte Rollen/2FA werden spätestens nach 10 Minuten wirksam.
        services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(10));

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = (int)HttpStatusCode.TooManyRequests;
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(kontext =>
                HttpMethods.IsPost(kontext.Request.Method) && kontext.Request.Path.StartsWithSegments(KontoPfad, StringComparison.OrdinalIgnoreCase)
                    ? RateLimitPartition.GetFixedWindowLimiter(
                        kontext.Connection.RemoteIpAddress?.ToString() ?? "unbekannt",
                        _ => new FixedWindowRateLimiterOptions { PermitLimit = anmeldung.AnfragenProMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })
                    : RateLimitPartition.GetNoLimiter("frei"));
        });

        return services;
    }

    /// <summary>Admin und Verwalter müssen Zwei-Faktor einrichten, bevor sie etwas anderes tun können.</summary>
    public static IApplicationBuilder UseZweiFaktorPflicht(this IApplicationBuilder app) =>
        app.Use(async (kontext, weiter) =>
        {
            if (KontoClaims.MussZweiFaktorEinrichten(kontext.User)
                && !kontext.Request.Path.StartsWithSegments(KontoPfad, StringComparison.OrdinalIgnoreCase)
                && !IstStatischeDatei(kontext.Request.Path))
            {
                kontext.Response.Redirect(ZweiFaktorEinrichtenPfad);
                return;
            }

            await weiter(kontext);
        });

    public static IEndpointRouteBuilder MapKontoEndpunkte(this IEndpointRouteBuilder endpunkte)
    {
        endpunkte.MapPost("/konto/abmelden", async (HttpContext kontext, SignInManager<Benutzer> anmeldung, IAntiforgery antiforgery) =>
        {
            await antiforgery.ValidateRequestAsync(kontext);
            await anmeldung.SignOutAsync();
            return TypedResults.LocalRedirect("~/konto/anmelden?abgemeldet=1");
        });
        return endpunkte;
    }

    private static bool IstStatischeDatei(PathString pfad) =>
        pfad.StartsWithSegments("/_framework", StringComparison.Ordinal)
        || pfad.StartsWithSegments("/_content", StringComparison.Ordinal)
        || Path.HasExtension(pfad.Value);
}
