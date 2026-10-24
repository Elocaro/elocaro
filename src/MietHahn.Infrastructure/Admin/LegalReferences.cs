using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Accounts;
using MietHahn.Application.Admin;
using MietHahn.Application.Todos;
using MietHahn.Domain.Common;
using MietHahn.Domain.Heating;
using MietHahn.Domain.Leases;
using MietHahn.Infrastructure.Common;
using MietHahn.Infrastructure.Finance;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Admin;

internal sealed class LegalReferences(MietHahnDbContext db, IModuleState modules, TimeProvider clock) : ILegalReferences
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public async Task<IReadOnlyList<LegalReference>> OverviewAsync(CancellationToken cancellationToken = default)
    {
        var today = GermanTime.Today(clock.GetUtcNow());
        var result = new List<LegalReference>();

        if (modules.IsEnabled(AppModule.Dunning) && modules.IsOptionEnabled(AppOption.DunningInterest))
        {
            result.Add(await BaseRateAsync(today, cancellationToken));
        }

        if (modules.IsEnabled(AppModule.RentIncreases) && modules.IsOptionEnabled(AppOption.RentModelIndex))
        {
            result.Add(await CpiAsync(today, cancellationToken));
        }

        if (modules.IsEnabled(AppModule.OperatingCosts))
        {
            result.Add(Carbon(today));
            result.Add(DegreeDays(today));
        }

        return result;
    }

    /// <summary>The Basiszinssatz (§ 247 BGB) changes on 1 January and 1 July.</summary>
    private async Task<LegalReference> BaseRateAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var settings = await FinanceSettingsService.GetOrCreateAsync(db, cancellationToken);
        var halfYearStart = new DateOnly(today.Year, today.Month <= 6 ? 1 : 7, 1);
        var next = halfYearStart.AddMonths(6);
        const string source = "Deutsche Bundesbank, Basiszinssatz nach § 247 BGB (bundesbank.de, „Basiszinssatz“); ändert sich zum 1.1. und 1.7.";
        const string link = "/verwaltung/finanzen/einstellungen";

        if (settings.BaseRate is not { } rate)
        {
            return new("base-rate", "Basiszinssatz (Verzugszinsen)", null, null, source, LegalReferenceStatus.Missing, halfYearStart, link,
                "Verzugszinsen sind eingeschaltet, aber ohne Basiszinssatz werden keine berechnet. Den aktuellen Wert tragen Sie unter Forderungen → Einstellungen ein.");
        }

        var current = settings.BaseRateAsOf is { } asOf && asOf >= halfYearStart;
        return new("base-rate", "Basiszinssatz (Verzugszinsen)", $"{rate.ToString("0.##", German)} % p. a.",
            settings.BaseRateAsOf is { } since ? $"gültig seit {since.ToString("dd.MM.yyyy", German)}" : "ohne Datum", source,
            current ? LegalReferenceStatus.Current : LegalReferenceStatus.Check, next, link,
            current ? null : $"Der Wert stammt nicht aus dem laufenden Halbjahr (seit {halfYearStart.ToString("dd.MM.yyyy", German)}). Aktuellen Satz bei der Bundesbank nachsehen und eintragen.");
    }

    /// <summary>The index is published monthly, about the middle of the following month.</summary>
    private async Task<LegalReference> CpiAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var newest = await db.Set<CpiValue>().AsNoTracking().OrderByDescending(v => v.Month).FirstOrDefaultAsync(cancellationToken);
        const string source = "Statistisches Bundesamt (Destatis), Verbraucherpreisindex Deutschland, Tabelle 61111-0002; Veröffentlichung etwa Mitte des Folgemonats";
        const string link = "/verwaltung/vpi";
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        // Published by now: last month's value from the 16th on, otherwise the one before.
        var expected = today.Day >= 16 ? thisMonth.AddMonths(-1) : thisMonth.AddMonths(-2);
        var nextCheck = today.Day >= 16 ? thisMonth.AddMonths(1).AddDays(15) : new DateOnly(today.Year, today.Month, 16);

        if (newest is null)
        {
            return new("cpi", "Verbraucherpreisindex (Indexmieten)", null, null, source, LegalReferenceStatus.Missing, nextCheck, link,
                "Für Indexmieten braucht MietHahn die Indexwerte. Sie lassen sich auf der Seite Verbraucherpreisindex eintragen.");
        }

        var current = newest.Month >= expected;
        return new("cpi", "Verbraucherpreisindex (Indexmieten)", $"{newest.Value.ToString("0.0", German)} (Basis 2020 = 100)",
            $"Stand {newest.Month.ToString("MM/yyyy", CultureInfo.InvariantCulture)}", source, current ? LegalReferenceStatus.Current : LegalReferenceStatus.Check, nextCheck, link,
            current ? null : $"Der neueste Wert ist {newest.Month.ToString("MM/yyyy", CultureInfo.InvariantCulture)}; erwartet wäre {expected.ToString("MM/yyyy", CultureInfo.InvariantCulture)}. Neuen Wert bei Destatis nachsehen und eintragen.");
    }

    private static LegalReference Carbon(DateOnly today)
    {
        var table = CarbonCostTables.For(today.Year);
        return table is null
            ? new("carbon", "CO₂-Kostenaufteilung (Stufenmodell)", null, null, "CO2KostAufG, Anlage zu § 1 Abs. 1", LegalReferenceStatus.Missing, null, "/verwaltung/objekte",
                "Für dieses Jahr gibt es keine Stufentabelle.")
            : new("carbon", "CO₂-Kostenaufteilung (Stufenmodell)", $"{table.Levels.Count} Stufen, Version {table.Version}", $"seit {table.ValidFromYear}", table.Source,
                table.Verified ? LegalReferenceStatus.Current : LegalReferenceStatus.Check, null, "/verwaltung/objekte",
                table.Verified ? null : $"{table.Status}. Vor dem Versand einer Heizkostenabrechnung gegen den Gesetzestext prüfen.");
    }

    private static LegalReference DegreeDays(DateOnly today)
    {
        var table = DegreeDayTables.For(today.Year);
        return new("degree-days", "Gradtagszahlen (Aufteilung der Heizkosten im Jahresverlauf)", $"Version {table.Version}", $"seit {table.ValidFromYear}", table.Source,
            table.Verified ? LegalReferenceStatus.Current : LegalReferenceStatus.Check, null, "/verwaltung/objekte",
            table.Verified ? null : $"{table.Status}. Gegen die Tabelle des zuständigen Anbieters (z. B. VDI 3807, Deutscher Wetterdienst) tauschen, bevor eine Abrechnung darauf beruht.");
    }
}

/// <summary>Dashboard: references that are missing or outdated and that a statement or letter would rely on.</summary>
internal sealed class LegalReferenceTodos(ILegalReferences references) : ITodoSource
{
    public async Task<IReadOnlyList<TodoItem>> TodosAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.IsInRole(Roles.Admin) && !user.HasClaim(Permissions.ClaimType, nameof(AccessRight.Finance)))
        {
            return [];
        }

        // The shipped tables are listed on the page "Rechtsstände" but do not nag every day: only values the landlord maintains are tasks.
        return (await references.OverviewAsync(cancellationToken))
            .Where(r => r.Status != LegalReferenceStatus.Current && r.Key is "base-rate" or "cpi")
            .Select(r => new TodoItem("Rechtsstände",
                r.Status == LegalReferenceStatus.Missing ? $"{r.Title}: Wert fehlt" : $"{r.Title}: Wert prüfen und aktualisieren",
                r.Link, r.Status == LegalReferenceStatus.Missing ? Urgency.Soon : Urgency.Info, r.Note))
            .ToList();
    }
}
