using System.Globalization;
using System.Net;
using Elocaro.Application.Accounts;
using Elocaro.Application.Documents;
using Elocaro.Application.Files;
using Elocaro.Application.Finance;
using Elocaro.Application.Leases;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Domain.Admin;
using Elocaro.Domain.Common;
using Elocaro.Domain.Communication;
using Elocaro.Domain.Files;
using Elocaro.Web.Tests.Account;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Web.Tests.Finance;

public class DunningPagesTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private const string TenantEmail = "anna@example.de";

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddScoped<IPdfGenerator, FakePdfGenerator>();

    // Approving, dismissing and holds are operated in FinanceBrowserTests; here the pages are read after service calls.
    [Fact]
    public async Task Proposal_dialog_shows_recipient_and_preview_and_nothing_is_sent_before_approval()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        await SetUpAsync(monthsAgo: 1, new FinanceSettingsInput { AccountingStart = FirstOfMonth(-1), ReminderFee = 0m });

        var start = await admin.GetAsync("/verwaltung");
        Assert.Contains("1 Mahnvorschlag prüfen", start.TextWithStreaming, StringComparison.Ordinal);
        var list = await admin.GetAsync("/verwaltung/finanzen/mahnungen");
        Assert.Contains("Basisvorlage", list.Text, StringComparison.Ordinal);
        Assert.Contains("Zahlungserinnerung", list.Text, StringComparison.Ordinal);
        Assert.Contains("Anna Schmidt", list.Text, StringComparison.Ordinal);
        Assert.Contains("Noch keine Mahnung freigegeben.", list.Text, StringComparison.Ordinal);

        var proposal = await App.WithScopeAsync(async sp => (await sp.GetRequiredService<IDunning>().OverviewAsync(TestContext.Current.CancellationToken)).Proposals.Single().Id);
        var dialog = await admin.GetAsync($"/verwaltung/finanzen/mahnungen/{proposal}");
        Assert.Contains("Es wird nichts versendet, bevor Sie „Freigeben“ wählen.", dialog.Text, StringComparison.Ordinal);
        Assert.Contains(TenantEmail, dialog.Text, StringComparison.Ordinal);
        var preview = dialog.Document.QuerySelector("iframe.document-preview")!.GetAttribute("src")!;
        Assert.EndsWith("vorschau.pdf?gebuehr=true&zinsen=true", preview, StringComparison.Ordinal);
        using (var pdf = await admin.Http.GetAsync(new Uri(preview, UriKind.Relative), TestContext.Current.CancellationToken))
        {
            Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
            Assert.Contains("no-store", pdf.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
        }

        Assert.Empty(await MailsAsync());
    }

    [Fact]
    public async Task Issued_reminder_appears_in_the_overview_the_document_library_and_the_tenant_account()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        await SetUpAsync(monthsAgo: 1, new FinanceSettingsInput { AccountingStart = FirstOfMonth(-1), ReminderFee = 0m });
        var proposal = await App.WithScopeAsync(async sp => (await sp.GetRequiredService<IDunning>().OverviewAsync(TestContext.Current.CancellationToken)).Proposals.Single().Id);

        var issued = await App.WithScopeAsync(sp => sp.GetRequiredService<IDunning>().IssueAsync(proposal, new DunningIssueInput { SendByMail = true }, TestContext.Current.CancellationToken));
        Assert.True(issued.Succeeded, string.Join(" ", issued.Errors));

        var mail = Assert.Single(await MailsAsync());
        Assert.Equal([TenantEmail], mail.To);
        var overview = await admin.GetAsync("/verwaltung/finanzen/mahnungen");
        Assert.Contains("Keine Mahnvorschläge", overview.Text, StringComparison.Ordinal);
        Assert.Contains("E-Mail", overview.Document.QuerySelector("#issued-heading")!.ParentElement!.TextContent, StringComparison.Ordinal);
        Assert.Contains("Zahlungserinnerung vom", (await admin.GetAsync("/verwaltung/dokumente")).Text, StringComparison.Ordinal);
        var account = await admin.GetAsync($"/verwaltung/vertraege/{await LeaseIdAsync()}/konto");
        Assert.Contains("Zahlungserinnerung", account.Document.QuerySelector("main")!.TextContent, StringComparison.Ordinal);
        var download = (await admin.GetAsync($"/verwaltung/finanzen/mahnungen/{proposal}")).Document.QuerySelectorAll("a").Single(a => a.TextContent == "PDF herunterladen").GetAttribute("href")!;
        using var file = await admin.Http.GetAsync(new Uri(download, UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
    }

    [Fact]
    public async Task Termination_relevance_is_shown_as_a_hint_only()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        await SetUpAsync(monthsAgo: 3, new FinanceSettingsInput { AccountingStart = FirstOfMonth(-3) });
        var lease = await LeaseIdAsync();

        var overview = await admin.GetAsync("/verwaltung/finanzen/mahnungen");
        Assert.Contains("Hinweis Kündigungsrelevanz", overview.Text, StringComparison.Ordinal);
        Assert.Contains("§ 543 Abs. 2 Nr. 3 BGB", overview.Text, StringComparison.Ordinal);
        Assert.Contains("Elocaro kündigt nie automatisch", overview.Text, StringComparison.Ordinal);
        Assert.Contains("Rückstand mit Kündigungsrelevanz", (await admin.GetAsync("/verwaltung")).TextWithStreaming, StringComparison.Ordinal);
        var account = await admin.GetAsync($"/verwaltung/vertraege/{lease}/konto");
        Assert.Contains("Hinweis Kündigungsrelevanz", account.Document.QuerySelector("main")!.TextContent, StringComparison.Ordinal);
        var dialog = await admin.GetAsync(overview.Document.QuerySelectorAll("a").Single(a => a.TextContent == "Prüfen und freigeben").GetAttribute("href")!);
        Assert.Contains("Kündigungsrelevanz:", dialog.Text, StringComparison.Ordinal);
        Assert.Empty(await MailsAsync());
    }

    [Fact]
    public async Task Without_finance_permission_there_is_no_dunning()
    {
        await SetUpAsync(monthsAgo: 1, new FinanceSettingsInput { AccountingStart = FirstOfMonth(-1) });
        var (manager, _) = await SignedInAsAsync(Roles.Manager, [AccessRight.MasterData]);
        using var _ = manager;
        var proposal = await App.WithScopeAsync(async sp =>
        {
            var overview = await sp.GetRequiredService<IDunning>().OverviewAsync(TestContext.Current.CancellationToken);
            return overview.Proposals.Single().Id;
        });

        Assert.Equal("/konto/zugriff-verweigert", (await manager.GetAsync("/verwaltung/finanzen/mahnungen", followRedirects: false)).RedirectLocation!.AbsolutePath);
        Assert.Equal("/konto/zugriff-verweigert", (await manager.GetAsync($"/verwaltung/finanzen/mahnungen/{proposal}", followRedirects: false)).RedirectLocation!.AbsolutePath);
        Assert.NotEqual(HttpStatusCode.OK, (await manager.GetAsync($"/verwaltung/finanzen/mahnungen/{proposal}/vorschau.pdf", followRedirects: false)).Status);
        Assert.DoesNotContain("Mahnwesen", (await manager.GetAsync("/verwaltung")).TextWithStreaming, StringComparison.Ordinal);
    }

    private static DateOnly FirstOfMonth(int offset) => new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).AddMonths(offset);

    private Task<Guid> LeaseIdAsync() =>
        App.WithScopeAsync(async sp => (await sp.GetRequiredService<ILeaseService>().ListAsync(true, TestContext.Current.CancellationToken)).Single().Id);

    private async Task<List<OutgoingMail>> MailsAsync()
    {
        var mails = new List<OutgoingMail>();
        await App.WithDbAsync(async db => mails.AddRange(await db.Set<OutgoingMail>().AsNoTracking().ToListAsync(TestContext.Current.CancellationToken)));
        return mails;
    }

    /// <summary>Lease from <paramref name="monthsAgo"/> months ago (unpaid), landlord profile and finance settings.</summary>
    private async Task SetUpAsync(int monthsAgo, FinanceSettingsInput settings, string? email = TenantEmail)
    {
        await App.WithScopeAsync(async sp =>
        {
            var ct = TestContext.Current.CancellationToken;
            var properties = sp.GetRequiredService<IPropertyService>();
            var property = (await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, ct)).Id!.Value;
            var apartment = (await properties.CreateUnitAsync(property, new UnitInput { Name = "Wohnung 1" }, ct)).Id!.Value;
            var tenant = (await sp.GetRequiredService<IPersonService>().CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt", Email = email }, null, ct)).Id!.Value;
            await sp.GetRequiredService<ILeaseService>().CreateAsync(new LeaseCreation { RentalUnitId = apartment, Start = FirstOfMonth(-monthsAgo), TenantIds = [tenant], BaseRent = 650m }, ct);
            var db = sp.GetRequiredService<Elocaro.Infrastructure.Persistence.ElocaroDbContext>();
            db.Add(new LandlordProfile("Max Mustermann", "Musterweg 1", "12345", "Musterstadt") { Iban = "DE02 1203 0000 0000 2020 51" });
            await db.SaveChangesAsync(ct);
            await sp.GetRequiredService<IFinanceSettings>().SaveAsync(settings, ct);
            await sp.GetRequiredService<ITenantAccounts>().GenerateChargesAsync(null, ct);
            return 0;
        });
    }
}
