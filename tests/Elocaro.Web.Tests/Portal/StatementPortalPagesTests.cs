using System.Net;
using Elocaro.Application.Accounts;
using Elocaro.Application.Common;
using Elocaro.Application.Files;
using Elocaro.Application.Finance;
using Elocaro.Application.Leases;
using Elocaro.Application.OperatingCosts;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Application.Receipts;
using Elocaro.Application.Statements;
using Elocaro.Domain.Common;
using Elocaro.Domain.OperatingCosts;
using Elocaro.Domain.People;
using Elocaro.Domain.Statements;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Web.Components.Controls;
using Elocaro.Web.Tests.Account;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elocaro.Web.Tests.Portal;

/// <summary>Sent statements in the portal: explanation page, receipt download, question prefill and the separation between tenants.</summary>
public class StatementPortalPagesTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private const string Marker = "STEUERBESCHEID-INHALT";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Tenant_reads_the_derivation_of_the_own_statement_and_never_sees_the_neighbour()
    {
        var setup = await SetUpAsync();
        using var _ = setup.Tenant;

        var list = await setup.Tenant.GetAsync("/portal/abrechnungen");
        Assert.Contains("2026", list.TextWithStreaming, StringComparison.Ordinal);
        Assert.Contains("Wohnung 1", list.TextWithStreaming, StringComparison.Ordinal);
        Assert.DoesNotContain("Wohnung 2", list.TextWithStreaming, StringComparison.Ordinal);
        Assert.Contains($"/portal/abrechnungen/{setup.OwnDispatch}", Hrefs(list));
        Assert.DoesNotContain($"/portal/abrechnungen/{setup.NeighbourDispatch}", Hrefs(list));

        var page = await setup.Tenant.GetAsync($"/portal/abrechnungen/{setup.OwnDispatch}", followRedirects: false);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        var text = page.TextWithStreaming;
        Assert.Contains("1.000,00 € nach Wohnfläche: Ihr Anteil 600,00 €", text, StringComparison.Ordinal);
        Assert.Contains("Vergleich mit dem Vorjahr", text, StringComparison.Ordinal);
        Assert.Contains("Für das Vorjahr liegt keine an Sie versendete Abrechnung vor.", text, StringComparison.Ordinal);
        Assert.Contains("ersetzt keinen förmlichen Einwand", text, StringComparison.Ordinal);
        Assert.Contains($"/portal/nachrichten/neu?abrechnung={setup.OwnDispatch}&position=betrkv-01", Hrefs(page));
        Assert.DoesNotContain("Weber", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Wohnung 2", text, StringComparison.Ordinal);
        Assert.DoesNotContain("400,00 €", text, StringComparison.Ordinal); // the neighbour's share

        var foreign = await setup.Tenant.GetAsync($"/portal/abrechnungen/{setup.NeighbourDispatch}", followRedirects: false);
        Assert.Equal(HttpStatusCode.NotFound, foreign.Status);
        Assert.DoesNotContain("400,00 €", foreign.TextWithStreaming, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await setup.Tenant.GetAsync($"/portal/abrechnungen/{Guid.NewGuid()}", followRedirects: false)).Status);
    }

    [Fact]
    public async Task Receipt_download_works_only_for_released_receipts_of_the_own_dispatch()
    {
        var setup = await SetUpAsync();
        using var _ = setup.Tenant;
        var own = $"/portal/abrechnungen/{setup.OwnDispatch}/belege/{setup.Receipt}/{setup.ReceiptFile}";

        Assert.Equal(HttpStatusCode.NotFound, (await setup.Tenant.GetAsync(own, followRedirects: false)).Status); // nothing released yet
        var before = await setup.Tenant.GetAsync($"/portal/abrechnungen/{setup.OwnDispatch}");
        Assert.Contains("noch keine Belege freigegeben", before.TextWithStreaming, StringComparison.Ordinal);

        await App.WithScopeAsync(sp => sp.GetRequiredService<IStatementReceiptShares>().SetSharedAsync(setup.Statement, [setup.Receipt], Ct));

        var page = await setup.Tenant.GetAsync($"/portal/abrechnungen/{setup.OwnDispatch}");
        Assert.Contains("Stadt Köln", page.TextWithStreaming, StringComparison.Ordinal);
        Assert.Contains(own, Hrefs(page));
        var file = await setup.Tenant.GetAsync(own, followRedirects: false);
        Assert.Equal(HttpStatusCode.OK, file.Status);
        Assert.Contains(Marker, file.Html, StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.NotFound, (await setup.Tenant.GetAsync($"/portal/abrechnungen/{setup.NeighbourDispatch}/belege/{setup.Receipt}/{setup.ReceiptFile}", followRedirects: false)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await setup.Tenant.GetAsync($"/portal/abrechnungen/{setup.OwnDispatch}/belege/{setup.Receipt}/{setup.LetterFile}", followRedirects: false)).Status);

        await App.WithScopeAsync(sp => sp.GetRequiredService<IStatementReceiptShares>().SetSharedAsync(setup.Statement, [], Ct));
        Assert.Equal(HttpStatusCode.NotFound, (await setup.Tenant.GetAsync(own, followRedirects: false)).Status); // withdrawn
    }

    [Fact]
    public async Task Receipt_download_is_closed_for_visitors_and_for_the_administration()
    {
        var setup = await SetUpAsync();
        using var _ = setup.Tenant;
        await App.WithScopeAsync(sp => sp.GetRequiredService<IStatementReceiptShares>().SetSharedAsync(setup.Statement, [setup.Receipt], Ct));
        var path = $"/portal/abrechnungen/{setup.OwnDispatch}/belege/{setup.Receipt}/{setup.ReceiptFile}";

        using var visitor = App.Browser();
        var anonymous = await visitor.GetAsync(path, followRedirects: false);
        Assert.Equal("/konto/anmelden", anonymous.RedirectLocation!.AbsolutePath);

        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var __ = admin;
        Assert.Equal("/konto/zugriff-verweigert", (await admin.GetAsync(path, followRedirects: false)).RedirectLocation!.AbsolutePath);
        Assert.Equal("/konto/zugriff-verweigert", (await admin.GetAsync("/portal/abrechnungen", followRedirects: false)).RedirectLocation!.AbsolutePath);
    }

    [Fact]
    public async Task A_question_about_a_position_prefills_the_subject_and_warns_about_the_deadline()
    {
        var setup = await SetUpAsync();
        using var _ = setup.Tenant;

        var page = await setup.Tenant.GetAsync($"/portal/nachrichten/neu?abrechnung={setup.OwnDispatch}&position=betrkv-01");

        Assert.Contains("Frage zur Abrechnung 2026: Laufende öffentliche Lasten (Grundsteuer)", Values(page));
        Assert.Contains("ersetzt keinen förmlichen Einwand", page.Text, StringComparison.Ordinal);

        // Foreign dispatch: no prefill, no hint (the subject is built from the tenant's own statement only).
        var foreign = await setup.Tenant.GetAsync($"/portal/nachrichten/neu?abrechnung={setup.NeighbourDispatch}&position=betrkv-01");
        Assert.DoesNotContain(Values(foreign), v => v?.StartsWith("Frage zur Abrechnung", StringComparison.Ordinal) == true);
        Assert.DoesNotContain("förmlichen Einwand", foreign.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Start_page_and_documents_point_to_the_statements()
    {
        var setup = await SetUpAsync();
        using var _ = setup.Tenant;

        var start = await setup.Tenant.GetAsync("/portal");
        Assert.Contains($"/portal/abrechnungen/{setup.OwnDispatch}", Hrefs(start));
        var documents = await setup.Tenant.GetAsync("/portal/dokumente");
        Assert.Contains("/portal/abrechnungen", Hrefs(documents));
        Assert.Contains("/portal/abrechnungen", Hrefs(start));
    }

    [Fact]
    public async Task Administration_chooses_receipts_and_warns_about_data_of_third_parties()
    {
        var setup = await SetUpAsync();
        using var _ = setup.Tenant;
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var __ = admin;

        var page = await admin.GetAsync($"/verwaltung/abrechnungen/{setup.Statement}");

        Assert.Contains("Belege für Mieter freigeben", page.Text, StringComparison.Ordinal);
        Assert.Contains("Belege können Daten Dritter enthalten", page.Text, StringComparison.Ordinal);
        Assert.Contains("Stadt Köln", page.Document.QuerySelector("#receipt-share-table")?.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Result_chart_agrees_with_the_balance_and_names_an_additional_payment()
    {
        MoveClockBehindTheBillingYear();
        var setup = await SetUpAsync((sp, _, lease) => PayMonthsAsync(sp, lease, 3)); // 600.00 € share, 3 × 100.00 € prepayments
        using var _ = setup.Tenant;

        var page = await setup.Tenant.GetAsync($"/portal/abrechnungen/{setup.OwnDispatch}");

        var chart = Chart(page, "result-chart");
        Assert.Contains("300,00 € Nachzahlung", page.TextWithStreaming, StringComparison.Ordinal); // the summary above the chart says the same
        Assert.Equal(["Ihr Anteil 600,00 €", "Vorauszahlungen 300,00 €", "Nachzahlung 300,00 €"], RowTexts(chart));
        Assert.Null(chart.GetAttribute("aria-label")); // caption and visible text say it all, nothing is read twice
        Assert.Contains("ergibt die Nachzahlung", chart.QuerySelector("figcaption")!.TextContent, StringComparison.Ordinal);
        Assert.Equal("width:100%", chart.QuerySelector("[data-row=share] .cost-chart-bar")!.GetAttribute("style"));
        Assert.Equal("width:50%", chart.QuerySelector("[data-row=prepayments] .cost-chart-bar")!.GetAttribute("style"));
        Assert.Equal("left:50%;width:50%", chart.QuerySelector("[data-row=result] .cost-chart-gap")!.GetAttribute("style"));
        Assert.DoesNotContain("error", chart.OuterHtml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Result_chart_names_a_credit()
    {
        MoveClockBehindTheBillingYear();
        var setup = await SetUpAsync((sp, _, lease) => PayMonthsAsync(sp, lease, 8)); // 600.00 € share, 8 × 100.00 € prepayments
        using var _ = setup.Tenant;

        var page = await setup.Tenant.GetAsync($"/portal/abrechnungen/{setup.OwnDispatch}");

        var chart = Chart(page, "result-chart");
        Assert.Contains("200,00 € Guthaben", page.TextWithStreaming, StringComparison.Ordinal);
        Assert.Equal(["Ihr Anteil 600,00 €", "Vorauszahlungen 800,00 €", "Guthaben 200,00 €"], RowTexts(chart));
        Assert.Contains("ergeben das Guthaben", chart.QuerySelector("figcaption")!.TextContent, StringComparison.Ordinal);
        Assert.Equal("left:75%;width:25%", chart.QuerySelector("[data-row=result] .cost-chart-gap")!.GetAttribute("style"));
    }

    [Fact]
    public async Task A_zero_balance_reads_as_balanced_on_top_while_the_balances_table_stays_as_it_is()
    {
        MoveClockBehindTheBillingYear();
        var setup = await SetUpAsync((sp, _, lease) => PayMonthsAsync(sp, lease, 6)); // 600.00 € share, 6 × 100.00 € prepayments
        using var _ = setup.Tenant;

        var page = await setup.Tenant.GetAsync($"/portal/abrechnungen/{setup.OwnDispatch}");

        Assert.Contains("Ausgeglichen", Chart(page, "statement-result").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Nachzahlung", Chart(page, "statement-result").TextContent, StringComparison.Ordinal);
        Assert.Equal("Ausgeglichen 0,00 €", RowTexts(Chart(page, "result-chart"))[^1]);
        Assert.All(
            Chart(page, "balances").QuerySelectorAll("tbody td[data-label=Ergebnis]"),
            c => Assert.Equal("0,00 € Nachzahlung", c.TextContent));
    }

    [Fact]
    public async Task Charts_show_the_own_cost_types_sorted_and_mark_only_the_previous_year_values_that_exist()
    {
        var setup = await SetUpAsync(async (sp, house, lease) =>
        {
            await CostsAsync(sp, house, 2026, "betrkv-13", 250m); // only this year: 150.00 €, the neighbour 100.00 €
            await CostsAsync(sp, house, 2025, "betrkv-01", 500m); // previous year: 300.00 €
            await CostsAsync(sp, house, 2025, "betrkv-10", 100m); // only the previous year: 60.00 €
            await DispatchPreviousYearAsync(sp, house, lease);
        });
        using var _ = setup.Tenant;

        var page = await setup.Tenant.GetAsync($"/portal/abrechnungen/{setup.OwnDispatch}");

        var costs = Chart(page, "cost-type-chart");
        Assert.Equal(["Laufende öffentliche Lasten (Grundsteuer) 80 % 600,00 €", "Sach- und Haftpflichtversicherung 20 % 150,00 €"], RowTexts(costs));
        Assert.Equal("width:80%", costs.QuerySelector("[data-cost-type=betrkv-01] .cost-chart-bar")!.GetAttribute("style")); // a full track is the whole share
        Assert.Equal("width:20%", costs.QuerySelector("[data-cost-type=betrkv-13] .cost-chart-bar")!.GetAttribute("style"));

        var comparison = Chart(page, "comparison-chart");
        var both = comparison.QuerySelector("[data-cost-type=betrkv-01]")!;
        Assert.Equal("width:100%", both.QuerySelector(".cost-chart-bar")!.GetAttribute("style"));
        Assert.Equal("--at:50%", both.QuerySelector(".cost-chart-prev")!.GetAttribute("style"));
        Assert.Equal("+300,00 €", both.QuerySelector(".cost-chart-value")!.TextContent);
        Assert.Equal("Aktuell 600,00 € · Vorjahr 300,00 €", both.QuerySelector(".cost-chart-detail")!.TextContent);
        Assert.Equal(["betrkv-01", "betrkv-13", "betrkv-10"], comparison.QuerySelectorAll(".cost-chart-row").Select(r => r.GetAttribute("data-cost-type")));
        Assert.Empty(comparison.QuerySelectorAll("[title]"));

        var newType = comparison.QuerySelector("[data-cost-type=betrkv-13]")!;
        Assert.NotNull(newType.QuerySelector(".cost-chart-bar"));
        Assert.Null(newType.QuerySelector(".cost-chart-prev"));
        Assert.Equal("neu", newType.QuerySelector(".cost-chart-value")!.TextContent);
        Assert.Equal("Aktuell 150,00 €", newType.QuerySelector(".cost-chart-detail")!.TextContent);

        var droppedType = comparison.QuerySelector("[data-cost-type=betrkv-10]")!;
        Assert.Null(droppedType.QuerySelector(".cost-chart-bar"));
        Assert.Equal("--at:10%", droppedType.QuerySelector(".cost-chart-prev")!.GetAttribute("style"));
        Assert.Equal("entfallen (Vorjahr 60,00 €)", droppedType.QuerySelector(".cost-chart-value")!.TextContent);

        // The table keeps its order and names a missing side; the sum stays a signed amount.
        var table = Chart(page, "comparison");
        Assert.Equal(
            ["+300,00 €", "neu", "entfallen"],
            table.QuerySelectorAll("tbody td[data-label=Unterschied]").Select(c => c.TextContent));
        Assert.Equal("+390,00 €", table.QuerySelector("tfoot th[data-label=Unterschied]")!.TextContent);

        Assert.NotNull(Element(page, "comparison")); // the tables stay
        foreach (var chart in new[] { Chart(page, "result-chart"), costs, comparison })
        {
            Assert.DoesNotContain("Weber", chart.OuterHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("Wohnung 2", chart.OuterHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("400,00", chart.OuterHtml, StringComparison.Ordinal); // the neighbour's shares
            Assert.DoesNotContain("100,00", chart.OuterHtml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Without_a_previous_statement_there_is_no_comparison_chart()
    {
        var setup = await SetUpAsync();
        using var _ = setup.Tenant;

        var page = await setup.Tenant.GetAsync($"/portal/abrechnungen/{setup.OwnDispatch}");

        Assert.NotNull(Element(page, "result-chart"));
        Assert.NotNull(Element(page, "cost-type-chart"));
        Assert.Null(Element(page, "comparison-chart"));
    }

    [Theory]
    [InlineData(0, 0, 0)] // nothing to show
    [InlineData(-50, 0, -50)] // a credit only: no bar could show it, the summary above names it
    [InlineData(100, 50, 40)] // bars that contradict the stated result
    public async Task Result_chart_is_left_out_when_it_could_not_show_the_result(int share, int prepayments, int balance)
    {
        var html = await RenderAsync<StatementResultChart>(new() { ["Share"] = (decimal)share, ["Prepayments"] = (decimal)prepayments, ["Balance"] = (decimal)balance });

        Assert.Equal("", html.Trim());
    }

    [Fact]
    public async Task Result_chart_clamps_a_negative_share_and_keeps_the_real_amount()
    {
        var html = await RenderAsync<StatementResultChart>(new() { ["Share"] = -50m, ["Prepayments"] = 100m, ["Balance"] = -150m });

        var chart = Parse(html).QuerySelector("#result-chart")!;
        Assert.Null(chart.QuerySelector("[data-row=share] .cost-chart-bar"));
        Assert.Equal(["Ihr Anteil -50,00 €", "Vorauszahlungen 100,00 €", "Guthaben 150,00 €"], RowTexts(chart));
        Assert.Equal("left:0%;width:100%", chart.QuerySelector("[data-row=result] .cost-chart-gap")!.GetAttribute("style"));
    }

    [Fact]
    public async Task Result_chart_says_so_when_share_and_prepayments_are_equal()
    {
        var html = await RenderAsync<StatementResultChart>(new() { ["Share"] = 100m, ["Prepayments"] = 100m, ["Balance"] = 0m });

        var chart = Parse(html).QuerySelector("#result-chart")!;
        Assert.Equal("Ausgeglichen 0,00 €", RowTexts(chart)[^1]);
        Assert.Null(chart.QuerySelector(".cost-chart-gap"));
        Assert.DoesNotContain("Ergebnis: 0,00 € Nachzahlung", chart.OuterHtml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cost_chart_merges_lines_of_a_cost_type_clamps_credits_and_is_left_out_without_positive_shares()
    {
        var html = await RenderAsync<StatementCostChart>(new() { ["Lines"] = new[] { Line("a", "Klein", 10m), Line("b", "Gutschrift", -20m), Line("c", "Groß", 30m), Line("a", "Klein", 30m) } });

        var chart = Parse(html).QuerySelector("#cost-type-chart")!;
        Assert.Equal(["Klein 57 % 40,00 €", "Groß 43 % 30,00 €", "Gutschrift -20,00 €"], RowTexts(chart));
        Assert.Equal("width:57.14%", chart.QuerySelector("[data-cost-type=a] .cost-chart-bar")!.GetAttribute("style"));
        Assert.Equal("width:42.86%", chart.QuerySelector("[data-cost-type=c] .cost-chart-bar")!.GetAttribute("style"));
        Assert.Null(chart.QuerySelector("[data-cost-type=b] .cost-chart-bar"));

        Assert.Equal("", (await RenderAsync<StatementCostChart>(new() { ["Lines"] = Array.Empty<PortalStatementLine>() })).Trim());
        Assert.Equal("", (await RenderAsync<StatementCostChart>(new() { ["Lines"] = new[] { Line("a", "Null", 0m), Line("b", "Gutschrift", -5m) } })).Trim());
    }

    [Fact]
    public async Task Comparison_chart_is_left_out_without_positive_values_and_keeps_a_real_previous_zero()
    {
        var period = Period.CalendarYear(2025);
        var nothing = new[] { new LineComparison("a", "Null", 0m, 0m), new LineComparison("b", "Gutschrift", null, -5m), new LineComparison("c", "Leer", null, null) };
        Assert.Equal("", (await RenderAsync<StatementComparisonChart>(new() { ["Lines"] = nothing, ["PreviousPeriod"] = period })).Trim());

        var html = await RenderAsync<StatementComparisonChart>(new() { ["Lines"] = new[] { new LineComparison("a", "Wert", 50m, 0m), new LineComparison("b", "Gutschrift", 100m, -5m) }, ["PreviousPeriod"] = period });

        var chart = Parse(html).QuerySelector("#comparison-chart")!;
        Assert.Equal("--at:0%", chart.QuerySelector("[data-cost-type=a] .cost-chart-prev")!.GetAttribute("style"));
        Assert.Equal("+50,00 €", chart.QuerySelector("[data-cost-type=a] .cost-chart-value")!.TextContent); // 0 is a real value, not "neu"
        Assert.Equal("--at:0%", chart.QuerySelector("[data-cost-type=b] .cost-chart-prev")!.GetAttribute("style"));
        Assert.Equal("Aktuell 100,00 € · Vorjahr -5,00 €", chart.QuerySelector("[data-cost-type=b] .cost-chart-detail")!.TextContent);
    }

    private static PortalStatementLine Line(string costType, string name, decimal share) => new(costType, name, false, "Wohnfläche", 1000m, share, "Rechenweg", []);

    private static async Task<string> RenderAsync<TComponent>(Dictionary<string, object?> parameters)
        where TComponent : IComponent
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters))).ToHtmlString());
    }

    private static AngleSharp.Html.Dom.IHtmlDocument Parse(string html) => new AngleSharp.Html.Parser.HtmlParser().ParseDocument(html);

    /// <summary>An element by id, including the parts delivered later by stream rendering.</summary>
    private static AngleSharp.Dom.IElement? Element(Page page, string id) =>
        page.Document.GetElementById(id)
        ?? page.Document.QuerySelectorAll("blazor-ssr template").OfType<AngleSharp.Html.Dom.IHtmlTemplateElement>().Select(t => t.Content.QuerySelector($"#{id}")).FirstOrDefault(e => e is not null);

    private static AngleSharp.Dom.IElement Chart(Page page, string id) => Element(page, id) ?? throw new InvalidOperationException($"Diagramm {id} fehlt.");

    /// <summary>The label and amount of each chart row as one text.</summary>
    private static string[] RowTexts(AngleSharp.Dom.IElement chart) =>
        [.. chart.QuerySelectorAll(".cost-chart-row").Select(r => string.Join(' ', $"{r.QuerySelector(".cost-chart-label")!.TextContent} {r.QuerySelector(".cost-chart-value")!.TextContent}".Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))];

    /// <summary>Every month of 2026 has its charge, whenever the test runs. Sign in after this: the one-time codes follow the clock.</summary>
    private void MoveClockBehindTheBillingYear()
    {
        var end = new DateTimeOffset(2027, 2, 1, 12, 0, 0, TimeSpan.Zero);
        if (App.Clock.GetUtcNow() < end)
        {
            App.Clock.SetUtcNow(end);
        }
    }

    /// <summary>The tenant pays rent and prepayment for the first months of 2026 (600.00 € each, of which 100.00 € prepayment).</summary>
    private static async Task PayMonthsAsync(IServiceProvider sp, Guid lease, int months)
    {
        await sp.GetRequiredService<IFinanceSettings>().SaveAsync(new FinanceSettingsInput { AccountingStart = new DateOnly(2026, 1, 1) }, Ct);
        var accounts = sp.GetRequiredService<ITenantAccounts>();
        await accounts.GenerateChargesAsync(null, Ct);
        for (var month = 1; month <= months; month++)
        {
            Assert.True((await accounts.RecordPaymentAsync(lease, new PaymentInput { Date = new DateOnly(2026, month, 5), Amount = 600m }, Ct)).Succeeded);
        }
    }

    private static async Task CostsAsync(IServiceProvider sp, Guid house, int year, string costType, decimal amount)
    {
        Assert.True((await sp.GetRequiredService<ICostAllocation>().SetRuleAsync(house, new CostRuleInput { CostType = costType, From = new DateOnly(2020, 1, 1), Key = AllocationKey.LivingArea }, Ct)).Succeeded);
        var receipt = await sp.GetRequiredService<IReceiptService>().CreateAsync(
            new ReceiptInput { SupplierName = "Versorger", InvoiceNumber = $"{costType}-{year}", InvoiceDate = new DateOnly(year, 12, 15), Gross = amount },
            [],
            new CostItemInput { CostType = costType, PropertyId = house, From = new DateOnly(year, 1, 1), To = new DateOnly(year, 12, 31) },
            Ct);
        Assert.True(receipt.Succeeded, string.Join(", ", receipt.Errors));
    }

    private static async Task DispatchPreviousYearAsync(IServiceProvider sp, Guid house, Guid lease)
    {
        var statement = await sp.GetRequiredService<IStatements>().ReleaseAsync(house, Period.CalendarYear(2025), true, null, Ct);
        Assert.True(statement.Succeeded, string.Join(", ", statement.Errors));
        using var letter = new MemoryStream("%PDF-1.4\n%%EOF\n"u8.ToArray());
        var file = await sp.GetRequiredService<IFileStore>().SaveAsync(letter, "abrechnung-vorjahr.pdf", Ct);
        var db = sp.GetRequiredService<ElocaroDbContext>();
        db.Add(new StatementDispatch(statement.Id!.Value, lease, DispatchChannel.Post, new DateOnly(2026, 2, 1), file.Id, null, null, 0m, 0m, null, null));
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>All link targets of the page, including the parts delivered later by stream rendering.</summary>
    private static List<string?> Hrefs(Page page) =>
        [.. page.Document.QuerySelectorAll("a")
            .Concat(page.Document.QuerySelectorAll("blazor-ssr template").OfType<AngleSharp.Html.Dom.IHtmlTemplateElement>().SelectMany(t => t.Content.QuerySelectorAll("a")))
            .Select(a => a.GetAttribute("href"))];

    private static List<string?> Values(Page page) => [.. page.Document.QuerySelectorAll("input").Select(i => i.GetAttribute("value"))];

    /// <param name="beforeRelease">Runs with the house and the own lease before the statement of 2026 is released (more costs, payments, earlier statements).</param>
    private async Task<Setup> SetUpAsync(Func<IServiceProvider, Guid, Guid, Task>? beforeRelease = null)
    {
        var (tenant, userId) = await SignedInAsAsync(Roles.Tenant);
        var ids = await App.WithScopeAsync(async sp =>
        {
            var properties = sp.GetRequiredService<IPropertyService>();
            var house = (await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct)).Id!.Value;
            var unit1 = (await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 1" }, Ct)).Id!.Value;
            var unit2 = (await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 2" }, Ct)).Id!.Value;
            await properties.SetUnitAreaAsync(unit1, new AreaInput { From = new DateOnly(2020, 1, 1), Area = 60m }, Ct);
            await properties.SetUnitAreaAsync(unit2, new AreaInput { From = new DateOnly(2020, 1, 1), Area = 40m }, Ct);
            var people = sp.GetRequiredService<IPersonService>();
            var anna = (await people.CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt" }, null, Ct)).Id!.Value;
            var ben = (await people.CreateAsync(new PersonInput { FirstName = "Ben", LastName = "Weber" }, null, Ct)).Id!.Value;
            var leases = sp.GetRequiredService<ILeaseService>();
            var leaseAnna = (await leases.CreateAsync(new LeaseCreation { RentalUnitId = unit1, Start = new DateOnly(2025, 1, 1), TenantIds = [anna], BaseRent = 500m, OperatingCostPrepayment = 100m }, Ct)).Id!.Value;
            var leaseBen = (await leases.CreateAsync(new LeaseCreation { RentalUnitId = unit2, Start = new DateOnly(2025, 1, 1), TenantIds = [ben], BaseRent = 400m, OperatingCostPrepayment = 80m }, Ct)).Id!.Value;
            Assert.True((await sp.GetRequiredService<ICostAllocation>().SetRuleAsync(house, new CostRuleInput { CostType = "betrkv-01", From = new DateOnly(2020, 1, 1), Key = AllocationKey.LivingArea }, Ct)).Succeeded);

            var db = sp.GetRequiredService<ElocaroDbContext>();
            (await db.Set<Person>().SingleAsync(p => p.Id == anna, Ct)).UserId = userId;
            await db.SaveChangesAsync(Ct);

            var files = sp.GetRequiredService<IFileStore>();
            using var invoice = new MemoryStream(System.Text.Encoding.ASCII.GetBytes($"%PDF-1.4\n% {Marker}\n%%EOF\n"));
            var invoiceFile = await files.SaveAsync(invoice, "grundsteuer.pdf", Ct);
            var receipt = await sp.GetRequiredService<IReceiptService>().CreateAsync(
                new ReceiptInput { SupplierName = "Stadt Köln", InvoiceNumber = "GS-2026", InvoiceDate = new DateOnly(2026, 12, 15), Gross = 1000m },
                [invoiceFile.Id],
                new CostItemInput { CostType = "betrkv-01", PropertyId = house, From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 12, 31) },
                Ct);
            Assert.True(receipt.Succeeded, string.Join(", ", receipt.Errors));
            if (beforeRelease is not null)
            {
                await beforeRelease(sp, house, leaseAnna);
            }

            var statement = await sp.GetRequiredService<IStatements>().ReleaseAsync(house, Period.CalendarYear(2026), true, null, Ct);
            Assert.True(statement.Succeeded, string.Join(", ", statement.Errors));

            using var letter = new MemoryStream("%PDF-1.4\n%%EOF\n"u8.ToArray());
            var letterFile = await files.SaveAsync(letter, "abrechnung.pdf", Ct);
            var own = new StatementDispatch(statement.Id!.Value, leaseAnna, DispatchChannel.Post, new DateOnly(2027, 2, 1), letterFile.Id, null, null, 600m, 0m, null, null);
            var neighbour = new StatementDispatch(statement.Id.Value, leaseBen, DispatchChannel.Post, new DateOnly(2027, 2, 1), letterFile.Id, null, null, 400m, 0m, null, null);
            db.AddRange(own, neighbour);
            await db.SaveChangesAsync(Ct);
            return (Statement: statement.Id.Value, Own: own.Id, Neighbour: neighbour.Id, Receipt: receipt.Id!.Value, ReceiptFile: invoiceFile.Id, LetterFile: letterFile.Id);
        });
        return new Setup(tenant, ids.Statement, ids.Own, ids.Neighbour, ids.Receipt, ids.ReceiptFile, ids.LetterFile);
    }

    private sealed record Setup(TestBrowser Tenant, Guid Statement, Guid OwnDispatch, Guid NeighbourDispatch, Guid Receipt, Guid ReceiptFile, Guid LetterFile);
}
