using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MietHahn.Application.Konten;

namespace MietHahn.Infrastructure.Identitaet;

/// <summary>Zusätzliche Claims: Anzeigename und ob Zwei-Faktor aktiv ist (für die 2FA-Pflicht).</summary>
internal sealed class MietHahnClaimsFactory(UserManager<Benutzer> benutzer, RoleManager<Rolle> rollen, IOptions<IdentityOptions> optionen)
    : UserClaimsPrincipalFactory<Benutzer, Rolle>(benutzer, rollen, optionen)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(Benutzer user)
    {
        var identitaet = await base.GenerateClaimsAsync(user);
        identitaet.AddClaim(new Claim(KontoClaims.Anzeigename, string.IsNullOrWhiteSpace(user.Anzeigename) ? user.Email ?? "" : user.Anzeigename));
        identitaet.AddClaim(new Claim(KontoClaims.ZweiFaktorAktiv, user.TwoFactorEnabled ? "1" : "0"));
        return identitaet;
    }
}

public static class KontoClaims
{
    public const string Anzeigename = "miethahn:anzeigename";
    public const string ZweiFaktorAktiv = "miethahn:2fa";

    /// <summary>Benutzer einer Rolle mit 2FA-Pflicht, der Zwei-Faktor noch nicht eingerichtet hat.</summary>
    public static bool MussZweiFaktorEinrichten(ClaimsPrincipal benutzer)
    {
        ArgumentNullException.ThrowIfNull(benutzer);
        return benutzer.Identity?.IsAuthenticated == true
            && Rollen.MitZweiFaktorPflicht.Any(benutzer.IsInRole)
            && benutzer.FindFirstValue(ZweiFaktorAktiv) != "1";
    }
}

/// <summary>Gesperrte Konten dürfen sich nicht anmelden; erfolgreiche Anmeldungen werden vermerkt.</summary>
internal sealed class MietHahnSignInManager(
    UserManager<Benutzer> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<Benutzer> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<Benutzer>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<Benutzer> confirmation,
    TimeProvider uhr)
    : SignInManager<Benutzer>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    public override async Task<bool> CanSignInAsync(Benutzer user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return !user.Gesperrt && await base.CanSignInAsync(user);
    }

    public override async Task SignInWithClaimsAsync(Benutzer user, AuthenticationProperties? authenticationProperties, IEnumerable<Claim> additionalClaims)
    {
        ArgumentNullException.ThrowIfNull(user);
        await base.SignInWithClaimsAsync(user, authenticationProperties, additionalClaims);
        user.LetzteAnmeldungAm = uhr.GetUtcNow();
        await UserManager.UpdateAsync(user);
    }
}

/// <summary>Token-Anbieter für Einladungen (7 Tage gültig, getrennt vom Passwort-Reset mit 2 Stunden).</summary>
internal sealed class EinladungsTokenProvider(
    Microsoft.AspNetCore.DataProtection.IDataProtectionProvider dataProtectionProvider,
    IOptions<EinladungsTokenOptionen> options,
    ILogger<DataProtectorTokenProvider<Benutzer>> logger)
    : DataProtectorTokenProvider<Benutzer>(dataProtectionProvider, options, logger)
{
}

/// <summary>Namen der Token-Anbieter für Identity.</summary>
public static class TokenAnbieter
{
    /// <summary>Einladungen (Passwort festlegen), 7 Tage gültig.</summary>
    public const string Einladung = "Einladung";
}

internal sealed class EinladungsTokenOptionen : DataProtectionTokenProviderOptions
{
    public EinladungsTokenOptionen()
    {
        Name = TokenAnbieter.Einladung;
        TokenLifespan = TimeSpan.FromDays(7);
    }
}
