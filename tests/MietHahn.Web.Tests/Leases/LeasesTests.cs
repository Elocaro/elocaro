using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.People;
using MietHahn.Application.Properties;
using MietHahn.Domain.Common;
using MietHahn.Web.Tests.Account;

namespace MietHahn.Web.Tests.Leases;

public class LeasesTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private DateOnly Today => GermanTime.Today(App.Clock.GetUtcNow());

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Create_lease_increase_rent_terminate_and_cancel()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var (property, apartment, anna, ben) = await MasterDataAsync();
        var start = new DateOnly(Today.Year, Today.Month, 1);

        var newItem = await admin.GetAsync($"/verwaltung/vertraege/neu?mietobjekt={apartment}&beginn={Iso(start)}");
        Assert.Equal(apartment.ToString(), newItem.Document.QuerySelector("#mietobjekt option[selected]")!.GetAttribute("value"));
        var lease = await admin.SubmitAsync(newItem, "vertrag", new Dictionary<string, string>
        {
            ["Form.Tenant1"] = anna.ToString(),
            ["Form.Tenant2"] = ben.ToString(),
            ["Form.BaseRent"] = "650",
            ["Form.OperatingCostPrepayment"] = "180",
            ["Form.HeatingCostPrepayment"] = "90.50",
            ["Form.People"] = "2",
            ["Form.Deposit"] = "1950",
        });
        Assert.Contains("Der Vertrag wurde angelegt.", lease.Text, StringComparison.Ordinal);
        Assert.Contains("Anna Schmidt, Ben Weber", lease.Text, StringComparison.Ordinal);
        Assert.Contains("920,50 €", lease.Text, StringComparison.Ordinal);
        Assert.Contains("1.950,00 €", lease.Text, StringComparison.Ordinal);

        // Belegung in der Objektübersicht
        Assert.Contains("Anna Schmidt, Ben Weber", (await admin.GetAsync($"/verwaltung/objekte/{property}")).Text, StringComparison.Ordinal);

        // Mieterhöhung ab Stichtag: alter Betrag bleibt im Verlauf
        var increase = start.AddMonths(2);
        lease = await admin.SubmitAsync(lease, "bestandteil", new Dictionary<string, string>
        {
            ["Component.Kind"] = "BaseRent",
            ["Component.From"] = Iso(increase),
            ["Component.Amount"] = "700",
        });
        Assert.Contains("Der Betrag wurde gespeichert.", lease.Text, StringComparison.Ordinal);
        Assert.Contains("Verlauf", lease.Text, StringComparison.Ordinal);
        Assert.Contains("700,00 €", lease.Text, StringComparison.Ordinal);
        Assert.Contains($"{increase.AddDays(-1):dd.MM.yyyy}", lease.Text, StringComparison.Ordinal);

        // Kündigung durch den Mieter: Termin nach § 573c
        var incoming = new DateOnly(Today.Year, Today.Month, 1).AddMonths(1).AddDays(14); // nach dem 3. Werktag
        lease = await admin.SubmitAsync(lease, "kuendigung", new Dictionary<string, string> { ["Termination.ReceivedOn"] = Iso(incoming), ["Termination.By"] = "Tenant" });
        var expected = MietHahn.Domain.Leases.Lease.TerminationDateForTenant(incoming);
        Assert.Contains($"endet am {expected:dd.MM.yyyy}", lease.Text, StringComparison.Ordinal);

        lease = await admin.SubmitAsync(lease, "kuendigung-aufheben");
        Assert.Contains("unbefristet", lease.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Second_lease_in_same_period_is_rejected()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var (_, apartment, anna, ben) = await MasterDataAsync();
        var fields = new Dictionary<string, string> { ["Form.Tenant1"] = anna.ToString(), ["Form.BaseRent"] = "650" };

        await admin.SubmitAsync(await admin.GetAsync($"/verwaltung/vertraege/neu?mietobjekt={apartment}&beginn={Iso(Today)}"), "vertrag", fields);
        var second = await admin.SubmitAsync(
            await admin.GetAsync($"/verwaltung/vertraege/neu?mietobjekt={apartment}&beginn={Iso(Today.AddMonths(3))}"),
            "vertrag",
            new Dictionary<string, string>(fields) { ["Form.Tenant1"] = ben.ToString() });

        Assert.Contains("Für dieses Mietobjekt gibt es in diesem Zeitraum bereits einen Vertrag.", second.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_tenant_and_base_rent_the_form_reports_errors()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var (_, apartment, _, _) = await MasterDataAsync();

        var errors = await admin.SubmitAsync(await admin.GetAsync($"/verwaltung/vertraege/neu?mietobjekt={apartment}&beginn={Iso(Today)}"), "vertrag");

        Assert.Contains("Bitte den Mieter wählen.", errors.Text, StringComparison.Ordinal);
        Assert.Contains("Bitte die Kaltmiete angeben.", errors.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tenant_change_move_out_new_address_and_move_in_prefilled()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var (_, apartment, anna, ben) = await MasterDataAsync();
        var lease = await admin.SubmitAsync(
            await admin.GetAsync($"/verwaltung/vertraege/neu?mietobjekt={apartment}&beginn={Iso(Today.AddMonths(-12))}"),
            "vertrag",
            new Dictionary<string, string> { ["Form.Tenant1"] = anna.ToString(), ["Form.BaseRent"] = "650" });
        var end = Today.AddDays(3);

        var moveOut = await admin.SubmitAsync(await admin.GetAsync(lease.Path + "/auszug"), "auszug", new Dictionary<string, string>
        {
            ["Input.End"] = Iso(end),
            ["Input.NewStreet"] = "Neue Straße 5",
            ["Input.NewPostalCode"] = "10115",
            ["Input.NewCity"] = "Berlin",
        });

        Assert.Contains($"endet am {end:dd.MM.yyyy}", moveOut.Text, StringComparison.Ordinal);
        var moveIn = moveOut.Document.QuerySelectorAll("a").Single(a => a.TextContent == "Neuen Vertrag anlegen").GetAttribute("href")!;
        Assert.Contains($"beginn={Iso(end.AddDays(1))}", moveIn, StringComparison.Ordinal);

        var newItem = await admin.SubmitAsync(await admin.GetAsync(moveIn), "vertrag", new Dictionary<string, string> { ["Form.Tenant1"] = ben.ToString(), ["Form.BaseRent"] = "690" });
        Assert.Contains("Der Vertrag wurde angelegt.", newItem.Text, StringComparison.Ordinal);

        var history = await admin.GetAsync($"/verwaltung/einheiten/{apartment}");
        Assert.Contains("Ben Weber", history.Text, StringComparison.Ordinal);
        Assert.Contains("Anna Schmidt", history.Text, StringComparison.Ordinal);

        App.Clock.Advance(TimeSpan.FromDays(5)); // nach dem Auszug gilt die neue Anschrift
        var record = await admin.GetAsync($"/verwaltung/personen/{anna}");
        Assert.Contains("Neue Straße 5, 10115 Berlin", record.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Add_and_remove_guarantor()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var (_, apartment, anna, ben) = await MasterDataAsync();
        var lease = await admin.SubmitAsync(
            await admin.GetAsync($"/verwaltung/vertraege/neu?mietobjekt={apartment}&beginn={Iso(Today)}"),
            "vertrag",
            new Dictionary<string, string> { ["Form.Tenant1"] = anna.ToString(), ["Form.BaseRent"] = "650" });

        lease = await admin.SubmitAsync(lease, "partei", new Dictionary<string, string> { ["NewParty.PersonId"] = ben.ToString(), ["NewParty.Role"] = "Guarantor" });
        Assert.Contains("Die Person wurde hinzugefügt.", lease.Text, StringComparison.Ordinal);
        Assert.Contains("Bürge", lease.Text, StringComparison.Ordinal);

        lease = await admin.SubmitAsync(lease, "partei-entfernen", new Dictionary<string, string> { ["RemovePersonId"] = ben.ToString() });
        Assert.Contains("Die Person wurde entfernt.", lease.Text, StringComparison.Ordinal);
        Assert.Empty(lease.Document.QuerySelectorAll("button[name=RemovePersonId]")); // einziger Mieter ist nicht entfernbar
    }

    [Fact]
    public async Task Without_master_data_permission_no_access()
    {
        var (manager, _) = await SignedInAsAsync(Roles.Manager, [AccessRight.Finance]);
        using var _ = manager;

        Assert.Equal("/konto/zugriff-verweigert", (await manager.GetAsync("/verwaltung/vertraege", followRedirects: false)).RedirectLocation!.AbsolutePath);
    }

    private async Task<(Guid Property, Guid Apartment, Guid Anna, Guid Ben)> MasterDataAsync() =>
        await App.WithScopeAsync(async sp =>
        {
            var ct = TestContext.Current.CancellationToken;
            var properties = sp.GetRequiredService<IPropertyService>();
            var property = (await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, ct)).Id!.Value;
            var apartment = (await properties.CreateUnitAsync(property, new UnitInput { Name = "Wohnung 1" }, ct)).Id!.Value;
            var people = sp.GetRequiredService<IPersonService>();
            var anna = (await people.CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt" }, null, ct)).Id!.Value;
            var ben = (await people.CreateAsync(new PersonInput { FirstName = "Ben", LastName = "Weber" }, null, ct)).Id!.Value;
            return (property, apartment, anna, ben);
        });
}
