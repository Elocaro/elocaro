using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.People;
using MietHahn.Domain.Communication;
using MietHahn.Domain.People;
using MietHahn.Infrastructure.Persistence;
using MietHahn.Web.Tests.Account;

namespace MietHahn.Web.Tests.People;

public class PeopleTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    // Suggest unrelated persons immediately once they are no longer brand new (instead of after 12 months).
    protected override IDictionary<string, string?> Configuration => new Dictionary<string, string?> { ["Privacy:MonthsWithoutRelation"] = "0" };

    // People are created through the service; the interactive create and edit forms are covered by the browser tests.
    private async Task<Page> CreatePersonAsync(TestBrowser admin, PersonInput data, AddressInput? address = null)
    {
        var id = await App.WithScopeAsync(async sp => (await sp.GetRequiredService<IPersonService>().CreateAsync(data, address, TestContext.Current.CancellationToken)).Id!.Value);
        return await admin.GetAsync($"/verwaltung/personen/{id}");
    }

    private Task<Page> CreateAnnaAsync(TestBrowser admin) =>
        CreatePersonAsync(
            admin,
            new PersonInput { Salutation = Salutation.Mrs, FirstName = "Anna", LastName = "Schmidt", Email = "anna@example.de", Phone = "0221 123456" },
            new AddressInput { From = MietHahn.Domain.Common.GermanTime.Today(App.Clock.GetUtcNow()), Street = "Altweg 1", PostalCode = "50667", City = "Köln" });

    [Fact]
    public async Task Search_finds_by_iban_and_filter_and_the_address_history_is_shown()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var person = await CreateAnnaAsync(admin);
        var id = Guid.Parse(person.Path[(person.Path.LastIndexOf('/') + 1)..]);
        Assert.Contains("Altweg 1, 50667 Köln", person.Text, StringComparison.Ordinal);

        // Several IBANs, one invalid (the dialogs themselves are covered by the browser tests)
        await App.WithScopeAsync(async sp =>
        {
            var people = sp.GetRequiredService<IPersonService>();
            var ct = TestContext.Current.CancellationToken;
            Assert.False((await people.AddBankDetailsAsync(id, new BankDetailsInput { Iban = "DE02120300000000202052" }, ct)).Succeeded);
            Assert.True((await people.AddBankDetailsAsync(id, new BankDetailsInput { Iban = "de02120300000000202051" }, ct)).Succeeded);
            Assert.True((await people.AddBankDetailsAsync(id, new BankDetailsInput { Iban = "DE89370400440532013000", AccountHolder = "Hans Schmidt" }, ct)).Succeeded);

            // Move-in tomorrow: previous address stays in the history
            var tomorrow = MietHahn.Domain.Common.GermanTime.Today(App.Clock.GetUtcNow()).AddDays(1);
            Assert.True((await people.SetAddressAsync(id, new AddressInput { From = tomorrow, Street = "Lindenweg 3", PostalCode = "50667", City = "Köln", AddressLine2 = "Wohnung 1" }, ct)).Succeeded);
            return 0;
        });

        person = await admin.GetAsync(person.Path);
        Assert.Contains("DE02 1203 0000 0000 2020 51", person.Text, StringComparison.Ordinal);
        Assert.Contains("Hans Schmidt", person.Text, StringComparison.Ordinal);
        Assert.Contains("Frühere und künftige Anschriften", person.Text, StringComparison.Ordinal);
        Assert.Contains("Wohnung 1, Lindenweg 3, 50667 Köln", person.Text, StringComparison.Ordinal); // from tomorrow
        App.Clock.Advance(TimeSpan.FromHours(26)); // not 24: the day of the clock change has 25 hours
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
        var person = await CreatePersonAsync(admin, new PersonInput { LastName = "Klempner", Company = "Rohr & Co.", ContactType = ContactType.Tradesperson, Occupation = "Sanitär", Email = "rohr@example.de" });
        Assert.Contains("Handwerker (Sanitär)", person.Text, StringComparison.Ordinal);

        var id = Guid.Parse(person.Path[(person.Path.LastIndexOf('/') + 1)..]);
        await App.WithScopeAsync(async sp => (await sp.GetRequiredService<IPersonService>().SetUpAccessAsync(id, TestContext.Current.CancellationToken)).Succeeded);
        person = await admin.GetAsync(person.Path);

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
        var person = await CreateAnnaAsync(admin);
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
        var person = await CreateAnnaAsync(admin);
        var id = Guid.Parse(person.Path[(person.Path.LastIndexOf('/') + 1)..]);

        App.Clock.Advance(TimeSpan.FromMinutes(1));
        var suggestions = await admin.GetAsync("/verwaltung/datenschutz");
        Assert.True(suggestions.Text.Contains("Anna Schmidt", StringComparison.Ordinal), suggestions.Text);

        // The confirmation dialog is covered by the browser tests.
        await App.WithScopeAsync(async sp => (await sp.GetRequiredService<MietHahn.Application.Privacy.IPrivacyService>().AnonymizeAsync(id, TestContext.Current.CancellationToken)).Succeeded);

        var record = await admin.GetAsync(person.Path);
        Assert.DoesNotContain("Schmidt", record.Text, StringComparison.Ordinal);
        Assert.Contains("wurden am", record.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearbeiten", record.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_master_data_permission_no_access_to_people_and_access_report()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        var person = await CreateAnnaAsync(admin);
        admin.Dispose();
        var (manager, _) = await SignedInAsAsync(Roles.Manager, [AccessRight.Finance]);
        using var _ = manager;

        Assert.Equal("/konto/zugriff-verweigert", (await manager.GetAsync("/verwaltung/personen", followRedirects: false)).RedirectLocation!.AbsolutePath);
        Assert.Equal("/konto/zugriff-verweigert", (await manager.GetAsync(person.Path + "/auskunft", followRedirects: false)).RedirectLocation!.AbsolutePath);
        Assert.Equal("/konto/zugriff-verweigert", (await manager.GetAsync("/verwaltung/datenschutz", followRedirects: false)).RedirectLocation!.AbsolutePath);
    }
}
