using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.Documents;
using MietHahn.Application.Files;
using MietHahn.Application.Finance;
using MietHahn.Application.Leases;
using MietHahn.Application.People;
using MietHahn.Application.Properties;
using MietHahn.Domain.Admin;
using MietHahn.Domain.Common;
using MietHahn.Domain.Communication;
using MietHahn.Domain.Files;
using MietHahn.Web.Tests.Account;

namespace MietHahn.Web.Tests.Finance;

public class DunningPagesTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private const string TenantEmail = "anna@example.de";

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddScoped<IPdfGenerator, FakePdfGenerator>();

    [Fact]
    public async Task Proposal_is_reviewed_in_the_dialog_and_only_the_approval_creates_and_sends_the_letter()
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
        Assert.Empty(await MailsAsync());

        // Dialog: preview PDF, recipient, no sending before approval
        var dialog = await admin.GetAsync(list.Document.QuerySelectorAll("a").Single(a => a.TextContent == "Prüfen und freigeben").GetAttribute("href")!);
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

        // Updating the preview does not approve anything.
        var again = await admin.SubmitAsync(dialog, "issue", new Dictionary<string, string> { ["Action"] = "preview", ["Data.SendByMail"] = "true" });
        Assert.Equal(HttpStatusCode.OK, again.Status);
        Assert.Contains("Freigeben", again.Text, StringComparison.Ordinal);
        Assert.Empty(await MailsAsync());

        var approved = await admin.SubmitAsync(again, "issue", new Dictionary<string, string> { ["Action"] = "issue", ["Data.SendByMail"] = "true" });
        Assert.Contains("Die Mahnung wurde freigegeben und per E-Mail versendet.", approved.Text, StringComparison.Ordinal);
        var download = approved.Document.QuerySelectorAll("a").Single(a => a.TextContent == "PDF herunterladen").GetAttribute("href")!;
        using (var file = await admin.Http.GetAsync(new Uri(download, UriKind.Relative), TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, file.StatusCode);
            Assert.Equal("application/pdf", file.Content.Headers.ContentType!.MediaType);
        }

        var mail = Assert.Single(await MailsAsync());
        Assert.Equal([TenantEmail], mail.To);
        Assert.Equal("Zahlungserinnerung zu Ihrer Mietzahlung", mail.Subject);
        Assert.Single(mail.AttachmentFileIds);

        var overview = await admin.GetAsync("/verwaltung/finanzen/mahnungen");
        Assert.Contains("Keine Mahnvorschläge", overview.Text, StringComparison.Ordinal);
        Assert.Contains("E-Mail", overview.Document.QuerySelector("#issued-heading")!.ParentElement!.TextContent, StringComparison.Ordinal);

        // Document library and account show the dunning.
        var documents = await admin.GetAsync("/verwaltung/dokumente");
        Assert.Contains("Zahlungserinnerung vom", documents.Text, StringComparison.Ordinal);
        var account = await admin.GetAsync($"/verwaltung/vertraege/{await LeaseIdAsync()}/konto");
        Assert.Contains("Zahlungserinnerung", account.Document.QuerySelector("#mahnwesen")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Approval_without_email_gives_a_download_for_postal_delivery()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        await SetUpAsync(monthsAgo: 1, new FinanceSettingsInput { AccountingStart = FirstOfMonth(-1) }, email: null);
        var dialog = await admin.GetAsync((await admin.GetAsync("/verwaltung/finanzen/mahnungen")).Document.QuerySelectorAll("a").Single(a => a.TextContent == "Prüfen und freigeben").GetAttribute("href")!);
        Assert.Contains("keine hinterlegt", dialog.Text, StringComparison.Ordinal);

        var approved = await admin.SubmitAsync(dialog, "issue", new Dictionary<string, string> { ["Action"] = "issue" });

        Assert.Contains("Die Mahnung wurde freigegeben. Das PDF steht zum Download für den Postversand bereit.", approved.Text, StringComparison.Ordinal);
        Assert.Contains("nicht per E-Mail versendet", approved.Text, StringComparison.Ordinal);
        Assert.Empty(await MailsAsync());
    }

    [Fact]
    public async Task Payment_before_approval_removes_the_proposal_and_a_stale_approval_sends_nothing()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        await SetUpAsync(monthsAgo: 1, new FinanceSettingsInput { AccountingStart = FirstOfMonth(-1) });
        var lease = await LeaseIdAsync();
        var dialog = await admin.GetAsync((await admin.GetAsync("/verwaltung/finanzen/mahnungen")).Document.QuerySelectorAll("a").Single(a => a.TextContent == "Prüfen und freigeben").GetAttribute("href")!);

        // The tenant pays everything while the dialog is open.
        var account = await admin.GetAsync($"/verwaltung/vertraege/{lease}/konto");
        var open = decimal.Parse(account.Document.QuerySelector("dl.details dd strong")!.TextContent.Replace(".", "", StringComparison.Ordinal).Replace(" €", "", StringComparison.Ordinal), CultureInfo.GetCultureInfo("de-DE"));
        var today = GermanTime.Today(App.Clock.GetUtcNow());
        await admin.SubmitAsync(account, "payment", new Dictionary<string, string>
        {
            ["Payment.Date"] = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["Payment.Amount"] = open.ToString(CultureInfo.InvariantCulture),
        });

        var stale = await admin.SubmitAsync(dialog, "issue", new Dictionary<string, string> { ["Action"] = "issue", ["Data.SendByMail"] = "true" });

        Assert.Contains("Der Rückstand besteht nicht mehr", stale.Text, StringComparison.Ordinal);
        Assert.Empty(await MailsAsync());
        Assert.Contains("Keine Mahnvorschläge", (await admin.GetAsync("/verwaltung/finanzen/mahnungen")).Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Mahnvorschlag", (await admin.GetAsync("/verwaltung")).TextWithStreaming, StringComparison.Ordinal);
        Assert.Contains("Mahnung nicht gefunden", (await admin.GetAsync(dialog.Path)).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dismissing_a_proposal_and_hold_per_tenancy()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        await SetUpAsync(monthsAgo: 1, new FinanceSettingsInput { AccountingStart = FirstOfMonth(-1) });
        var lease = await LeaseIdAsync();

        // Hold from the tenant account (instalment agreement)
        var account = await admin.GetAsync($"/verwaltung/vertraege/{lease}/konto");
        Assert.Contains("Mahnvorschlag:", account.Document.QuerySelector("#mahnwesen")!.TextContent, StringComparison.Ordinal);
        account = await admin.SubmitAsync(account, "hold", new Dictionary<string, string> { ["Hold.Reason"] = "Ratenvereinbarung" });
        Assert.Contains("Die Mahnsperre wurde gesetzt.", account.Text, StringComparison.Ordinal);
        Assert.Contains("Ratenvereinbarung", account.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Mahnvorschlag:", account.Text, StringComparison.Ordinal);

        var overview = await admin.GetAsync("/verwaltung/finanzen/mahnungen");
        Assert.Contains("Keine Mahnvorschläge", overview.Text, StringComparison.Ordinal);
        Assert.Contains("Ratenvereinbarung", overview.Text, StringComparison.Ordinal);

        overview = await admin.SubmitAsync(overview, "holds", new Dictionary<string, string> { ["EndHold"] = overview.Document.QuerySelector("button[name=EndHold]")!.GetAttribute("value")! });
        Assert.Contains("Die Mahnsperre wurde aufgehoben.", overview.Text, StringComparison.Ordinal);
        Assert.Contains("Keine aktive Mahnsperre.", overview.Text, StringComparison.Ordinal);
        Assert.Contains("Prüfen und freigeben", overview.Text, StringComparison.Ordinal);

        // Dismiss
        var dialog = await admin.GetAsync(overview.Document.QuerySelectorAll("a").Single(a => a.TextContent == "Prüfen und freigeben").GetAttribute("href")!);
        var dismissed = await admin.SubmitAsync(dialog, "dismiss");
        Assert.Contains("Der Vorschlag wurde verworfen.", dismissed.Text, StringComparison.Ordinal);
        Assert.Contains("Keine Mahnvorschläge", dismissed.Text, StringComparison.Ordinal);
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
        Assert.Contains("MietHahn kündigt nie automatisch", overview.Text, StringComparison.Ordinal);
        Assert.Contains("Rückstand mit Kündigungsrelevanz", (await admin.GetAsync("/verwaltung")).TextWithStreaming, StringComparison.Ordinal);
        var account = await admin.GetAsync($"/verwaltung/vertraege/{lease}/konto");
        Assert.Contains("Hinweis Kündigungsrelevanz", account.Document.QuerySelector("#mahnwesen")!.TextContent, StringComparison.Ordinal);
        var dialog = await admin.GetAsync(overview.Document.QuerySelectorAll("a").Single(a => a.TextContent == "Prüfen und freigeben").GetAttribute("href")!);
        Assert.Contains("Kündigungsrelevanz:", dialog.Text, StringComparison.Ordinal);
        Assert.Empty(await MailsAsync());
    }

    [Fact]
    public async Task Fee_interest_and_base_rate_are_maintained_in_the_finance_settings()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        var page = await admin.GetAsync("/verwaltung/finanzen/einstellungen");
        Assert.Contains("keine Rechtsberatung", page.Text, StringComparison.Ordinal);
        Assert.Equal("5", ((AngleSharp.Html.Dom.IHtmlInputElement)page.Document.QuerySelector("#dunning-grace-days")!).Value);

        var saved = await admin.SubmitAsync(page, "finance", new Dictionary<string, string>
        {
            ["Data.Allocation"] = "Statutory",
            ["Data.DunningGraceDays"] = "3",
            ["Data.DaysUntilFirstNotice"] = "10",
            ["Data.DaysUntilSecondNotice"] = "12",
            ["Data.DunningDeadlineDays"] = "9",
            ["Data.DunningMinimumAmount"] = "10.00",
            ["Data.FirstNoticeFee"] = "5.00",
            ["Data.SecondNoticeFee"] = "7.50",
            ["Data.ChargeDefaultInterest"] = "true",
            ["Data.BaseRate"] = "2.27",
            ["Data.BaseRateAsOf"] = "2026-07-01",
            ["Data.InterestMarginPoints"] = "5",
        });
        Assert.Contains("Gespeichert.", saved.Text, StringComparison.Ordinal);

        var settings = await App.WithScopeAsync(sp => sp.GetRequiredService<IFinanceSettings>().GetAsync(TestContext.Current.CancellationToken));
        Assert.Equal(3, settings.DunningGraceDays);
        Assert.Equal(10, settings.DaysUntilFirstNotice);
        Assert.Equal(12, settings.DaysUntilSecondNotice);
        Assert.Equal(9, settings.DunningDeadlineDays);
        Assert.Equal(10m, settings.DunningMinimumAmount);
        Assert.Equal(5m, settings.FirstNoticeFee);
        Assert.Equal(7.5m, settings.SecondNoticeFee);
        Assert.True(settings.ChargeDefaultInterest);
        Assert.Equal(2.27m, settings.BaseRate);
        Assert.Equal(new DateOnly(2026, 7, 1), settings.BaseRateAsOf);
        Assert.Equal(5m, settings.InterestMarginPoints);

        var invalid = await admin.SubmitAsync(saved, "finance", new Dictionary<string, string> { ["Data.DunningGraceDays"] = "400" });
        Assert.Contains("Die Karenzzeit liegt zwischen 0 und 60 Tagen.", invalid.Text, StringComparison.Ordinal);
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
            var db = sp.GetRequiredService<MietHahn.Infrastructure.Persistence.MietHahnDbContext>();
            db.Add(new LandlordProfile("Andy Hahn", "Musterweg 1", "12345", "Musterstadt") { Iban = "DE02 1203 0000 0000 2020 51" });
            await db.SaveChangesAsync(ct);
            await sp.GetRequiredService<IFinanceSettings>().SaveAsync(settings, ct);
            await sp.GetRequiredService<ITenantAccounts>().GenerateChargesAsync(null, ct);
            return 0;
        });
    }

    /// <summary>Stand-in for Gotenberg: stores a minimal PDF.</summary>
    private sealed class FakePdfGenerator(IFileStore fileStore) : IPdfGenerator
    {
        private static readonly byte[] Pdf = "%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF\n"u8.ToArray();

        public Task<byte[]> GenerateAsync(PdfRequest job, CancellationToken cancellationToken = default) => Task.FromResult(Pdf);

        public async Task<StoredFile> GenerateAndStoreAsync(PdfRequest job, string fileName, CancellationToken cancellationToken = default)
        {
            using var content = new MemoryStream(Pdf);
            return await fileStore.SaveAsync(content, fileName, cancellationToken);
        }
    }
}
