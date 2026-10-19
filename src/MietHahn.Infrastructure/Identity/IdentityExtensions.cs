using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MietHahn.Application.Accounts;

namespace MietHahn.Infrastructure.Identity;

/// <summary>Zusätzliche Claims: Anzeigename und ob Zwei-Faktor aktiv ist (für die 2FA-Pflicht).</summary>
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

    /// <summary>Benutzer einer Rolle mit 2FA-Pflicht, der Zwei-Faktor noch nicht eingerichtet hat.</summary>
    public static bool MustSetUpTwoFactor(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.Identity?.IsAuthenticated == true
            && Roles.RequiringTwoFactor.Any(user.IsInRole)
            && user.FindFirstValue(TwoFactorEnabled) != "1";
    }
}

/// <summary>Gesperrte Konten dürfen sich nicht anmelden; erfolgreiche Anmeldungen werden vermerkt.</summary>
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

/// <summary>Token-Anbieter für Einladungen (7 Tage gültig, getrennt vom Passwort-Reset mit 2 Stunden).</summary>
internal sealed class InvitationTokenProvider(
    Microsoft.AspNetCore.DataProtection.IDataProtectionProvider dataProtectionProvider,
    IOptions<InvitationTokenOptions> options,
    ILogger<DataProtectorTokenProvider<ApplicationUser>> logger)
    : DataProtectorTokenProvider<ApplicationUser>(dataProtectionProvider, options, logger)
{
}

/// <summary>Namen der Token-Anbieter für Identity.</summary>
public static class TokenProviders
{
    /// <summary>Einladungen (Passwort festlegen), 7 Tage gültig.</summary>
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
