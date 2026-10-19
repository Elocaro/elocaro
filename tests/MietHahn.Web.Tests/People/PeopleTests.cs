using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Domain.Communication;
using MietHahn.Infrastructure.Persistence;
using MietHahn.Web.Tests.Account;

namespace MietHahn.Web.Tests.People;

public class PeopleTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    // Suggest unrelated persons immediately once they are no longer brand new (instead of after 12 months).
    protected override IDictionary<string, string?> Configuration => new Dictionary<string, string?> { ["Privacy:MonthsWithoutRelation"] = "0" };

    private static readonly Dictionary<string, string> Anna = new()
    {
        ["Data.Salutation"] = "Mrs",
        ["Data.FirstName"] = "Anna",
        ["Data.LastName"] = "Schmidt",
        ["Data.Email"] = "anna@example.de",
        ["Data.Phone"] = "0221 123456",
        ["Address.Street"] = "Altweg 1",
        ["Address.PostalCode"] = "50667",
        ["Address.City"] = "Köln",
    };

    [Fact]
    public async Task Create_search_and_edit_tenant()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        var person = await admin.SubmitAsync(await admin.GetAsync("/verwaltung/personen/neu"), "person", Anna);
        Assert.Matches("^/verwaltung/personen/[0-9a-f-]{36}$", person.Path);
        Assert.Contains("Die Person wurde angelegt.", person.Text, StringComparison.Ordinal);
        Assert.Contains("Altweg 1, 50667 Köln", person.Text, StringComparison.Ordinal);

        // Multiple IBANs, one invalid
        var wrong = await admin.SubmitAsync(person, "iban", new Dictionary<string, string> { ["Bank.Iban"] = "DE02120300000000202052" });
        Assert.Contains("Bitte eine gültige IBAN angeben", wrong.Text, StringComparison.Ordinal);
        person = await admin.SubmitAsync(person, "iban", new Dictionary<string, string> { ["Bank.Iban"] = "de02120300000000202051" });
        person = await admin.SubmitAsync(person, "iban", new Dictionary<string, string> { ["Bank.Iban"] = "DE89370400440532013000", ["Bank.AccountHolder"] = "Hans Schmidt" });
        Assert.Contains("DE02 1203 0000 0000 2020 51", person.Text, StringComparison.Ordinal);
        Assert.Contains("Hans Schmidt", person.Text, StringComparison.Ordinal);

        // Each row has its own button in the shared form; the clicked one submits its id.
        var firstIban = person.Document.QuerySelector("button[name=RemoveId]")!.GetAttribute("value")!;
        person = await admin.SubmitAsync(person, "iban-remove", new Dictionary<string, string> { ["RemoveId"] = firstIban });
        Assert.Contains("Die IBAN wurde entfernt.", person.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("DE02 1203", person.Text, StringComparison.Ordinal);

        // Move-in: new address, previous address stays in the history
        person = await admin.SubmitAsync(person, "address", new Dictionary<string, string>
        {
            ["Address.From"] = MietHahn.Domain.Common.GermanTime.Today(App.Clock.GetUtcNow()).AddDays(1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            ["Address.Street"] = "Lindenweg 3",
            ["Address.PostalCode"] = "50667",
            ["Address.City"] = "Köln",
            ["Address.AddressLine2"] = "Wohnung 1",
        });
        Assert.Contains("Die Anschrift wurde gespeichert.", person.Text, StringComparison.Ordinal);
        Assert.Contains("Frühere und künftige Anschriften", person.Text, StringComparison.Ordinal);
        Assert.Contains("Wohnung 1, Lindenweg 3, 50667 Köln", person.Text, StringComparison.Ordinal); // from tomorrow
        App.Clock.Advance(TimeSpan.FromDays(1));
        Assert.Contains("Wohnung 1, Lindenweg 3, 50667 Köln (seit", (await admin.GetAsync(person.Path)).Text, StringComparison.Ordinal);

        Assert.Contains("Schmidt, Anna", (await admin.GetAsync("/verwaltung/personen?q=3704")).Text, StringComparison.Ordinal);
        Assert.Contains("Keine Treffer für „Meier“", (await admin.GetAsync("/verwaltung/personen?q=Meier")).Text, StringComparison.Ordinal);
        Assert.Contains("Keine Personen in dieser Auswahl.", (await admin.GetAsync("/verwaltung/personen?filter=Contacts")).Text, StringComparison.Ordinal);
        Assert.Contains("Schmidt, Anna", (await admin.GetAsync("/verwaltung/personen?filter=Unsinn")).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tradesperson_gets_access_with_matching_role()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var person = await admin.SubmitAsync(await admin.GetAsync("/verwaltung/personen/neu"), "person", new Dictionary<string, string>
        {
            ["Data.LastName"] = "Klempner",
            ["Data.Company"] = "Rohr & Co.",
            ["Data.ContactType"] = "Tradesperson",
            ["Data.Occupation"] = "Sanitär",
            ["Data.Email"] = "rohr@example.de",
        });
        Assert.Contains("Handwerker (Sanitär)", person.Text, StringComparison.Ordinal);

        person = await admin.SubmitAsync(person, "access");

        Assert.Contains("Der Zugang wurde eingerichtet.", person.Text, StringComparison.Ordinal);
        Assert.Contains("rohr@example.de (Handwerker)", person.Text, StringComparison.Ordinal);
        var mail = await App.WithScopeAsync(sp => sp.GetRequiredService<MietHahnDbContext>().Set<OutgoingMail>().SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(["rohr@example.de"], mail.To);
        Assert.Contains("Zugang", (await admin.GetAsync("/verwaltung/personen?filter=Contacts")).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gdpr_access_report_as_download()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var person = await admin.SubmitAsync(await admin.GetAsync("/verwaltung/personen/neu"), "person", Anna);
        var link = person.Document.QuerySelectorAll("a").Single(a => a.TextContent == "DSGVO-Auskunft").GetAttribute("href")!;

        using var response = await admin.Http.GetAsync(new Uri(link, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("dsgvo-auskunft-", response.Content.Headers.ContentDisposition!.FileNameStar ?? response.Content.Headers.ContentDisposition.FileName, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Schmidt", json.RootElement.GetProperty("person").GetProperty("lastName").GetString());
        Assert.Equal("Altweg 1", json.RootElement.GetProperty("person").GetProperty("addresses")[0].GetProperty("street").GetString());
    }

    [Fact]
    public async Task Suggestion_after_retention_period_and_anonymization()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var person = await admin.SubmitAsync(await admin.GetAsync("/verwaltung/personen/neu"), "person", Anna);
        var path = person.Path;

        App.Clock.Advance(TimeSpan.FromMinutes(1));
        var suggestions = await admin.GetAsync("/verwaltung/datenschutz");
        Assert.True(suggestions.Text.Contains("Anna Schmidt", StringComparison.Ordinal), suggestions.Text);

        var button = suggestions.Document.QuerySelector("button[name=PersonId]")!.GetAttribute("value")!;
        var afterwards = await admin.SubmitAsync(suggestions, "anonymize", new Dictionary<string, string> { ["PersonId"] = button });
        Assert.Contains("Die Person wurde anonymisiert.", afterwards.Text, StringComparison.Ordinal);

        var record = await admin.GetAsync(path);
        Assert.DoesNotContain("Schmidt", record.Text, StringComparison.Ordinal);
        Assert.Contains("wurden am", record.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearbeiten", record.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_master_data_permission_no_access_to_people_and_access_report()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        var person = await admin.SubmitAsync(await admin.GetAsync("/verwaltung/personen/neu"), "person", Anna);
        admin.Dispose();
        var (manager, _) = await SignedInAsAsync(Roles.Manager, [AccessRight.Finance]);
        using var _ = manager;

        Assert.Equal("/konto/zugriff-verweigert", (await manager.GetAsync("/verwaltung/personen", followRedirects: false)).RedirectLocation!.AbsolutePath);
        Assert.Equal("/konto/zugriff-verweigert", (await manager.GetAsync(person.Path + "/auskunft", followRedirects: false)).RedirectLocation!.AbsolutePath);
        Assert.Equal("/konto/zugriff-verweigert", (await manager.GetAsync("/verwaltung/datenschutz", followRedirects: false)).RedirectLocation!.AbsolutePath);
    }
}
