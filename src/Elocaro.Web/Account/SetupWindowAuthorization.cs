using Elocaro.Application.Accounts;
using Microsoft.AspNetCore.Authorization;

namespace Elocaro.Web.Account;

/// <summary>
/// The fallback policy: signed-in users. In addition, while the installation has no account yet (the window of the initial setup),
/// the connection of interactive pages (<c>/_blazor</c>) is open to anyone, so that the setup can offer an interactive page (restore
/// from a backup). Pages still decide about access themselves; every page with <c>[Authorize]</c> stays closed.
/// </summary>
internal sealed class AuthenticatedOrSetupWindowRequirement : IAuthorizationRequirement;

internal sealed class AuthenticatedOrSetupWindowHandler(IAccountService accounts) : AuthorizationHandler<AuthenticatedOrSetupWindowRequirement>
{
    private static readonly PathString BlazorPath = new("/_blazor");

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AuthenticatedOrSetupWindowRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            context.Succeed(requirement);
            return;
        }

        if (context.Resource is HttpContext http && http.Request.Path.StartsWithSegments(BlazorPath, StringComparison.OrdinalIgnoreCase) && await accounts.InitialSetupPossibleAsync(http.RequestAborted))
        {
            context.Succeed(requirement);
        }
    }
}
