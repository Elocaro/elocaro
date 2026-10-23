using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.Admin;
using MietHahn.Web.Tests.Account;

namespace MietHahn.Web.Tests.AccessControl;

public class ModuleTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private Task<bool> SaveAsync(params AppModule[] enabled) =>
        App.WithScopeAsync(async sp =>
        {
            await sp.GetRequiredService<IModuleState>().SaveAsync(enabled, completeSetup: true);
            return true;
        });

    [Fact]
    public async Task A_switched_off_module_disappears_from_navigation_and_its_pages_answer_404()
    {
        var (browser, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = browser;
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/verwaltung/vpi")).Status);
        Assert.Contains("Bank", (await browser.GetAsync("/verwaltung")).Document.QuerySelector(".sidebar")!.TextContent, StringComparison.Ordinal);

        await SaveAsync(AppModule.Tickets);

        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync("/verwaltung/vpi")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync("/verwaltung/bank/konten")).Status);
        var start = await browser.GetAsync("/verwaltung");
        Assert.Equal(HttpStatusCode.OK, start.Status);
        var sidebar = start.Document.QuerySelector(".sidebar")!.TextContent;
        Assert.DoesNotContain("Bank", sidebar, StringComparison.Ordinal);
        Assert.Contains("Tickets", sidebar, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/verwaltung/objekte")).Status);
    }

    [Fact]
    public async Task Modules_page_lists_every_module_with_its_description()
    {
        var (browser, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = browser;

        var page = await browser.GetAsync("/verwaltung/einstellungen/module");

        Assert.Equal(HttpStatusCode.OK, page.Status);
        var text = page.Document.Body!.TextContent;
        foreach (var info in ModuleCatalog.All)
        {
            Assert.Contains(info.Title, text, StringComparison.Ordinal);
            Assert.Contains(info.Purpose, text, StringComparison.Ordinal);
        }
    }
}

public class ModuleOptionPagesTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private Task<bool> SaveAsync(params AppOption[] options) =>
        App.WithScopeAsync(async sp =>
        {
            await sp.GetRequiredService<IModuleState>().SaveAsync(Enum.GetValues<AppModule>(), completeSetup: true, options);
            return true;
        });

    [Fact]
    public async Task Lease_form_offers_only_the_chosen_rent_models_and_billing_types()
    {
        var (browser, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = browser;

        var all = (await browser.GetAsync("/verwaltung/vertraege/neu")).Document.Body!.TextContent;
        Assert.Contains("Mietmodell", all, StringComparison.Ordinal);
        Assert.Contains("Nebenkosten", all, StringComparison.Ordinal);

        await SaveAsync(AppOption.BillingPrepayment);

        var reduced = (await browser.GetAsync("/verwaltung/vertraege/neu")).Document.Body!.TextContent;
        Assert.DoesNotContain("Mietmodell", reduced, StringComparison.Ordinal);
        Assert.DoesNotContain("Nebenkosten", reduced, StringComparison.Ordinal);
        Assert.Contains("Betriebskosten-Vorauszahlung", reduced, StringComparison.Ordinal);
        Assert.DoesNotContain("Betriebskostenpauschale", reduced, StringComparison.Ordinal);

        await SaveAsync(AppOption.BillingFlatRate, AppOption.RentModelIndex);

        var flat = (await browser.GetAsync("/verwaltung/vertraege/neu")).Document.Body!.TextContent;
        Assert.Contains("Mietmodell", flat, StringComparison.Ordinal);
        Assert.DoesNotContain("Betriebskosten-Vorauszahlung", flat, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Index_pages_and_tab_exist_only_while_index_rent_is_on()
    {
        var (browser, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = browser;
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/verwaltung/vpi")).Status);

        await SaveAsync(AppOption.BillingPrepayment);

        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync("/verwaltung/vpi")).Status);
        var contracts = await browser.GetAsync("/verwaltung/vertraege");
        Assert.DoesNotContain("Verbraucherpreisindex", contracts.Document.Body!.TextContent, StringComparison.Ordinal);

        await SaveAsync(AppOption.BillingPrepayment, AppOption.RentModelIndex);

        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/verwaltung/vpi")).Status);
    }
}

public class ModuleWizardTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    protected override bool SetupCompleted => false;

    [Fact]
    public async Task Dashboard_leads_admins_to_the_wizard_until_it_is_completed()
    {
        var (browser, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = browser;

        var redirect = await browser.GetAsync("/verwaltung", followRedirects: false);
        Assert.Equal(HttpStatusCode.Redirect, redirect.Status);
        Assert.Equal("/verwaltung/einrichtung", redirect.RedirectLocation!.OriginalString);
        var wizard = await browser.GetAsync("/verwaltung/einrichtung");
        Assert.Equal(HttpStatusCode.OK, wizard.Status);
        Assert.Contains("Willkommen bei MietHahn", wizard.Document.Body!.TextContent, StringComparison.Ordinal);

        await App.CompleteSetupAsync();

        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/verwaltung", followRedirects: false)).Status);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/verwaltung/einrichtung")).Status);
    }
}
