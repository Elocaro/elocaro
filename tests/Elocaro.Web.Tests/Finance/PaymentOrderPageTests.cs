using System.Net;
using System.Text;
using AngleSharp.Html.Dom;
using Elocaro.Application.Accounts;
using Elocaro.Application.Common;
using Elocaro.Application.People;
using Elocaro.Application.Receipts;
using Elocaro.Domain.Admin;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Web.Tests.Account;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Web.Tests.Finance;

public class PaymentOrderPageTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private const string PageUrl = "/verwaltung/finanzen/ueberweisungen";
    private const string FileUrl = "/verwaltung/finanzen/ueberweisungen/datei";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_list_offers_an_open_receipt_and_the_download_is_a_sepa_file()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var receipt = await SetUpAsync();

        var page = await admin.GetAsync(PageUrl);

        Assert.Contains("Offene Rechnungen", page.Text, StringComparison.Ordinal);
        Assert.Contains("Schmidt Heizung", page.Text, StringComparison.Ordinal);
        Assert.Contains("Überweisungsdatei herunterladen", page.Text, StringComparison.Ordinal);
        using var response = await PostAsync(admin, Fields(page, $"receipt:{receipt}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/xml", response.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("ueberweisungen-", response.Content.Headers.ContentDisposition!.FileName, StringComparison.Ordinal);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var xml = Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync(Ct));
        Assert.Contains("urn:iso:std:iso:20022:tech:xsd:pain.001.001.09", xml, StringComparison.Ordinal);
        Assert.Contains("<InstdAmt Ccy=\"EUR\">595.00</InstdAmt>", xml, StringComparison.Ordinal);
        Assert.Contains("DE02120300000000202051", xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_choice_the_user_comes_back_to_the_list_with_the_reason()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        await SetUpAsync();
        var page = await admin.GetAsync(PageUrl);

        using var response = await PostAsync(admin, Fields(page));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith(PageUrl + "?fehler=", response.Headers.Location!.OriginalString, StringComparison.Ordinal);
        var again = await admin.GetAsync(response.Headers.Location.OriginalString);
        Assert.Contains("keine Überweisung ausgewählt", again.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_the_antiforgery_token_or_the_finance_permission_nothing_is_created()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var adminBrowser = admin;
        using var noToken = await PostAsync(admin, []);
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);

        var (manager, _) = await SignedInAsAsync(Roles.Manager, [AccessRight.MasterData]);
        using var managerBrowser = manager;
        var page = await manager.GetAsync(PageUrl, followRedirects: false);
        Assert.Equal("/konto/zugriff-verweigert", page.RedirectLocation!.AbsolutePath);
        var start = await manager.GetAsync("/verwaltung");
        using var response = await PostAsync(manager, [new("__RequestVerificationToken", Token(start))]);
        Assert.Equal("/konto/zugriff-verweigert", response.Headers.Location!.AbsolutePath);
    }

    private async Task<Guid> SetUpAsync() =>
        await App.WithScopeAsync(async sp =>
        {
            var people = sp.GetRequiredService<IPersonService>();
            var supplier = (await people.CreateAsync(new PersonInput { LastName = "Schmidt Heizung", Company = "Schmidt Heizung GmbH" }, null, Ct)).Id!.Value;
            await people.AddBankDetailsAsync(supplier, new BankDetailsInput { Iban = "DE02120300000000202051" }, Ct);
            var db = sp.GetRequiredService<ElocaroDbContext>();
            db.Add(new LandlordProfile("Hans Müller", "Musterweg 1", "50667", "Köln") { Iban = "DE89 3704 0044 0532 0130 00" });
            await db.SaveChangesAsync(Ct);
            return (await sp.GetRequiredService<IReceiptService>().CreateAsync(new ReceiptInput
            {
                SupplierId = supplier,
                InvoiceNumber = "R-2026-117",
                InvoiceDate = new DateOnly(2026, 11, 20),
                Gross = 595m,
                Net = 500m,
                Vat = 95m,
                DueOn = new DateOnly(2026, 12, 4),
            }, [], null, Ct)).Id!.Value;
        });

    private static List<KeyValuePair<string, string>> Fields(Page page, params string[] choices)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("__RequestVerificationToken", Token(page)),
            new("ausfuehrung", DateOnly.FromDateTime(DateTime.Today.AddYears(1)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
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
