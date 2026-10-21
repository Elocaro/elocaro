using MietHahn.Application.Accounts;
using MietHahn.Web.Components.Common;

namespace MietHahn.Web.Components.Layout;

/// <summary>A link in the tab bar of an area or an entity.</summary>
/// <param name="Href">Target; matched as prefix unless <paramref name="Exact"/> is set. A group opens its first child.</param>
/// <param name="Also">Further path prefixes that belong to the tab (pages reached from it).</param>
/// <param name="Children">Pages of a group; shown as second row below the tab bar while the group is active.</param>
public sealed record NavTab(string Label, string Href, string? Policy = null, bool Exact = false, string[]? Also = null, NavTab[]? Children = null)
{
    public bool IsGroup => Children is { Length: > 0 };

    public bool Matches(string path) => IsGroup
        ? Children!.Any(child => child.Matches(path))
        : Own(path, Href, Exact) || (Also?.Any(prefix => Own(path, prefix, false)) ?? false);

    private static bool Own(string path, string href, bool exact) => exact
        ? path.Equals(href, StringComparison.OrdinalIgnoreCase)
        : path.Equals(href, StringComparison.OrdinalIgnoreCase) || path.StartsWith(href + "/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Group of related pages under one tab.</summary>
    public static NavTab Group(string label, string? policy, params NavTab[] children) => new(label, children[0].Href, policy, Children: children);
}

/// <summary>The tab bar to show for a page.</summary>
public sealed record NavTabSet(string Label, NavTab[] Tabs);

/// <summary>An entry of the main navigation. <see cref="Prefixes"/> decide which pages belong to the area.</summary>
public sealed record NavArea(string Label, string Icon, string Href, string? Policy, string[] Prefixes, NavTab[] Tabs, string? Group = null, bool Exact = false)
{
    public bool Contains(string path) => Exact
        ? path.Equals(Href, StringComparison.OrdinalIgnoreCase)
        : Prefixes.Any(prefix => path.Equals(prefix, StringComparison.OrdinalIgnoreCase) || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Structure of the administration: main areas (sidebar, bottom bar) with sibling pages as tabs, and the
/// workspace tabs of a single property or lease. Single place for what belongs together.
/// </summary>
public static class AdminNavModel
{
    private const string Root = "/verwaltung";

    public static readonly NavArea Start = new("Übersicht", AppIcons.Todo, Root, null, [Root], [], Exact: true);

    public static readonly NavArea[] Areas =
    [
        Start,
        new("Objekte", AppIcons.Property, $"{Root}/objekte", Policies.MasterData,
            [$"{Root}/objekte", $"{Root}/einheiten", $"{Root}/zimmer", $"{Root}/zaehler", $"{Root}/abrechnungen"], [], "Bestand"),
        new("Personen", AppIcons.People, $"{Root}/personen", Policies.MasterData,
            [$"{Root}/personen", $"{Root}/datenschutz"],
            [new("Personen", $"{Root}/personen"), new("Datenschutz", $"{Root}/datenschutz")], "Bestand"),
        new("Verträge", AppIcons.Lease, $"{Root}/vertraege", Policies.MasterData,
            [$"{Root}/vertraege", $"{Root}/vpi"],
            [new("Verträge", $"{Root}/vertraege"), new("Verbraucherpreisindex", $"{Root}/vpi")], "Bestand"),
        new("Dokumente", AppIcons.Documents, $"{Root}/dokumente", Policies.MasterData,
            [$"{Root}/dokumente"],
            [new("Dokumente", $"{Root}/dokumente", Exact: true), new("Schreiben an Mieter", $"{Root}/dokumente/schreiben")], "Bestand"),
        new("Fristen", AppIcons.Deadlines, $"{Root}/fristen", Policies.MasterData, [$"{Root}/fristen"],
            [new("Kalender", $"{Root}/fristen", Exact: true), new("Wartung", $"{Root}/fristen/wartung")], "Bestand"),

        new("Bank", AppIcons.Bank, $"{Root}/bank/zuordnung", Policies.Finance, [$"{Root}/bank"],
            [new("Zuordnen", $"{Root}/bank/zuordnung", Also: [$"{Root}/bank/umsaetze/"]),
             new("Alle Umsätze", $"{Root}/bank/umsaetze", Exact: true),
             new("Regeln", $"{Root}/bank/regeln"),
             NavTab.Group("Konten", null, new("Bankkonten", $"{Root}/bank/konten", Also: [$"{Root}/bank/importe"]), new("CSV-Profile", $"{Root}/bank/csv-profile"))], "Finanzen"),
        new("Forderungen", AppIcons.OpenItems, $"{Root}/finanzen/offene-posten", Policies.Finance,
            [$"{Root}/finanzen/offene-posten", $"{Root}/finanzen/mahnungen", $"{Root}/finanzen/einstellungen"],
            [new("Offene Posten", $"{Root}/finanzen/offene-posten"), new("Mahnwesen", $"{Root}/finanzen/mahnungen"), new("Einstellungen", $"{Root}/finanzen/einstellungen")], "Finanzen"),
        new("Belege", AppIcons.Receipts, $"{Root}/belege", Policies.Finance, [$"{Root}/belege"], [], "Finanzen"),
        new("Darlehen & Steuer", AppIcons.Loan, $"{Root}/finanzen/darlehen", Policies.Finance,
            [$"{Root}/finanzen/darlehen", $"{Root}/finanzen/anlage-v-zeilen"],
            [new("Darlehen", $"{Root}/finanzen/darlehen"), new("Anlage-V-Zeilen", $"{Root}/finanzen/anlage-v-zeilen")], "Finanzen"),

        new("Benutzer", AppIcons.Users, $"{Root}/benutzer", Policies.UserManagement, [$"{Root}/benutzer"], [], "System"),
        new("Einstellungen", AppIcons.Finance, $"{Root}/einstellungen/vermieter", Policies.System,
            [$"{Root}/einstellungen", $"{Root}/system"],
            [new("Vermieter", $"{Root}/einstellungen/vermieter"), new("Vorlagen", $"{Root}/einstellungen/vorlagen"), new("Fristen", $"{Root}/einstellungen/fristen"),
             new("E-Mail-Versand", $"{Root}/system/mails"), new("Hintergrundjobs", $"{Root}/system/jobs"),
             new("Datensicherung", $"{Root}/system/datensicherung"), new("Belegauslesung", $"{Root}/system/belegauslesung")], "System"),
    ];

    /// <summary>The area a path belongs to, or null (e.g. account pages).</summary>
    public static NavArea? AreaOf(string path) => Areas.FirstOrDefault(area => area.Contains(path));

    /// <summary>The tabs of the page at <paramref name="path"/>: workspace of a property or lease, else the siblings of its area.</summary>
    public static NavTabSet? TabsFor(string path)
    {
        path = path.TrimEnd('/');
        if (EntityTabs(path, $"{Root}/objekte/", PropertyTabs) is { } property)
        {
            return new("Objekt", property);
        }

        if (EntityTabs(path, $"{Root}/vertraege/", LeaseTabs) is { } lease)
        {
            return new("Mietvertrag", lease);
        }

        return AreaOf(path) is { Tabs.Length: > 1 } area ? new(area.Label, area.Tabs) : null;
    }

    private static NavTab[] PropertyTabs(string id) =>
    [
        new("Übersicht", id, Exact: true),
        new("Zähler", $"{id}/zaehler", Also: [$"{id}/ablesung"]),
        NavTab.Group("Nebenkosten", Policies.Finance,
            new("Umlageschlüssel", $"{id}/umlageschluessel"),
            new("Heizkosten", $"{id}/heizung"),
            new("Vorschau", $"{id}/nebenkosten"),
            new("Abrechnungs-Assistent", $"{id}/abrechnung"),
            new("Abrechnungen", $"{id}/abrechnungen")),
        NavTab.Group("Steuer", Policies.Finance,
            new("AfA", $"{id}/afa"),
            new("Erhaltungsaufwand", $"{id}/erhaltungsaufwand"),
            new("Anlage V", $"{id}/anlage-v")),
    ];

    private static NavTab[] LeaseTabs(string id) =>
    [
        new("Übersicht", id, Exact: true),
        NavTab.Group("Konto", Policies.Finance,
            new("Mieterkonto", $"{id}/konto"),
            new("Vereinbarte Kostenarten", $"{id}/kostenarten")),
        NavTab.Group("Vertrag", null,
            new("Vertragsdokument", $"{id}/vertragsdokument"),
            new("Mieterhöhung", $"{id}/mieterhoehung")),
        NavTab.Group("Ein- & Auszug", null,
            new("Übergabe Einzug", $"{id}/uebergabe/einzug"),
            new("Übergabe Auszug", $"{id}/uebergabe/auszug"),
            new("Mieterwechsel", $"{id}/auszug")),
    ];

    private static NavTab[]? EntityTabs(string path, string prefix, Func<string, NavTab[]> tabs)
    {
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var segment = path[prefix.Length..].Split('/')[0];
        return Guid.TryParse(segment, out _) ? tabs(prefix + segment) : null;
    }
}
