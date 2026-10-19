using Microsoft.AspNetCore.Authorization;
using MietHahn.Application.Accounts;

namespace MietHahn.Web.AccessControl;

internal static class AccessControlExtensions
{
    /// <summary>Richtlinien für die Bereiche und Einzelrechte (siehe docs/entwicklung.md → Autorisierung).</summary>
    public static AuthorizationBuilder AddMietHahnPolicies(this AuthorizationBuilder builder)
    {
        builder
            .AddPolicy(Policies.Admin, p => p.RequireRole(Roles.Admin, Roles.Manager))
            .AddPolicy(Policies.Service, p => p.RequireRole(Roles.Caretaker, Roles.Tradesperson))
            .AddPolicy(Policies.Portal, p => p.RequireRole(Roles.Tenant));

        foreach (var permission in Permissions.All)
        {
            builder.AddPolicy(Policies.For(permission), p => p.RequireAssertion(k => HasPermission(k.User, permission)));
        }

        return builder;
    }

    /// <summary>Admins haben alle Rechte, Verwalter die ihnen zugewiesenen.</summary>
    public static bool HasPermission(System.Security.Claims.ClaimsPrincipal user, AccessRight permission) =>
        user.IsInRole(Roles.Admin)
        || (user.IsInRole(Roles.Manager) && user.HasClaim(Permissions.ClaimType, permission.ToString()));

    /// <summary>Startbereich je Rolle.</summary>
    public static string HomePage(System.Security.Claims.ClaimsPrincipal user) =>
        user.IsInRole(Roles.Admin) || user.IsInRole(Roles.Manager) ? "/verwaltung"
        : user.IsInRole(Roles.Caretaker) || user.IsInRole(Roles.Tradesperson) ? "/service"
        : user.IsInRole(Roles.Tenant) ? "/portal"
        : "/konto/sicherheit";
}
