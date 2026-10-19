using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using MietHahn.Infrastructure.Identity;
using MietHahn.Web.AccessControl;

namespace MietHahn.Web.Account;

internal static class AccountExtensions
{
    public const string AccountPath = "/konto";
    public const string TwoFactorSetupPath = "/konto/sicherheit/zwei-faktor";

    public static IServiceCollection AddMietHahnSignIn(this IServiceCollection services, IConfiguration configuration)
    {
        var signIn = configuration.GetSection(SignInOptions.Section).Get<SignInOptions>() ?? new SignInOptions();
        services.Configure<SignInOptions>(configuration.GetSection(SignInOptions.Section));

        services.AddCascadingAuthenticationState();
        services.AddScoped<Redirector>();

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
            o.ExpireTimeSpan = TimeSpan.FromDays(signIn.SessionDays);
            o.SlidingExpiration = true;
        });

        // Security-Stamp bei jeder Anfrage prüfen (für die kleine Nutzerzahl unkritisch): Gesperrte Konten werden
        // sofort abgemeldet, geänderte Rollen und Rechte gelten ab der nächsten Anfrage (Principal wird neu aufgebaut).
        services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero);

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddMietHahnPolicies();
        services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, FileAccessHandler>();
        services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, PropertyAccessHandler>();

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = (int)HttpStatusCode.TooManyRequests;
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                HttpMethods.IsPost(context.Request.Method) && context.Request.Path.StartsWithSegments(AccountPath, StringComparison.OrdinalIgnoreCase)
                    ? RateLimitPartition.GetFixedWindowLimiter(
                        context.Connection.RemoteIpAddress?.ToString() ?? "unbekannt",
                        _ => new FixedWindowRateLimiterOptions { PermitLimit = signIn.RequestsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })
                    : RateLimitPartition.GetNoLimiter("frei"));
        });

        return services;
    }

    /// <summary>Admin und Verwalter müssen Zwei-Faktor einrichten, bevor sie etwas anderes tun können.</summary>
    public static IApplicationBuilder UseTwoFactorRequirement(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (AccountClaims.MustSetUpTwoFactor(context.User)
                && !context.Request.Path.StartsWithSegments(AccountPath, StringComparison.OrdinalIgnoreCase)
                && !IsStaticFile(context.Request.Path))
            {
                context.Response.Redirect(TwoFactorSetupPath);
                return;
            }

            await next(context);
        });

    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/konto/abmelden", async (HttpContext context, SignInManager<ApplicationUser> signIn, IAntiforgery antiforgery) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            await signIn.SignOutAsync();
            return TypedResults.LocalRedirect("~/konto/anmelden?abgemeldet=true");
        });
        return endpoints;
    }

    private static bool IsStaticFile(PathString path) =>
        path.StartsWithSegments("/_framework", StringComparison.Ordinal)
        || path.StartsWithSegments("/_content", StringComparison.Ordinal)
        || Path.HasExtension(path.Value);
}
