using Elocaro.Web.Components.Common;
using Elocaro.Web.Components.Layout;

namespace Elocaro.Web.Tests;

public class PageInfoCatalogTests
{
    private static readonly string Property = "/verwaltung/objekte/" + Guid.NewGuid();
    private static readonly string Lease = "/verwaltung/vertraege/" + Guid.NewGuid();

    private static IEnumerable<string> Hrefs(IEnumerable<NavTab> tabs) =>
        tabs.SelectMany(tab => tab.IsGroup ? Hrefs(tab.Children!) : [tab.Href]);

    [Fact]
    public void Every_page_in_the_navigation_has_an_info()
    {
        var hrefs = AdminNavModel.Areas.SelectMany(area => Hrefs(area.Tabs).Append(area.Href))
            .Concat(Hrefs(AdminNavModel.TabsFor(Property)!.Tabs))
            .Concat(Hrefs(AdminNavModel.TabsFor(Lease)!.Tabs))
            .Distinct()
            .ToList();

        var missing = hrefs.Where(href => PageInfoCatalog.For(href) is null).ToList();

        Assert.True(missing.Count == 0, "Ohne Seiteninfo: " + string.Join(", ", missing));
    }

    [Fact]
    public void Infos_are_complete_and_links_point_to_pages_of_the_application()
    {
        foreach (var path in PageInfoCatalog.Paths)
        {
            var info = PageInfoCatalog.For(path)!;
            Assert.False(string.IsNullOrWhiteSpace(info.Title), path);
            Assert.False(string.IsNullOrWhiteSpace(info.Intro), path);
            Assert.NotEmpty(info.Sections);
            Assert.All(info.Sections, section =>
            {
                Assert.False(string.IsNullOrWhiteSpace(section.Heading), path);
                Assert.NotEmpty(section.Items);
                Assert.All(section.Items, item => Assert.False(string.IsNullOrWhiteSpace(item), path));
            });
            foreach (var link in info.Links ?? [])
            {
                Assert.StartsWith("/verwaltung", link.Href, StringComparison.Ordinal);
                Assert.NotNull(PageInfoCatalog.For(link.Href));
            }
        }
    }

    [Fact]
    public void Ids_in_a_path_are_normalized()
    {
        var id = Guid.NewGuid();

        Assert.Equal("/verwaltung/objekte/{id}/zaehler", PageInfoCatalog.Normalize($"/Verwaltung/Objekte/{id}/Zaehler/"));
        Assert.Same(PageInfoCatalog.For("/verwaltung/vertraege/" + id + "/konto"), PageInfoCatalog.For("/verwaltung/vertraege/" + Guid.NewGuid() + "/konto"));
        Assert.Null(PageInfoCatalog.For("/konto/anmelden"));
    }
}
