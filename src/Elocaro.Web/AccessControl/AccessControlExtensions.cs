using Elocaro.Application.Accounts;
using Microsoft.AspNetCore.Authorization;

namespace Elocaro.Web.AccessControl;

internal static class AccessControlExtensions
{
    /// <summary>Policies for the areas and individual permissions (see docs/entwicklung.md → Autorisierung).</summary>
    public static AuthorizationBuilder AddElocaroPolicies(this AuthorizationBuilder builder)
    {
        builder
            .AddPolicy(Policies.Admin, p => p.RequireRole(Roles.Admin, Roles.Manager))
            .AddPolicy(Policies.Service, p => p.RequireRole(Roles.Caretaker, Roles.Tradesperson))
            .AddPolicy(Policies.Portal, p => p.RequireRole(Roles.Tenant))
            .AddPolicy(Policies.Owner, p => p.RequireRole(Roles.Owner));

        foreach (var permission in Permissions.All)
        {
            builder.AddPolicy(Policies.For(permission), p => p.RequireAssertion(k => HasPermission(k.User, permission)));
        }

        return builder;
    }

    /// <summary>Admins have all permissions, managers the ones assigned to them.</summary>
    public static bool HasPermission(System.Security.Claims.ClaimsPrincipal user, AccessRight permission) =>
        user.IsInRole(Roles.Admin)
        || (user.IsInRole(Roles.Manager) && user.HasClaim(Permissions.ClaimType, permission.ToString()));

    /// <summary>Start area per role.</summary>
    public static string HomePage(System.Security.Claims.ClaimsPrincipal user) =>
        user.IsInRole(Roles.Admin) || user.IsInRole(Roles.Manager) ? "/verwaltung"
        : user.IsInRole(Roles.Caretaker) || user.IsInRole(Roles.Tradesperson) ? "/service"
        : user.IsInRole(Roles.Tenant) ? "/portal"
        : user.IsInRole(Roles.Owner) ? "/eigentuemer"
        : "/konto/sicherheit";
}
