using System.Security.Claims;
using Elocaro.Infrastructure.Identity;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Elocaro.Web.Account;

/// <summary>
/// A circuit does not see the cookie validation of later requests: check the security stamp regularly, so locked accounts
/// and changed roles take effect in open pages too (the cookie middleware does the same on every request).
/// </summary>
internal sealed class RevalidatingAuthenticationStateProvider(
    ILoggerFactory loggerFactory,
    IServiceScopeFactory scopes,
    IOptions<IdentityOptions> options) : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(5);

    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState authenticationState, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await manager.GetUserAsync(authenticationState.User);
        if (user is null || !manager.SupportsUserSecurityStamp)
        {
            return false;
        }

        var principalStamp = authenticationState.User.FindFirstValue(options.Value.ClaimsIdentity.SecurityStampClaimType);
        return principalStamp == await manager.GetSecurityStampAsync(user);
    }
}
