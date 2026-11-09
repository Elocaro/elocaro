using System.Text.Encodings.Web;
using Elocaro.Application.Accounts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Elocaro.Web.AccessControl;

/// <summary>
/// Authenticates an endpoint by a personal access token (<c>Authorization: Bearer elo_…</c>). The sign-in cookie does
/// not count here: an endpoint that names this scheme is only reached with a token, which also rules out cross-site requests with the cookie.
/// </summary>
internal sealed class AccessTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IAccessTokens tokens)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ElocaroAccessToken";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var principal = await tokens.AuthenticateAsync(header[prefix.Length..].Trim(), Context.RequestAborted);
        return principal is null
            ? AuthenticateResult.Fail("Das Zugriffstoken ist ungültig, abgelaufen oder widerrufen.")
            : AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer realm=\"elocaro\"";
        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}
