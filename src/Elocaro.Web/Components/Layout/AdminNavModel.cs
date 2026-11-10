using Elocaro.Application.Accounts;
using Elocaro.Application.Admin;
using Elocaro.Web.Components.Common;

namespace Elocaro.Web.Components.Layout;

/// <summary>A link in the tab bar of an area or an entity.</summary>
/// <param name="Href">Target; matched as prefix unless <paramref name="Exact"/> is set. A group opens its first child.</param>
/// <param name="Also">Further path prefixes that belong to the tab (pages reached from it).</param>
/// <param name="SeparatorBefore">Within a group: a divider before this page (sets apart preparation from execution).</param>
/// <param name="Children">Pages of a group; shown as second row below the tab bar while the group is active.</param>
public sealed record NavTab(string Label, string Href, string? Policy = null, bool Exact = false, string[]? Also = null, NavTab[]? Children = null, bool SeparatorBefore = false)
{
    public bool IsGroup => Children is { Length: > 0 };

    public bool Matches(string path) => IsGroup
        ? Children!.Any(child => child.Matches(path))
        : Own(path, Href, Exact) || (Also?.Any(prefix => Own(path, prefix, false)) ?? false);

    private static bool Own(string path, string href, bool exact) => exact
        ? path.Equals(href, StringComparison.OrdinalIgnoreCase)
        : path.Equals(href, StringComparison.OrdinalIgnoreCase) || path.StartsWith(href.EndsWith('/') ? href : href + "/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Group of related pages under one tab.</summary>
    public static NavTab Group(string label, string? policy, params NavTab[] children) => new(label, children[0].Href, policy, Children: children);
}

/// <summary>The tab bar to show for a page. <paramref name="IsArea"/>: the siblings of an area, which the sidebar lists itself on wide screens.</summary>
public sealed record NavTabSet(string Label, NavTab[] Tabs, bool IsArea = false);

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
        new("Bestand", AppIcons.Property, $"{Root}/objekte", Policies.MasterData,
            [$"{Root}/objekte", $"{Root}/einheiten", $"{Root}/zimmer", $"{Root}/zaehler", $"{Root}/abrechnungen",
             $"{Root}/personen", $"{Root}/datenschutz", $"{Root}/vertraege", $"{Root}/vpi"],
            [new("Objekte", $"{Root}/objekte", Also: [$"{Root}/einheiten", $"{Root}/zimmer", $"{Root}/zaehler", $"{Root}/abrechnungen"]),
             NavTab.Group("Personen", null,
                 new("Personen", $"{Root}/personen", Exact: true), new("Änderungen", $"{Root}/personen/aenderungen"), new("Datenschutz", $"{Root}/datenschutz")),
             NavTab.Group("Verträge", null,
                 new("Verträge", $"{Root}/vertraege"), new("Verbraucherpreisindex", $"{Root}/vpi"))]),
        new("Kommunikation", AppIcons.Messages, $"{Root}/tickets", Policies.MasterData,
            [$"{Root}/tickets", $"{Root}/nachrichten", $"{Root}/aushaenge", $"{Root}/dokumente", $"{Root}/fristen"],
            [NavTab.Group("Tickets", null,
                 new("Tickets", $"{Root}/tickets", Exact: true, Also: [$"{Root}/tickets/neu"]), new("Hausmeister-Aufgaben", $"{Root}/tickets/routinen")),
             NavTab.Group("Mitteilungen", null,
                 new("Nachrichten", $"{Root}/nachrichten"), new("Aushänge", $"{Root}/aushaenge")),
             NavTab.Group("Dokumente", null,
                 new("Dokumente", $"{Root}/dokumente", Exact: true), new("Schreiben an Mieter", $"{Root}/dokumente/schreiben")),
             NavTab.Group("Fristen", null,
                 new("Kalender", $"{Root}/fristen", Exact: true), new("Wartung", $"{Root}/fristen/wartung"))]),
        new("Geld", AppIcons.Bank, $"{Root}/bank/zuordnung", Policies.Finance,
            [$"{Root}/bank", $"{Root}/finanzen", $"{Root}/belege"],
            [NavTab.Group("Bank", null,
                 new("Zuordnen", $"{Root}/bank/zuordnung", Also: [$"{Root}/bank/umsaetze/"]),
                 new("Alle Umsätze", $"{Root}/bank/umsaetze", Exact: true),
                 new("Regeln", $"{Root}/bank/regeln"),
                 new("Bankkonten", $"{Root}/bank/konten", Also: [$"{Root}/bank/importe"]),
                 new("CSV-Profile", $"{Root}/bank/csv-profile")),
             NavTab.Group("Forderungen", null,
                 new("Offene Posten", $"{Root}/finanzen/offene-posten"), new("Mahnwesen", $"{Root}/finanzen/mahnungen"), new("Finanzeinstellungen", $"{Root}/finanzen/einstellungen")),
             new("Belege", $"{Root}/belege"),
             NavTab.Group("Darlehen & Steuer", null,
                 new("Darlehen", $"{Root}/finanzen/darlehen"), new("Anlage-V-Zeilen", $"{Root}/finanzen/anlage-v-zeilen"), new("DATEV-Export", $"{Root}/finanzen/datev"))]),

