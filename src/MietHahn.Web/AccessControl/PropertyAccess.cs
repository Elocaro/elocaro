using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using MietHahn.Application.Accounts;
using MietHahn.Application.Properties;

namespace MietHahn.Web.AccessControl;

/// <summary>Anforderung „Objekt ansehen“ (ressourcenbasiert).</summary>
internal sealed class PropertyReadRequirement : IAuthorizationRequirement
{
    public static readonly PropertyReadRequirement Requirement = new();
}

/// <summary>Ressource für <see cref="PropertyReadRequirement"/>.</summary>
internal sealed record PropertyResource(Guid PropertyId);

/// <summary>Verwaltung mit Recht Stammdaten sieht alle Objekte, Hausmeister nur die ihnen zugeordneten.</summary>
internal sealed class PropertyAccessHandler(IPropertyAssignments assignments) : AuthorizationHandler<PropertyReadRequirement, PropertyResource>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PropertyReadRequirement requirement, PropertyResource resource)
    {
        if (AccessControlExtensions.HasPermission(context.User, AccessRight.MasterData))
        {
            context.Succeed(requirement);
            return;
        }

        if (context.User.IsInRole(Roles.Caretaker)
            && Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            && await assignments.IsAssignedAsync(userId, resource.PropertyId))
        {
            context.Succeed(requirement);
        }
    }
}
