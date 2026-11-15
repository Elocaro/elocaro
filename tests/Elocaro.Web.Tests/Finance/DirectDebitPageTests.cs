using System.Globalization;
using System.Net;
using System.Text;
using AngleSharp.Html.Dom;
using Elocaro.Application.Accounts;
using Elocaro.Application.Common;
using Elocaro.Application.Finance;
using Elocaro.Application.Leases;
using Elocaro.Application.Payments;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Domain.Admin;
using Elocaro.Domain.Payments;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Web.Tests.Account;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Web.Tests.Finance;

public class DirectDebitPageTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private const string ListUrl = "/verwaltung/finanzen/lastschriften";
    private const string MandatesUrl = "/verwaltung/finanzen/lastschrift-mandate";
    private const string FileUrl = "/verwaltung/finanzen/lastschriften/datei";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_list_offers_the_open_rent_with_a_mandate_and_the_download_is_a_core_direct_debit_file()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        await SetUpAsync();

        var page = await admin.GetAsync(ListUrl);

        Assert.Contains("Anna Schmidt", page.Text, StringComparison.Ordinal);
        Assert.Contains("MND-WEB-1", page.Text, StringComparison.Ordinal);
        Assert.Contains("Lastschriftdatei herunterladen", page.Text, StringComparison.Ordinal);
        var choices = page.Document.QuerySelectorAll("input[name=auswahl]").OfType<IHtmlInputElement>().Select(i => i.Value).ToList();
        Assert.NotEmpty(choices);
        using var response = await PostAsync(admin, Fields(page, choices[0]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/xml", response.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("lastschriften-", response.Content.Headers.ContentDisposition!.FileName, StringComparison.Ordinal);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var xml = Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync(Ct));
        Assert.Contains("urn:iso:std:iso:20022:tech:xsd:pain.008.001.08", xml, StringComparison.Ordinal);
        Assert.Contains("<Cd>CORE</Cd>", xml, StringComparison.Ordinal);
        Assert.Contains("<MndtId>MND-WEB-1</MndtId>", xml, StringComparison.Ordinal);
        Assert.Contains("DE98ZZZ09999999999", xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_mandate_list_shows_reference_account_ending_and_state()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        await SetUpAsync();

        var page = await admin.GetAsync(MandatesUrl);

        Assert.Contains("MND-WEB-1", page.TextWithStreaming, StringComparison.Ordinal);
        Assert.Contains("…2051", page.TextWithStreaming, StringComparison.Ordinal);
        Assert.Contains("gültig", page.TextWithStreaming, StringComparison.Ordinal);
        Assert.DoesNotContain("DE02 1203", page.TextWithStreaming, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_choice_the_user_comes_back_with_the_reason_and_without_the_token_or_permission_nothing_is_created()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var adminBrowser = admin;
        await SetUpAsync();
        var page = await admin.GetAsync(ListUrl);

        using var noChoice = await PostAsync(admin, Fields(page));
        Assert.Equal(HttpStatusCode.Redirect, noChoice.StatusCode);
        Assert.StartsWith(ListUrl + "?fehler=", noChoice.Headers.Location!.OriginalString, StringComparison.Ordinal);

        using var noToken = await PostAsync(admin, []);
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);

        var (manager, _) = await SignedInAsAsync(Roles.Manager, [AccessRight.MasterData]);
        using var managerBrowser = manager;
        var denied = await manager.GetAsync(ListUrl, followRedirects: false);
        Assert.Equal("/konto/zugriff-verweigert", denied.RedirectLocation!.AbsolutePath);
        var start = await manager.GetAsync("/verwaltung");
        using var response = await PostAsync(manager, [new("__RequestVerificationToken", Token(start))]);
        Assert.Equal("/konto/zugriff-verweigert", response.Headers.Location!.AbsolutePath);
    }

    private async Task SetUpAsync() =>
        await App.WithScopeAsync<bool>(async sp =>
        {
            var properties = sp.GetRequiredService<IPropertyService>();
            var property = (await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct)).Id!.Value;
            var unit = (await properties.CreateUnitAsync(property, new UnitInput { Name = "Wohnung 1" }, Ct)).Id!.Value;
            var tenant = (await sp.GetRequiredService<IPersonService>().CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt" }, null, Ct)).Id!.Value;
            var start = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-2);
            var lease = (await sp.GetRequiredService<ILeaseService>().CreateAsync(new LeaseCreation { RentalUnitId = unit, Start = start, TenantIds = [tenant], BaseRent = 650m }, Ct)).Id!.Value;
            var db = sp.GetRequiredService<ElocaroDbContext>();
            db.Add(new LandlordProfile("Hans Müller", "Musterweg 1", "50667", "Köln") { Iban = "DE89 3704 0044 0532 0130 00", CreditorId = "DE98ZZZ09999999999" });
            await db.SaveChangesAsync(Ct);
            await sp.GetRequiredService<IFinanceSettings>().SaveAsync(new FinanceSettingsInput { AccountingStart = start }, Ct);
            await sp.GetRequiredService<ITenantAccounts>().GenerateChargesAsync(null, Ct);
            await sp.GetRequiredService<IDirectDebitMandates>().CreateAsync(
                lease, new MandateInput { Reference = "MND-WEB-1", DebtorName = "Anna Schmidt", DebtorIban = "DE02 1203 0000 0000 2020 51", SignedOn = start.AddMonths(-1), Sequence = MandateSequence.Recurring }, Ct);
            return true;
        });

    private static List<KeyValuePair<string, string>> Fields(Page page, params string[] choices)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("__RequestVerificationToken", Token(page)),
            new("einzugstag", DateOnly.FromDateTime(DateTime.Today.AddDays(3)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
        };
        fields.AddRange(choices.Select(c => new KeyValuePair<string, string>("auswahl", c)));
        return fields;
    }

    private static string Token(Page page) =>
        page.Document.QuerySelectorAll("input[name=__RequestVerificationToken]").OfType<IHtmlInputElement>().First().Value;

    private static async Task<HttpResponseMessage> PostAsync(TestBrowser browser, List<KeyValuePair<string, string>> fields)
    {
        using var content = new FormUrlEncodedContent(fields);
        return await browser.Http.PostAsync(new Uri(FileUrl, UriKind.Relative), content, Ct);
    }
}
