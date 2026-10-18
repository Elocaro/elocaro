using Microsoft.AspNetCore.Authorization;
using MietHahn.Application.Konten;

namespace MietHahn.Web.Autorisierung;

internal static class AutorisierungErweiterungen
{
    /// <summary>Richtlinien für die Bereiche und Einzelrechte (siehe docs/entwicklung.md → Autorisierung).</summary>
    public static AuthorizationBuilder AddMietHahnRichtlinien(this AuthorizationBuilder builder)
    {
        builder
            .AddPolicy(Richtlinien.Verwaltung, p => p.RequireRole(Rollen.Admin, Rollen.Verwalter))
            .AddPolicy(Richtlinien.Service, p => p.RequireRole(Rollen.Hausmeister, Rollen.Handwerker))
            .AddPolicy(Richtlinien.Portal, p => p.RequireRole(Rollen.Mieter));

        foreach (var recht in Rechte.Alle)
        {
            builder.AddPolicy(Richtlinien.Fuer(recht), p => p.RequireAssertion(k => HatRecht(k.User, recht)));
        }

        return builder;
    }

    /// <summary>Admins haben alle Rechte, Verwalter die ihnen zugewiesenen.</summary>
    public static bool HatRecht(System.Security.Claims.ClaimsPrincipal benutzer, Recht recht) =>
        benutzer.IsInRole(Rollen.Admin)
        || (benutzer.IsInRole(Rollen.Verwalter) && benutzer.HasClaim(Rechte.ClaimTyp, recht.ToString()));

    /// <summary>Startbereich je Rolle.</summary>
    public static string Startseite(System.Security.Claims.ClaimsPrincipal benutzer) =>
        benutzer.IsInRole(Rollen.Admin) || benutzer.IsInRole(Rollen.Verwalter) ? "/verwaltung"
        : benutzer.IsInRole(Rollen.Hausmeister) || benutzer.IsInRole(Rollen.Handwerker) ? "/service"
        : benutzer.IsInRole(Rollen.Mieter) ? "/portal"
        : "/konto/sicherheit";
}
