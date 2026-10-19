using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.Leases;
using MietHahn.Application.People;
using MietHahn.Application.Properties;
using MietHahn.Domain.Common;
using MietHahn.Web.Tests.Account;

namespace MietHahn.Web.Tests.Finance;

public class TenantAccountTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    [Fact]
    public async Task Set_up_accounting_rents_are_created_book_and_delete_payment()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        // Move-in on the 15th of the previous month; the app clock runs in real time.
        var today = GermanTime.Today(App.Clock.GetUtcNow());
        var previousMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        var lease = await LeaseAsync(previousMonth.AddDays(14));
        var days = DateTime.DaysInMonth(previousMonth.Year, previousMonth.Month);

        var account = await admin.GetAsync($"/verwaltung/vertraege/{lease}/konto");
        Assert.Contains("Noch kein Buchhaltungsbeginn festgelegt", account.Text, StringComparison.Ordinal);
        Assert.Contains("Buchhaltungsbeginn festlegen", (await admin.GetAsync("/verwaltung")).TextWithStreaming, StringComparison.Ordinal);

        var settings = await admin.SubmitAsync(await admin.GetAsync("/verwaltung/finanzen/einstellungen"), "finanzen", new Dictionary<string, string>
        {
            ["Data.AccountingStart"] = Iso(previousMonth),
            ["Data.Allocation"] = "Statutory",
        });
        Assert.Contains("Gespeichert. 2 Sollstellungen angelegt oder angeglichen.", settings.Text, StringComparison.Ordinal);

        account = await admin.GetAsync($"/verwaltung/vertraege/{lease}/konto");
        var prorated = decimal.Round(650m * (days - 14) / days, 2, MidpointRounding.AwayFromZero);
        Assert.Contains($"Kaltmiete {previousMonth:MM/yyyy} (anteilig {days - 14}/{days})", account.Text, StringComparison.Ordinal);
        Assert.Contains(Money(prorated + 650m), account.Document.QuerySelector("dl.angaben")!.TextContent, StringComparison.Ordinal);

        account = await admin.SubmitAsync(account, "zahlung", new Dictionary<string, string>
        {
            ["Payment.Date"] = Iso(today),
            ["Payment.Amount"] = "100.50",
            ["Payment.Purpose"] = "Miete",
        });
        Assert.Contains("Die Zahlung wurde gebucht.", account.Text, StringComparison.Ordinal);
        Assert.Contains(Money(prorated + 650m - 100.50m), account.Document.QuerySelector("dl.angaben")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("offen", account.Text, StringComparison.Ordinal);

        // Monthly rents have no delete button, the payment does.
        var buttons = account.Form("loeschen").QuerySelectorAll("button[name=Delete]").ToList();
        var button = Assert.Single(buttons);
        Assert.StartsWith("Payment:", button.GetAttribute("value"), StringComparison.Ordinal);
        account = await admin.SubmitAsync(account, "loeschen", new Dictionary<string, string> { ["Delete"] = button.GetAttribute("value")! });
        Assert.Contains("Die Buchung wurde gelöscht.", account.Text, StringComparison.Ordinal);
        Assert.Contains(Money(prorated + 650m), account.Document.QuerySelector("dl.angaben")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Book_credit_and_export_open_items_as_CSV()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var today = GermanTime.Today(App.Clock.GetUtcNow());
        var lease = await LeaseAsync(today.AddMonths(-3));
        var account = await admin.GetAsync($"/verwaltung/vertraege/{lease}/konto");

        account = await admin.SubmitAsync(account, "sollstellung", new Dictionary<string, string>
        {
            ["Charge.Kind"] = "AdditionalPayment",
            ["Charge.Due"] = Iso(today.AddDays(-20)),
            ["Charge.Amount"] = "212.40",
            ["Charge.Name"] = "=Nachzahlung NK 2025",
        });
        Assert.Contains("Die Buchung wurde angelegt.", account.Text, StringComparison.Ordinal);
        account = await admin.SubmitAsync(account, "sollstellung", new Dictionary<string, string>
        {
            ["Charge.Kind"] = "Refund",
            ["Charge.Due"] = Iso(today.AddDays(-10)),
            ["Charge.Amount"] = "12.40",
        });
        Assert.Contains("Erstattung (Guthaben)", account.Text, StringComparison.Ordinal);
        Assert.Contains(Money(200m), account.Document.QuerySelector("dl.angaben")!.TextContent, StringComparison.Ordinal);

        var list = await admin.GetAsync("/verwaltung/finanzen/offene-posten");
        Assert.Contains("=Nachzahlung NK 2025", list.Text, StringComparison.Ordinal);
        Assert.Contains("Summe offen: " + Money(200m), list.Text, StringComparison.Ordinal);

        using var csv = await admin.Http.GetAsync(new Uri("/verwaltung/finanzen/offene-posten.csv", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
        var content = Encoding.UTF8.GetString(await csv.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        var lines = content.TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Mietobjekt;Mieter;Art;Forderung;Fällig;Betrag;Offen;Tage überfällig", lines[0]);
        Assert.Equal($"Lindenweg 3, Wohnung 1;Anna Schmidt;Nachzahlung (Abrechnung);'=Nachzahlung NK 2025;{today.AddDays(-20):dd.MM.yyyy};212,40;200,00;20", lines[1]);
    }

    [Fact]
    public async Task Without_finance_permission_no_tenant_account()
    {
        var lease = await LeaseAsync(new DateOnly(2026, 1, 1));
        var (manager, _) = await SignedInAsAsync(Roles.Manager, [AccessRight.MasterData]);
        using var _ = manager;

        Assert.Equal("/konto/zugriff-verweigert", (await manager.GetAsync($"/verwaltung/vertraege/{lease}/konto", followRedirects: false)).RedirectLocation!.AbsolutePath);
        Assert.NotEqual(HttpStatusCode.OK, (await manager.GetAsync("/verwaltung/finanzen/offene-posten.csv", followRedirects: false)).Status);
        var details = await manager.GetAsync($"/verwaltung/vertraege/{lease}");
        Assert.DoesNotContain("Mieterkonto", details.Text, StringComparison.Ordinal);
    }

    private static string Money(decimal amount) => amount.ToString("#,##0.00 €", CultureInfo.GetCultureInfo("de-DE"));

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private async Task<Guid> LeaseAsync(DateOnly start) =>
        await App.WithScopeAsync(async sp =>
        {
            var ct = TestContext.Current.CancellationToken;
            var properties = sp.GetRequiredService<IPropertyService>();
            var property = (await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, ct)).Id!.Value;
            var apartment = (await properties.CreateUnitAsync(property, new UnitInput { Name = "Wohnung 1" }, ct)).Id!.Value;
            var tenant = (await sp.GetRequiredService<IPersonService>().CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt" }, null, ct)).Id!.Value;
            return (await sp.GetRequiredService<ILeaseService>().CreateAsync(new LeaseCreation { RentalUnitId = apartment, Start = start, TenantIds = [tenant], BaseRent = 650m }, ct)).Id!.Value;
        });
}
