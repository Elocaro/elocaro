using System.Net;
using System.Threading.RateLimiting;
using Elocaro.Application.Admin;
using Elocaro.Infrastructure.Identity;
using Elocaro.Web.AccessControl;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Elocaro.Web.Account;

internal static class AccountExtensions
{
    public const string AccountPath = "/konto";
    public const string TwoFactorSetupPath = "/konto/sicherheit/zwei-faktor";

    public static IServiceCollection AddElocaroSignIn(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var signIn = configuration.GetSection(SignInOptions.Section).Get<SignInOptions>() ?? new SignInOptions();
        services.Configure<SignInOptions>(configuration.GetSection(SignInOptions.Section));
        var mcp = configuration.GetSection(Elocaro.Application.Accounts.McpOptions.Section).Get<Elocaro.Application.Accounts.McpOptions>() ?? new();

        services.AddCascadingAuthenticationState();
        services.AddScoped<Redirector>();

        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();

        // Personal access tokens for integrations; only endpoints that name this scheme accept them (see docs/mcp.md).
        services.AddAuthentication().AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, AccessTokenAuthenticationHandler>(AccessTokenAuthenticationHandler.SchemeName, null);
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

        foreach (var scheme in new[] { IdentityConstants.TwoFactorUserIdScheme, IdentityConstants.TwoFactorRememberMeScheme, IdentityConstants.ExternalScheme })
        {
            services.Configure<CookieAuthenticationOptions>(scheme, o => o.Cookie.SecurePolicy = CookieSecurePolicy.Always);
        }

        // Unlike the sign-in cookies, antiforgery answers plain http with an error under "Always". Production always has https
        // (reported by the proxy); development and the browser tests also run on http://localhost.
        services.AddAntiforgery(o => o.Cookie.SecurePolicy = environment.IsProduction() ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest);

        // Validate the security stamp on every request (uncritical for the small number of users): locked accounts are
        // signed out immediately, changed roles and permissions apply from the next request (the principal is rebuilt).
        services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero);

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().AddRequirements(new AuthenticatedOrSetupWindowRequirement()).Build())
            .AddElocaroPolicies();
        services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, AuthenticatedOrSetupWindowHandler>();
        services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, FileAccessHandler>();
        services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, PropertyAccessHandler>();

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = (int)HttpStatusCode.TooManyRequests;
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var address = context.Connection.RemoteIpAddress?.ToString() ?? "unbekannt";

                // Per client address, not per token: partitions per presented token could be created without limit by anyone.
                if (context.Request.Path.StartsWithSegments(Mcp.McpExtensions.Path, StringComparison.OrdinalIgnoreCase))
                {
                    return RateLimitPartition.GetFixedWindowLimiter(
                        "mcp:" + address,
                        _ => new FixedWindowRateLimiterOptions { PermitLimit = mcp.RequestsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
                }

                return HttpMethods.IsPost(context.Request.Method) && context.Request.Path.StartsWithSegments(AccountPath, StringComparison.OrdinalIgnoreCase)
                    ? RateLimitPartition.GetFixedWindowLimiter(
                        address,
                        _ => new FixedWindowRateLimiterOptions { PermitLimit = signIn.RequestsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })
                    : RateLimitPartition.GetNoLimiter("frei");
            });
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
                && !IsStaticFile(context))
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

    /// <summary>
    /// Decides by the matched endpoint, not by the look of the path: exports such as <c>offene-posten.csv</c> also end in a file
    /// extension. Routing runs before this middleware (WebApplication adds it to the start of the pipeline); without a
    /// matched endpoint a request counts as not static, so it is redirected.
    /// </summary>
    private static bool IsStaticFile(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<StaticFileEndpoint>() is not null
        || context.Request.Path.StartsWithSegments("/_framework", StringComparison.Ordinal)
        || context.Request.Path.StartsWithSegments("/_content", StringComparison.Ordinal);

    /// <summary>Marks the endpoints of the static assets (see <c>MapStaticAssets</c> in Program.cs).</summary>
    public sealed class StaticFileEndpoint;
}
