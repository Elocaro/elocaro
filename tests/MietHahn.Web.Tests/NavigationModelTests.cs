using MietHahn.Web.Components.Layout;

namespace MietHahn.Web.Tests;

public class NavigationModelTests
{
    [Fact]
    public void The_sidebar_has_at_most_seven_areas()
    {
        Assert.InRange(AdminNavModel.Areas.Length, 1, 7);
        Assert.Equal(AdminNavModel.Areas.Length, AdminNavModel.Areas.Select(a => a.Label).Distinct().Count());
    }

    [Fact]
    public void Tabs_have_at_most_two_levels_and_each_page_is_three_clicks_from_the_dashboard()
    {
        foreach (var area in AdminNavModel.Areas)
        {
            foreach (var tab in area.Tabs)
            {
                // Sidebar → tab → page of the group.
                Assert.All(tab.Children ?? [], child => Assert.False(child.IsGroup, $"{area.Label}/{tab.Label}: Gruppen lassen sich nicht schachteln"));
            }
        }

        foreach (var set in new[] { AdminNavModel.TabsFor("/verwaltung/objekte/" + Guid.NewGuid())!, AdminNavModel.TabsFor("/verwaltung/vertraege/" + Guid.NewGuid())! })
        {
            Assert.All(set.Tabs, tab => Assert.All(tab.Children ?? [], child => Assert.False(child.IsGroup)));
        }
    }

    [Fact]
    public void The_settings_tabs_fit_into_one_row_and_the_system_pages_sit_in_a_group()
    {
        var settings = AdminNavModel.Areas.Single(a => a.Href.EndsWith("/einstellungen/vermieter", StringComparison.Ordinal));

        Assert.InRange(settings.Tabs.Length, 1, 7);
        var system = Assert.Single(settings.Tabs, t => t.IsGroup);
        Assert.Equal("System", system.Label);
        Assert.Equal(["E-Mail-Versand", "Hintergrundjobs", "Datensicherung", "Belegauslesung"], system.Children!.Select(c => c.Label));
        Assert.All(system.Children!, child => Assert.True(system.Matches(child.Href)));
    }

    [Fact]
    public void Every_page_of_the_old_areas_still_belongs_to_an_area()
    {
        string[] paths =
        [
            "/verwaltung/objekte", "/verwaltung/einheiten/x", "/verwaltung/zimmer/x", "/verwaltung/zaehler/x", "/verwaltung/abrechnungen/x", "/verwaltung/personen", "/verwaltung/personen/aenderungen",
            "/verwaltung/datenschutz", "/verwaltung/vertraege", "/verwaltung/vpi", "/verwaltung/dokumente", "/verwaltung/dokumente/schreiben", "/verwaltung/tickets", "/verwaltung/tickets/routinen",
            "/verwaltung/nachrichten", "/verwaltung/aushaenge", "/verwaltung/fristen", "/verwaltung/fristen/wartung", "/verwaltung/bank/zuordnung", "/verwaltung/bank/umsaetze", "/verwaltung/bank/regeln",
            "/verwaltung/bank/konten", "/verwaltung/bank/csv-profile", "/verwaltung/finanzen/offene-posten", "/verwaltung/finanzen/mahnungen", "/verwaltung/belege", "/verwaltung/finanzen/darlehen",
            "/verwaltung/finanzen/datev", "/verwaltung/benutzer", "/verwaltung/einstellungen/module", "/verwaltung/system/jobs",
        ];

        foreach (var path in paths)
        {
            var area = AdminNavModel.AreaOf(path);
            Assert.True(area is not null, path);
            Assert.True(area!.Tabs.Length == 0 || AdminNavModel.TabsFor(path) is not null, path);
        }
    }

    [Theory]
    [InlineData("/verwaltung/einheiten/x", "Übersicht")]
    [InlineData("/verwaltung/zimmer/x", "Übersicht")]
    [InlineData("/verwaltung/zaehler/x", "Zähler")]
    [InlineData("/verwaltung/abrechnungen/x", "Abrechnungen")]
    public void Pages_of_a_property_outside_its_path_get_the_workspace_tabs_of_that_property(string path, string activeTab)
    {
        var propertyId = Guid.NewGuid();

        var set = AdminNavModel.TabsFor(path, propertyId)!;

        Assert.Equal("Objekt", set.Label);
        Assert.Contains(set.Tabs, tab => tab.Href.EndsWith(propertyId.ToString(), StringComparison.Ordinal));
        var active = set.Tabs.Single(tab => tab.Matches(path));
        Assert.Equal(activeTab, active.Label.Length > 0 && active.IsGroup ? active.Children!.Single(c => c.Matches(path)).Label : active.Label);
    }

    [Fact]
    public void Without_a_property_those_pages_keep_the_area_tabs()
    {
        Assert.Equal("Bestand", AdminNavModel.TabsFor("/verwaltung/einheiten/x")!.Label);
    }
}
