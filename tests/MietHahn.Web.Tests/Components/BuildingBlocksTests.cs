using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Web.Components.Common;
using MietHahn.Web.Display;

namespace MietHahn.Web.Tests.Components;

public class BuildingBlocksTests : BunitContext
{
    [Theory]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1234,56", 1234.56)]
    [InlineData("1234.56", 1234.56)]
    [InlineData("1234.5", 1234.5)]
    [InlineData("-12 €", -12)]
    [InlineData("812,505", 812.51)]
    [InlineData("1.234", 1234)] // Tausenderpunkt ohne Nachkommastellen
    [InlineData(" 0,5 ", 0.5)]
    public void Amounts_are_parsed_in_German_format(string text, double expected)
    {
        Assert.True(InputAmount.Read(text, out var amount));
        Assert.Equal((decimal)expected, amount);
    }

    [Theory]
    [InlineData("zwölf")]
    [InlineData("1,2,3")]
    public void Invalid_amounts_are_detected(string text)
    {
        Assert.False(InputAmount.Read(text, out _));
    }

    [Fact]
    public void Empty_amount_is_null()
    {
        Assert.True(InputAmount.Read("  ", out var amount));
        Assert.Null(amount);
    }

    [Fact]
    public void Sort_header_toggles_direction_and_sets_aria_sort()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/verwaltung/benutzer?seite=3");

        var newItem = Render<SortHeader>(p => p.Add(x => x.Column, "name").Add(x => x.Text, "Name").Add(x => x.Query, new TableQuery(null, false, 3)));
        var ascending = Render<SortHeader>(p => p.Add(x => x.Column, "name").Add(x => x.Text, "Name").Add(x => x.Query, new TableQuery("name", false, 1)));

        Assert.Equal("none", newItem.Find("th").GetAttribute("aria-sort"));
        Assert.Equal("http://localhost/verwaltung/benutzer?sortierung=name", newItem.Find("a").GetAttribute("href"));
        Assert.Equal("ascending", ascending.Find("th").GetAttribute("aria-sort"));
        Assert.Contains("absteigend=true", ascending.Find("a").GetAttribute("href"), StringComparison.Ordinal);
    }

    [Fact]
    public void Pagination_appears_only_with_multiple_pages()
    {
        Assert.Empty(Render<Pagination>(p => p.Add(x => x.Page, 1).Add(x => x.Pages, 1)).Markup.Trim());

        var middle = Render<Pagination>(p => p.Add(x => x.Page, 2).Add(x => x.Pages, 3).Add(x => x.Total, 60));
        var links = middle.FindAll("a").Select(a => a.GetAttribute("href")).ToList();
        Assert.Equal(["http://localhost/", "http://localhost/?seite=3"], links);
        Assert.Contains("Seite 2 von 3 (60 Einträge)", middle.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Table_query_sorts_and_pages()
    {
        var numbers = Enumerable.Range(1, 30).ToList();
        var query = new TableQuery("wert", Descending: true, Page: 2, PageSize: 10);

        var sorted = query.Sort(numbers, new Dictionary<string, Func<int, object?>> { ["wert"] = z => z }, "wert").ToList();
        var page = query.Paginate(sorted);

        Assert.Equal(Enumerable.Range(11, 10).Reverse(), page.Entries);
        Assert.Equal(3, page.Pages);
        Assert.Equal(30, page.Total);
    }

    [Fact]
    public void Unknown_sort_column_falls_back_to_default()
    {
        var query = new TableQuery("boese'--", false, 1);

        var sorted = query.Sort(new List<int> { 3, 1, 2 }.AsQueryable(), new Dictionary<string, System.Linq.Expressions.Expression<Func<int, object?>>> { ["wert"] = z => z }, "wert");

        Assert.Equal([1, 2, 3], sorted);
    }

    [Fact]
    public void Page_header_shows_title_back_and_actions()
    {
        var header = Render<PageHeader>(p => p
            .Add(x => x.Title, "Benutzer")
            .Add(x => x.Back, "/verwaltung")
            .Add(x => x.Actions, (RenderFragment)(b => b.AddMarkupContent(0, "<a class=\"knopf\" href=\"/neu\">Neu</a>"))));

        Assert.Equal("Benutzer", header.Find("h1").TextContent);
        Assert.Equal("/verwaltung", header.Find("a.zurueck").GetAttribute("href"));
        Assert.NotNull(header.Find(".seitenkopf-aktionen a.knopf"));
    }

    [Fact]
    public void Danger_action_asks_for_confirmation_before_submitting()
    {
        var action = Render<DangerAction>(p => p
            .Add(x => x.Text, "Konto sperren")
            .Add(x => x.Confirmation, "Wirklich sperren?")
            .Add(x => x.FormName, "sperren")
            .Add(x => x.Confirmed, () => { }));

        var details = action.Find("details.gefahrenaktion");
        Assert.False(details.HasAttribute("open"));
        Assert.Equal("Konto sperren", details.QuerySelector("summary")!.TextContent);
        Assert.Contains("Wirklich sperren?", details.TextContent, StringComparison.Ordinal);
    }
}
