using System.Net;
using System.Threading.RateLimiting;
using Elocaro.Application.Setup;
using Elocaro.Infrastructure.Identity;
using Elocaro.Web.AccessControl;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Elocaro.Web.Account;

internal static class AccountExtensions
{
    public const string AccountPath = "/konto";
    public const string TwoFactorSetupPath = "/konto/sicherheit/zwei-faktor";

    public static IServiceCollection AddElocaroSignIn(this IServiceCollection services, IConfiguration configuration)
    {
        var signIn = configuration.GetSection(SignInOptions.Section).Get<SignInOptions>() ?? new SignInOptions();
        services.Configure<SignInOptions>(configuration.GetSection(SignInOptions.Section));

        services.AddCascadingAuthenticationState();
        services.AddScoped<Redirector>();

        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        services.ConfigureApplicationCookie(o =>
        {
            o.Cookie.Name = "elocaro";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            o.LoginPath = "/konto/anmelden";
            o.LogoutPath = "/konto/abmelden";
            o.AccessDeniedPath = "/konto/zugriff-verweigert";
            o.ExpireTimeSpan = TimeSpan.FromDays(signIn.SessionDays);
            o.SlidingExpiration = true;
        });

        // Validate the security stamp on every request (uncritical for the small number of users): locked accounts are
        // signed out immediately, changed roles and permissions apply from the next request (the principal is rebuilt).
        services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero);

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddElocaroPolicies();
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

    /// <summary>Admins and managers must set up two-factor authentication before they can do anything else.</summary>
    public static IApplicationBuilder UseTwoFactorRequirement(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            // A public demo instance has no two-factor authentication (the password is published on the sign-in page).
            if (AccountClaims.MustSetUpTwoFactor(context.User)
                && !context.RequestServices.GetRequiredService<IOptions<DemoOptions>>().Value.Enabled
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

        endpoints.MapPost("/konto/darstellung", async (HttpContext context, IAntiforgery antiforgery) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            var form = await context.Request.ReadFormAsync();
            ThemePreference.Write(context, form["theme"].ToString());
            return TypedResults.LocalRedirect("~" + Redirector.LocalTarget(form["returnUrl"].ToString()));
        });
        return endpoints;
    }

    private static bool IsStaticFile(PathString path) =>
        path.StartsWithSegments("/_framework", StringComparison.Ordinal)
        || path.StartsWithSegments("/_content", StringComparison.Ordinal)
        || Path.HasExtension(path.Value);
}
