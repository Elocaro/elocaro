using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MietHahn.Application.Accounts;

namespace MietHahn.Infrastructure.Identity;

/// <summary>Additional claims: display name and whether two-factor is enabled (for the mandatory 2FA).</summary>
internal sealed class MietHahnClaimsFactory(UserManager<ApplicationUser> user, RoleManager<Role> roles, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser, Role>(user, roles, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(AccountClaims.DisplayName, string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email ?? "" : user.DisplayName));
        identity.AddClaim(new Claim(AccountClaims.TwoFactorEnabled, user.TwoFactorEnabled ? "1" : "0"));
        return identity;
    }
}

public static class AccountClaims
{
    public const string DisplayName = "miethahn:anzeigename";
    public const string TwoFactorEnabled = "miethahn:2fa";

    /// <summary>User in a role with mandatory 2FA who has not yet set up two-factor.</summary>
    public static bool MustSetUpTwoFactor(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.Identity?.IsAuthenticated == true
            && Roles.RequiringTwoFactor.Any(user.IsInRole)
            && user.FindFirstValue(TwoFactorEnabled) != "1";
    }
}

/// <summary>Blocked accounts may not sign in; successful sign-ins are recorded.</summary>
internal sealed class MietHahnSignInManager(
    UserManager<ApplicationUser> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<ApplicationUser>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<ApplicationUser> confirmation,
    TimeProvider clock)
    : SignInManager<ApplicationUser>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    public override async Task<bool> CanSignInAsync(ApplicationUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return !user.Locked && await base.CanSignInAsync(user);
    }

    public override async Task SignInWithClaimsAsync(ApplicationUser user, AuthenticationProperties? authenticationProperties, IEnumerable<Claim> additionalClaims)
    {
        ArgumentNullException.ThrowIfNull(user);
        await base.SignInWithClaimsAsync(user, authenticationProperties, additionalClaims);
        user.LastSignInAt = clock.GetUtcNow();
        await UserManager.UpdateAsync(user);
    }
}

/// <summary>Token provider for invitations (valid for 7 days, separate from the 2-hour password reset).</summary>
internal sealed class InvitationTokenProvider(
    Microsoft.AspNetCore.DataProtection.IDataProtectionProvider dataProtectionProvider,
    IOptions<InvitationTokenOptions> options,
    ILogger<DataProtectorTokenProvider<ApplicationUser>> logger)
    : DataProtectorTokenProvider<ApplicationUser>(dataProtectionProvider, options, logger)
{
}

/// <summary>Names of the token providers for Identity.</summary>
public static class TokenProviders
{
    /// <summary>Invitations (set password), valid for 7 days.</summary>
    public const string Invitation = "Einladung";
}

internal sealed class InvitationTokenOptions : DataProtectionTokenProviderOptions
{
    public InvitationTokenOptions()
    {
        Name = TokenProviders.Invitation;
        TokenLifespan = TimeSpan.FromDays(7);
    }
}