        new("Benutzer", AppIcons.Users, $"{Root}/benutzer", Policies.UserManagement, [$"{Root}/benutzer"], [], "System"),
        new("Einstellungen", AppIcons.Finance, $"{Root}/einstellungen/vermieter", Policies.System,
            [$"{Root}/einstellungen", $"{Root}/system"],
            [new("Vermieter", $"{Root}/einstellungen/vermieter"), new("Module", $"{Root}/einstellungen/module"), new("Vorlagen", $"{Root}/einstellungen/vorlagen"), new("Erinnerungen", $"{Root}/einstellungen/fristen"), new("Rechtsstände", $"{Root}/einstellungen/rechtsstaende"), new("Daten importieren", $"{Root}/einstellungen/import"),
             NavTab.Group("System", null,
                 new("E-Mail-Versand", $"{Root}/system/mails"), new("Hintergrundjobs", $"{Root}/system/jobs"),
                 new("Datensicherung", $"{Root}/system/datensicherung"), new("Notfallmappe", $"{Root}/system/notfallmappe"), new("Belegauslesung", $"{Root}/system/belegauslesung"))], "System"),
    ];

    /// <summary>The area a path belongs to, or null (e.g. account pages).</summary>
    public static NavArea? AreaOf(string path) => Areas.FirstOrDefault(area => area.Contains(path));

    /// <summary>The areas whose module is on.</summary>
    public static IEnumerable<NavArea> VisibleAreas(IModuleState modules)
    {
        foreach (var area in Areas)
        {
            if (IsOn(area.Href, modules))
            {
                yield return area;
            }
            else if (Visible(area.Tabs, modules) is [{ } first, ..])
            {
                // The area's own entry page is off, but other pages remain: lead to the first of them.
                yield return area with { Href = first.Href };
            }
        }
    }

    /// <summary>The tabs without those of switched-off modules; a group disappears with its last page.</summary>
    public static NavTab[] Visible(IEnumerable<NavTab> tabs, IModuleState modules) =>
        tabs.Select(tab => tab.IsGroup ? tab with { Children = Visible(tab.Children!, modules) } : tab)
            .Where(tab => tab.IsGroup ? tab.Children!.Length > 0 : IsOn(tab.Href, modules))
            .Select(tab => tab.Children is [{ } only]
                ? only with { Label = tab.Label, Policy = only.Policy ?? tab.Policy } // a group of one page is a plain tab, no second row
                : tab.IsGroup ? tab with { Href = tab.Children!.Any(child => child.Href == tab.Href) ? tab.Href : tab.Children![0].Href } : tab)
            .ToArray();

    /// <summary>Whether the page at <paramref name="href"/> exists in this installation (its module and option are switched on).</summary>
    public static bool IsOn(string href, IModuleState modules) =>
        (ModuleCatalog.OwnerOf(href) is not { } owner || modules.IsEnabled(owner.Module))
        && (ModuleCatalog.OptionOf(href) is not { } option || modules.IsVisible(option.Option));

    /// <summary>The tabs of the page at <paramref name="path"/>: workspace of a property or lease, else the siblings of its area.</summary>
    public static NavTabSet? TabsFor(string path, IModuleState modules, Guid? propertyId = null)
    {
        if (TabsFor(path, propertyId) is not { } set)
        {
            return null;
        }

        var tabs = Visible(set.Tabs, modules);
        return tabs.Length > 1 || (tabs.Length == 1 && set.Tabs.Length == 1) ? set with { Tabs = tabs } : null;
    }

    /// <summary>The tabs of the page at <paramref name="path"/>: workspace of a property or lease, else the siblings of its area.</summary>
    /// <param name="propertyId">The property a page outside of <c>/objekte/{id}</c> (unit, room, meter, statement) belongs to; it gets that property's workspace tabs.</param>
    public static NavTabSet? TabsFor(string path, Guid? propertyId = null)
    {
        path = path.TrimEnd('/');
        if (propertyId is { } owningProperty && BelongsToProperty(path))
        {
            return new("Objekt", PropertyTabs($"{Root}/objekte/{owningProperty}"));
        }

        if (EntityTabs(path, $"{Root}/objekte/", PropertyTabs) is { } property)
        {
            return new("Objekt", property);
        }

        if (EntityTabs(path, $"{Root}/vertraege/", LeaseTabs) is { } lease)
        {
            return new("Mietvertrag", lease);
        }

        return AreaOf(path) is { Tabs.Length: > 1 } area ? new(area.Label, area.Tabs, IsArea: true) : null;
    }

    /// <summary>Pages that belong to a property although their path does not start with it.</summary>
    public static bool BelongsToProperty(string path) =>
        PropertyPrefixes.Any(prefix => path.Equals(prefix, StringComparison.OrdinalIgnoreCase) || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase));

    private static readonly string[] PropertyPrefixes = [$"{Root}/einheiten", $"{Root}/zimmer", $"{Root}/zaehler", $"{Root}/abrechnungen"];

    private static NavTab[] PropertyTabs(string id) =>
    [
        new("Übersicht", id, Exact: true, Also: [$"{id}/belegimport", $"{Root}/einheiten", $"{Root}/zimmer"]),
        new("Belegung", $"{id}/belegung"),
        new("Zähler", $"{id}/zaehler", Also: [$"{id}/ablesung", $"{id}/zaehlerimport", $"{Root}/zaehler"]),
        // Order of the work: set up (supply contracts, keys, heating), then check, then settle. The group still opens on the statements.
        NavTab.Group("Nebenkosten", Policies.Finance,
            new("Versorgungsverträge", $"{id}/versorgung"),
            new("Umlageschlüssel", $"{id}/umlageschluessel"),
            new("Heizkosten", $"{id}/heizung"),
            new("Vorab prüfen", $"{id}/nebenkosten", SeparatorBefore: true),
            new("Abrechnungen", $"{id}/abrechnungen", Also: [$"{id}/abrechnung", $"{Root}/abrechnungen"])) with { Href = $"{id}/abrechnungen" },
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
        new("Mitteilungen", $"{id}/kommunikation"),
        NavTab.Group("Ein- & Auszug", null,
            new("Mieterwechsel", $"{id}/auszug"),
            new("Übergabe Einzug", $"{id}/uebergabe/einzug"),
            new("Übergabe Auszug", $"{id}/uebergabe/auszug")),
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
