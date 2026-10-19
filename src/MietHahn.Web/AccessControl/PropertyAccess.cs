using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using MietHahn.Application.Accounts;
using MietHahn.Application.Properties;

namespace MietHahn.Web.AccessControl;

/// <summary>Requirement "view property" (resource-based).</summary>
internal sealed class PropertyReadRequirement : IAuthorizationRequirement
{
    public static readonly PropertyReadRequirement Requirement = new();
}

/// <summary>Resource for <see cref="PropertyReadRequirement"/>.</summary>
internal sealed record PropertyResource(Guid PropertyId);

/// <summary>Administrators with the master data permission see all properties, caretakers only those assigned to them.</summary>
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
