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

public class LeaseFormOptionTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    [Fact]
    public async Task Deposit_and_guarantor_fields_follow_the_profile_and_dunning_settings_follow_the_module()
    {
        var (browser, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = browser;
        var full = (await browser.GetAsync("/verwaltung/vertraege/neu")).Document.Body!.TextContent;
        Assert.Contains("Kaution – Notiz", full, StringComparison.Ordinal);
        Assert.Contains("Bürge", full, StringComparison.Ordinal);
        Assert.Contains("Karenzzeit", (await browser.GetAsync("/verwaltung/finanzen/einstellungen")).Document.Body!.TextContent, StringComparison.Ordinal);

        await App.WithScopeAsync(async sp =>
        {
            await sp.GetRequiredService<IModuleState>().SaveAsync(Enum.GetValues<AppModule>().Where(m => m != AppModule.Dunning), completeSetup: true, [AppOption.BillingPrepayment]);
            return true;
        });

        var reduced = (await browser.GetAsync("/verwaltung/vertraege/neu")).Document.Body!.TextContent;
        Assert.DoesNotContain("Kaution – Notiz", reduced, StringComparison.Ordinal);
        Assert.DoesNotContain("Bürge", reduced, StringComparison.Ordinal);
        Assert.DoesNotContain("Karenzzeit", (await browser.GetAsync("/verwaltung/finanzen/einstellungen")).Document.Body!.TextContent, StringComparison.Ordinal);
    }
}

public class DunningOptionPagesTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    [Fact]
    public async Task Fee_and_interest_fields_appear_only_when_chosen()
    {
        var (browser, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = browser;
        var full = (await browser.GetAsync("/verwaltung/finanzen/einstellungen")).Document.Body!.TextContent;
        Assert.Contains("Gebühr 1. Mahnung", full, StringComparison.Ordinal);
        Assert.Contains("Basiszinssatz (% p. a.)", full, StringComparison.Ordinal);

        await App.WithScopeAsync(async sp =>
        {
            await sp.GetRequiredService<IModuleState>().SaveAsync(Enum.GetValues<AppModule>(), completeSetup: true, [AppOption.BillingPrepayment]);
            return true;
        });

        var reduced = (await browser.GetAsync("/verwaltung/finanzen/einstellungen")).Document.Body!.TextContent;
        Assert.Contains("Karenzzeit", reduced, StringComparison.Ordinal);
        Assert.DoesNotContain("Gebühr 1. Mahnung", reduced, StringComparison.Ordinal);
        Assert.DoesNotContain("Basiszinssatz (% p. a.)", reduced, StringComparison.Ordinal);
    }
}

public class TaxOptionPagesTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    [Fact]
    public async Task Tax_parts_can_be_switched_off_one_by_one_and_the_area_stays_while_pages_remain()
    {
        var (browser, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = browser;
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/verwaltung/finanzen/darlehen")).Status);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/verwaltung/finanzen/datev")).Status);

        await App.WithScopeAsync(async sp =>
        {
            await sp.GetRequiredService<IModuleState>().SaveAsync(Enum.GetValues<AppModule>(), completeSetup: true, [AppOption.TaxDepreciation]);
            return true;
        });

        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync("/verwaltung/finanzen/darlehen")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync("/verwaltung/finanzen/datev")).Status);
        var lines = await browser.GetAsync("/verwaltung/finanzen/anlage-v-zeilen");
        Assert.Equal(HttpStatusCode.OK, lines.Status);
        var sidebar = lines.Document.QuerySelector(".sidebar")!;
        Assert.Contains("Darlehen & Steuer", sidebar.TextContent, StringComparison.Ordinal);
        Assert.Contains("/verwaltung/finanzen/anlage-v-zeilen", sidebar.InnerHtml, StringComparison.Ordinal);
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
        Assert.Contains("Schritt 1 von", wizard.Document.Body!.TextContent, StringComparison.Ordinal);

        await App.CompleteSetupAsync();

        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/verwaltung", followRedirects: false)).Status);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/verwaltung/einrichtung")).Status);
    }
}
