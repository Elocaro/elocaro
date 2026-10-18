using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using MietHahn.Application.Konten;
using MietHahn.Application.Objekte;

namespace MietHahn.Web.Autorisierung;

/// <summary>Anforderung „Objekt ansehen“ (ressourcenbasiert).</summary>
internal sealed class ObjektLesen : IAuthorizationRequirement
{
    public static readonly ObjektLesen Anforderung = new();
}

/// <summary>Ressource für <see cref="ObjektLesen"/>.</summary>
internal sealed record ObjektRessource(Guid ObjektId);

/// <summary>Verwaltung mit Recht Stammdaten sieht alle Objekte, Hausmeister nur die ihnen zugeordneten.</summary>
internal sealed class ObjektZugriffHandler(IObjektZuordnungen zuordnungen) : AuthorizationHandler<ObjektLesen, ObjektRessource>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, ObjektLesen requirement, ObjektRessource resource)
    {
        if (AutorisierungErweiterungen.HatRecht(context.User, Recht.Stammdaten))
        {
            context.Succeed(requirement);
            return;
        }

        if (context.User.IsInRole(Rollen.Hausmeister)
            && Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var benutzerId)
            && await zuordnungen.IstZugeordnetAsync(benutzerId, resource.ObjektId))
        {
            context.Succeed(requirement);
        }
    }
}
