using Bunit;
using Elocaro.Application.Portfolio;
using Elocaro.Web.Components.Common;
using Elocaro.Web.Components.Controls;

namespace Elocaro.Web.Tests.Components;

public class ChartBlocksTests : BunitContext
{
    private static readonly string[] Words = ["erledigt", "aktueller Schritt"];

    [Fact]
    public void Line_chart_needs_two_points()
    {
        var one = Render<LineChart>(p => p.Add(c => c.Caption, "Restschuld").Add(c => c.Points, [new ChartPoint("2026", 10m)]));
        Assert.Empty(one.Markup.Trim());
    }

    [Fact]
    public void Line_chart_handles_a_flat_series_and_negative_values()
    {
        var flat = Render<LineChart>(p => p.Add(c => c.Caption, "Miete").Add(c => c.StartAtZero, false).Add(c => c.Points, [new ChartPoint("a", 5m), new ChartPoint("b", 5m)]));
        Assert.DoesNotContain("NaN", flat.Markup, StringComparison.Ordinal);

        var credit = Render<LineChart>(p => p.Add(c => c.Caption, "Saldo").Add(c => c.Points, [new ChartPoint("a", -50m), new ChartPoint("b", 100m)]));
        Assert.Single(credit.FindAll(".line-chart-zero"));
        Assert.Contains("Saldo: von", credit.Find("svg").GetAttribute("aria-label"), StringComparison.Ordinal);
    }

    [Fact]
    public void Line_chart_offers_the_values_as_a_table_and_names_markers()
    {
        var chart = Render<LineChart>(p => p.Add(c => c.Caption, "Restschuld").Add(c => c.Points, [new ChartPoint("2026", 100m), new ChartPoint("2027", 90m, "Zinsbindung endet")]));

        Assert.Equal(2, chart.FindAll("tbody tr").Count);
        Assert.Single(chart.FindAll(".line-chart-marker"));
        Assert.Contains("2027: Zinsbindung endet", chart.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Segment_bar_drops_empty_parts_and_shows_nothing_without_a_value()
    {
        var empty = Render<SegmentBar>(p => p.Add(c => c.Segments, [new ChartSegment("a", 0m, "0", ChartTone.Primary)]));
        Assert.Empty(empty.Markup.Trim());

        var bar = Render<SegmentBar>(p => p.Add(c => c.Segments, [new ChartSegment("a", 1m, "eins", ChartTone.Primary), new ChartSegment("b", 3m, "drei", ChartTone.Error), new ChartSegment("c", 0m, "null", ChartTone.Neutral)]));
        Assert.Equal(2, bar.FindAll(".segment-bar > span").Count);
        Assert.Equal("width:25%", bar.FindAll(".segment-bar > span")[0].GetAttribute("style"));
        Assert.Contains("b: drei", bar.Find(".segment-bar").GetAttribute("aria-label"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(5, 10, "50")]
    [InlineData(20, 10, "100")]
    [InlineData(-3, 10, "0")]
    public void Progress_meter_stays_on_the_scale(double value, double total, string expected)
    {
        var meter = Render<ProgressMeter>(p => p.Add(c => c.Value, Dec.From(value)).Add(c => c.Total, Dec.From(total)).Add(c => c.Text, "Stand"));
        Assert.Equal($"width:{expected}%", meter.Find(".meter-bar").GetAttribute("style"));
    }

    [Fact]
    public void Progress_meter_without_a_total_draws_nothing()
    {
        var meter = Render<ProgressMeter>(p => p.Add(c => c.Value, 1m).Add(c => c.Total, 0m).Add(c => c.Text, "Stand"));
        Assert.Empty(meter.Markup.Trim());
    }

    [Fact]
    public void Process_track_says_its_state_in_words_not_only_in_color()
    {
        var track = Render<ProcessTrack>(p => p.Add(c => c.Label, "Stand").Add(c => c.Steps,
        [
            new ProcessStep("Eins", ProcessState.Done),
            new ProcessStep("Zwei", ProcessState.Current, "bis morgen", "/ziel"),
            new ProcessStep("Drei", ProcessState.Unknown),
        ]));

        Assert.All(Words, word => Assert.Contains(word, track.Markup, StringComparison.Ordinal));
        Assert.Equal("step", track.FindAll("li")[1].GetAttribute("aria-current"));
        Assert.Equal("/ziel", track.Find("a").GetAttribute("href"));
    }

    [Fact]
    public void Kpi_tile_is_a_link_only_with_a_target()
    {
        var plain = Render<KpiTile>(p => p.Add(c => c.Label, "Belegung").Add(c => c.Value, "3"));
        Assert.Equal("DIV", plain.Find(".kpi").TagName);

        var link = Render<KpiTile>(p => p.Add(c => c.Label, "Belegung").Add(c => c.Value, "3").Add(c => c.Href, "/ziel").Add(c => c.Level, NoticeLevel.Warning));
        Assert.Equal("A", link.Find(".kpi").TagName);
        Assert.Contains("is-warning", link.Find(".kpi").ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public void Help_term_explains_itself_with_the_text_of_the_glossary()
    {
        var term = Render<HelpTerm>(p => p.Add(c => c.Term, "Zufluss").Add(c => c.Text, "zugeflossen"));

        Assert.Equal("zugeflossen", term.Find("button").TextContent);
        Assert.Equal(term.Find("[popover]").Id, term.Find("button").GetAttribute("popovertarget"));
        Assert.Contains(Glossary.Explain("Zufluss"), term.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Month_chart_draws_a_credit_below_the_zero_line()
    {
        var history = Enumerable.Range(0, 3).Select(i => new MonthFigure(new DateOnly(2026, i + 1, 1), 100m, 100m, -50m)).ToList();
        var chart = Render<MonthChart>(p => p.Add(c => c.History, history));

        Assert.Single(chart.FindAll(".line-chart-zero"));
        var zero = double.Parse(chart.Find(".line-chart-zero").GetAttribute("y1")!, System.Globalization.CultureInfo.InvariantCulture);
        var line = double.Parse(chart.Find(".line-open").GetAttribute("points")!.Split(' ')[0].Split(',')[1], System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(line > zero, "the line of a credit must lie below the zero line");
    }
}
