using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Admin;
using MietHahn.Application.Documents;
using MietHahn.Domain.Common;
using MietHahn.Domain.Documents;
using MietHahn.Domain.Leases;
using MietHahn.Domain.Properties;
using MietHahn.Infrastructure.Common;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Documents;

internal sealed class DocumentReadiness(MietHahnDbContext db, ILandlordProfileService landlord, TimeProvider clock) : IDocumentReadiness
{
    public async Task<IReadOnlyList<ReadinessIssue>> CheckAsync(TemplateKind? kind = null, Guid? leaseId = null, CancellationToken cancellationToken = default)
    {
        var issues = new List<ReadinessIssue>();
        if (await landlord.GetAsync(cancellationToken) is null)
        {
            issues.Add(new(ReadinessIssueKind.LandlordProfile,
                "Es ist noch kein Vermieterprofil hinterlegt. Es wird für Briefkopf und Absender jedes Dokuments gebraucht.",
                "/verwaltung/einstellungen/vermieter", "Vermieterprofil anlegen", true));
        }

        if (kind is { } templateKind && !await db.Set<DocumentTemplate>().AsNoTracking().AnyAsync(t => t.Kind == templateKind, cancellationToken))
        {
            issues.Add(new(ReadinessIssueKind.Template,
                $"Es gibt noch keine Vorlage für {KindText(templateKind)}.",
                "/verwaltung/einstellungen/vorlagen", "Standardvorlagen anlegen", true));
        }

        if (leaseId is { } id && kind == TemplateKind.Contract && await MissingAreaAsync(id, cancellationToken) is { } unitName)
        {
            issues.Add(new(ReadinessIssueKind.LivingArea,
                $"Für {unitName} ist keine Wohnfläche hinterlegt; der Vertrag nennt dann keine Fläche.",
                $"/verwaltung/einheiten/{await UnitIdAsync(id, cancellationToken)}", "Fläche eintragen", false));
        }

        return issues;
    }

    private async Task<Guid> UnitIdAsync(Guid leaseId, CancellationToken cancellationToken) =>
        await db.Set<Lease>().AsNoTracking().Where(v => v.Id == leaseId).Select(v => v.UnitId).SingleAsync(cancellationToken);

    /// <summary>Name of the unit when it has no living area at the start of the lease or today; otherwise <c>null</c>.</summary>
    private async Task<string?> MissingAreaAsync(Guid leaseId, CancellationToken cancellationToken)
    {
        var lease = await db.Set<Lease>().AsNoTracking().SingleOrDefaultAsync(v => v.Id == leaseId, cancellationToken);
        if (lease is null)
        {
            return null;
        }

        var unit = await db.Set<Unit>().AsNoTracking().Include(e => e.Areas).Include(e => e.Room).ThenInclude(z => z.Areas).AsSplitQuery()
            .SingleOrDefaultAsync(e => e.Id == lease.UnitId, cancellationToken);
        if (unit is null)
        {
            return null;
        }

        var today = GermanTime.Today(clock.GetUtcNow());
        var start = lease.Start;
        // A shared flat room has its own area; the unit's living area is not needed for it.
        if (lease.RoomId is { } roomId)
        {
            var room = unit.Room.SingleOrDefault(z => z.Id == roomId);
            return room is not null && room.AreaOn(start) is null && room.AreaOn(today) is null ? $"das Zimmer „{room.Name}“" : null;
        }

        return unit.AreaOn(start) is null && unit.AreaOn(today) is null ? $"„{unit.Name}“" : null;
    }

    private static string KindText(TemplateKind kind) => kind switch
    {
        TemplateKind.Contract => "Mietverträge",
        TemplateKind.RentIncrease => "Mieterhöhungen",
        TemplateKind.Letter => "Schreiben",
        _ => "dieses Dokument",
    };
}
