using System.Security.Claims;
using Elocaro.Application.Accounts;
using Elocaro.Application.Admin;
using Elocaro.Application.Todos;
using Elocaro.Domain.Bank;
using Elocaro.Domain.Leases;
using Elocaro.Domain.People;
using Elocaro.Domain.Properties;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Setup;

/// <summary>
/// Dashboard: the first steps of a new installation (landlord profile, property, units with living area, person, lease, bank account).
/// The first open step is due soon, the rest are information; once everything is done the checklist disappears.
/// </summary>
internal sealed class SetupChecklistTodos(ElocaroDbContext db, ILandlordProfileService landlord, IModuleState modules) : ITodoSource
{
    public async Task<IReadOnlyList<TodoItem>> TodosAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.IsInRole(Roles.Admin) && !user.HasClaim(Permissions.ClaimType, nameof(AccessRight.MasterData)))
        {
            return [];
        }

        var steps = new List<(bool Done, string Title, string Link, string Hint)>();
        if (user.IsInRole(Roles.Admin) || user.HasClaim(Permissions.ClaimType, nameof(AccessRight.System)))
        {
            steps.Add((await landlord.GetAsync(cancellationToken) is not null, "Vermieterprofil anlegen", "/verwaltung/einstellungen/vermieter",
                "Name und Anschrift erscheinen in Briefkopf und Fußzeile aller Dokumente."));
        }

        var property = await db.Set<Property>().AsNoTracking().OrderBy(p => p.CreatedAt).Select(p => new { p.Id }).FirstOrDefaultAsync(cancellationToken);
        steps.Add((property is not null, "Erstes Objekt anlegen", "/verwaltung/objekte/neu", "Das Haus mit Anschrift; Wohnungen und Zimmer kommen danach. Haben Sie schon eine Tabelle, übernehmen Sie sie unter Einstellungen → Daten importieren."));

        var units = await db.Set<Unit>().AsNoTracking().Include(e => e.Areas).Include(e => e.Room).ToListAsync(cancellationToken);
        var firstUnit = units.OrderBy(e => e.CreatedAt).FirstOrDefault();
        steps.Add((units.Count > 0, "Erste Einheit anlegen", property is null ? "/verwaltung/objekte/neu" : $"/verwaltung/objekte/{property.Id}",
            "Eine Wohnung oder ein Stellplatz im Objekt."));

        var withoutArea = units.Where(e => !e.IsSharedFlat && e.Areas.Count == 0).ToList();
        steps.Add((units.Count > 0 && withoutArea.Count == 0,
            withoutArea.Count > 1 ? $"Wohnfläche eintragen ({withoutArea.Count} Einheiten)" : "Wohnfläche eintragen",
            withoutArea.FirstOrDefault() is { } first ? $"/verwaltung/einheiten/{first.Id}" : firstUnit is null ? "/verwaltung/objekte" : $"/verwaltung/einheiten/{firstUnit.Id}",
            "Die Fläche braucht die Nebenkostenabrechnung und der Mietvertrag."));

        steps.Add((await db.Set<Person>().AsNoTracking().AnyAsync(p => p.AnonymizedAt == null, cancellationToken), "Ersten Mieter anlegen", "/verwaltung/personen/neu",
            "Name und E-Mail genügen; alles Weitere lässt sich später ergänzen."));
        steps.Add((await db.Set<Lease>().AsNoTracking().AnyAsync(cancellationToken), "Ersten Mietvertrag anlegen", "/verwaltung/vertraege/neu",
            "Laufzeit, Mieter und Miete; daraus entstehen Mieterkonto und Vertragsdokument."));
        if (modules.IsEnabled(AppModule.Bank) && (user.IsInRole(Roles.Admin) || user.HasClaim(Permissions.ClaimType, nameof(AccessRight.Finance))))
        {
            steps.Add((await db.Set<BankAccount>().AsNoTracking().AnyAsync(cancellationToken), "Bankkonto anlegen", "/verwaltung/bank/konten/neu",
                "Damit Mieteingänge automatisch zugeordnet werden. Wer das nicht braucht, schaltet das Modul „Bank“ unter Einstellungen → Module ab."));
        }

        if (steps.All(s => s.Done))
        {
            return [];
        }

        var done = steps.Count(s => s.Done);
        var result = new List<TodoItem>();
        var firstOpen = true;
        foreach (var (isDone, title, link, hint) in steps)
        {
            if (isDone)
            {
                continue;
            }

            result.Add(new TodoItem(TodoAreas.Setup, title, link, firstOpen ? Urgency.Soon : Urgency.Info, hint, Progress: new TodoProgress(done, steps.Count)));
            firstOpen = false;
        }

        return result;
    }
}
