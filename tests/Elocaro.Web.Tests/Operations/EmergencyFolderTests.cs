using System.Net;
using System.Text;
using AngleSharp.Html.Dom;
using Elocaro.Application.Accounts;
using Elocaro.Application.Properties;
using Elocaro.Infrastructure.Audit;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Web.Tests.Account;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Web.Tests.Operations;

public class EmergencyFolderTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private const string Page = "/verwaltung/system/notfallmappe";
    private const string Create = "/verwaltung/system/notfallmappe/erzeugen";
    private const string FilePassword = "Das-ist-ein-langes-Passwort-2026";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Page_explains_the_folder_and_warns_that_the_password_cannot_be_recovered()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        var page = await admin.GetAsync(Page);

        Assert.Contains("Noch nie erzeugt.", page.Text, StringComparison.Ordinal);
        Assert.Contains("Das Passwort wird nirgends gespeichert.", page.Text, StringComparison.Ordinal);
        Assert.Contains("Es gibt keine Wiederherstellung", page.Text, StringComparison.Ordinal);
        Assert.Contains("getrennten Orten", page.Text, StringComparison.Ordinal);
        Assert.Contains("keine Passwörter", page.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_downloads_the_encrypted_folder_and_it_is_audited_without_the_password()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var page = await admin.GetAsync(Page);

        using var response = await PostAsync(admin, Fields(page, "create", FilePassword, FilePassword));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.StartsWith("elocaro-notfallmappe-", response.Content.Headers.ContentDisposition.FileNameStar, StringComparison.Ordinal);
        Assert.EndsWith(".html", response.Content.Headers.ContentDisposition.FileNameStar, StringComparison.Ordinal);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains("id=\"mappe-daten\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain(FilePassword, body, StringComparison.Ordinal);
        Assert.DoesNotContain("Max Mustermann", body, StringComparison.Ordinal);

        await App.WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ElocaroDbContext>();
            var entry = Assert.Single(await db.Set<AuditEntry>().Where(a => a.EntityType == "Notfallmappe").ToListAsync(Ct));
            Assert.Equal(Elocaro.Application.Audit.AuditAction.Read, entry.Action);
            Assert.DoesNotContain(FilePassword, entry.ChangesJson + entry.Purpose + entry.User, StringComparison.Ordinal);
            return 0;
        });

        var after = await admin.GetAsync(Page);
        Assert.DoesNotContain("Noch nie erzeugt.", after.Text, StringComparison.Ordinal);
        Assert.Contains("Zuletzt erzeugt am", after.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Short_or_different_passwords_lead_back_to_the_page_with_a_fixed_message_and_keep_the_notes()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var page = await admin.GetAsync(Page);

        using var tooShort = await PostAsync(admin, Fields(page, "create", "kurz", "kurz", contacts: "Frau Lange"));
        using var different = await PostAsync(admin, Fields(page, "create", FilePassword, FilePassword + "x", contacts: "Frau Lange"));

        Assert.Equal(HttpStatusCode.Redirect, tooShort.StatusCode);
        Assert.Equal(Page + "?fehler=passwort-kurz", tooShort.Headers.Location!.OriginalString);
        Assert.Equal(Page + "?fehler=passwort-ungleich", different.Headers.Location!.OriginalString);

        var back = await admin.GetAsync(Page + "?fehler=passwort-kurz");
        Assert.Contains("mindestens 12 Zeichen", back.Text, StringComparison.Ordinal);
        Assert.Contains("Frau Lange", back.Html, StringComparison.Ordinal);
        var unknown = await admin.GetAsync(Page + "?fehler=<script>alert(1)</script>");
        Assert.DoesNotContain("<script>alert(1)", unknown.Html, StringComparison.Ordinal); // the key is never echoed as markup
        Assert.DoesNotContain("id=\"folder-error\"", unknown.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Saving_only_stores_the_notes_and_creates_no_file()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var page = await admin.GetAsync(Page);

        using var response = await PostAsync(admin, Fields(page, "save", "", "", contacts: "Hausmeister Herr Krämer", location: "Aktenschrank links"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(Page + "?meldung=gespeichert", response.Headers.Location!.OriginalString);
        var again = await admin.GetAsync(Page + "?meldung=gespeichert");
        Assert.Contains("Die Angaben wurden gespeichert.", again.Text, StringComparison.Ordinal);
        Assert.Contains("Hausmeister Herr Krämer", again.Text, StringComparison.Ordinal);
        Assert.Contains("Noch nie erzeugt.", again.Text, StringComparison.Ordinal);
        await App.WithScopeAsync(async sp =>
        {
            Assert.Empty(await sp.GetRequiredService<ElocaroDbContext>().Set<AuditEntry>().Where(a => a.EntityType == "Notfallmappe").ToListAsync(Ct));
            return 0;
        });
    }

    [Fact]
    public async Task Without_antiforgery_token_the_request_is_rejected()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        using var response = await PostAsync(admin, [new("Action", "create"), new("Password", FilePassword), new("PasswordConfirmation", FilePassword)]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Without_system_permission_there_is_no_access_to_page_and_download()
    {
        var (manager, _) = await SignedInAsAsync(Roles.Manager, [AccessRight.MasterData, AccessRight.Finance]);
        using var _ = manager;

        var page = await manager.GetAsync(Page, followRedirects: false);
        Assert.Equal("/konto/zugriff-verweigert", page.RedirectLocation!.AbsolutePath);

        // Token from an allowed page so that only authorization applies
        var start = await manager.GetAsync("/verwaltung");
        var token = start.Document.QuerySelectorAll("input[name=__RequestVerificationToken]").OfType<IHtmlInputElement>().First().Value;
        using var response = await PostAsync(manager, [new("__RequestVerificationToken", token), new("Action", "create"), new("Password", FilePassword), new("PasswordConfirmation", FilePassword)]);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/konto/zugriff-verweigert", response.Headers.Location!.AbsolutePath);
    }

    [Fact]
    public async Task Without_sign_in_the_download_redirects_to_sign_in()
    {
        using var browser = App.Browser();

        using var response = await PostAsync(browser, []);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/konto/anmelden", response.Headers.Location!.AbsolutePath);
    }

    [Fact]
    public async Task Dashboard_reminds_the_admin_until_a_folder_has_been_created()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        await App.WithScopeAsync(async sp =>
        {
            Assert.True((await sp.GetRequiredService<IPropertyService>().CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct)).Succeeded);
            return 0;
        });

        var before = await admin.GetAsync("/verwaltung");
        Assert.Contains("Notfallmappe erstellen", before.TextWithStreaming, StringComparison.Ordinal);

        var page = await admin.GetAsync(Page);
        using var _created = await PostAsync(admin, Fields(page, "create", FilePassword, FilePassword));
        var after = await admin.GetAsync("/verwaltung");
        Assert.DoesNotContain("Notfallmappe erstellen", after.TextWithStreaming, StringComparison.Ordinal);
    }

    private static List<KeyValuePair<string, string>> Fields(Page page, string action, string password, string confirmation, string contacts = "", string location = "")
    {
        var form = page.Document.Forms.Single(f => f.GetAttribute("action") == Create);
        var token = form.Elements.OfType<IHtmlInputElement>().Single(i => i.Name == "__RequestVerificationToken").Value;
        return
        [
            new("__RequestVerificationToken", token),
            new("Action", action),
            new("ImportantContacts", contacts),
            new("DocumentLocation", location),
            new("FurtherNotes", ""),
            new("Password", password),
            new("PasswordConfirmation", confirmation),
        ];
    }

    private static async Task<HttpResponseMessage> PostAsync(TestBrowser browser, List<KeyValuePair<string, string>> fields)
    {
        using var content = new FormUrlEncodedContent(fields);
        return await browser.Http.PostAsync(new Uri(Create, UriKind.Relative), content, Ct);
    }
}
