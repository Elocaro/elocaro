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
}
