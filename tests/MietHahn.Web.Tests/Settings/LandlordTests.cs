using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.Admin;
using MietHahn.Web.Tests.Account;

namespace MietHahn.Web.Tests.Settings;

public class LandlordTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private static readonly Dictionary<string, string> ValidDetails = new()
    {
        ["Data.Name"] = "Andy Hahn",
        ["Data.Street"] = "Musterweg 1",
        ["Data.PostalCode"] = "12345",
        ["Data.City"] = "Musterstadt",
        ["Data.Iban"] = "de02120300000000202051",
        ["Data.Bic"] = "byladem1001",
    };

    [Fact]
    public async Task Admin_creates_and_updates_the_landlord_profile()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        var empty = await admin.GetAsync("/verwaltung/einstellungen/vermieter");
        Assert.Contains("Noch kein Vermieterprofil hinterlegt", empty.Text, StringComparison.Ordinal);

        var saved = await admin.SubmitAsync(empty, "vermieter", ValidDetails);
        Assert.Contains("Die Vermieterangaben wurden gespeichert.", saved.Text, StringComparison.Ordinal);
        Assert.Equal("DE02 1203 0000 0000 2020 51", saved.Document.QuerySelector("#iban")!.GetAttribute("value"));
        Assert.Equal("BYLADEM1001", saved.Document.QuerySelector("#bic")!.GetAttribute("value"));

        var changed = await admin.SubmitAsync(saved, "vermieter", new Dictionary<string, string>(ValidDetails) { ["Data.City"] = "Neustadt" });
        Assert.Equal("Neustadt", changed.Document.QuerySelector("#ort")!.GetAttribute("value"));

        var profile = await App.WithScopeAsync(sp => sp.GetRequiredService<ILandlordProfileService>().GetAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Neustadt", profile!.City);
    }

    [Fact]
    public async Task Invalid_postal_code_is_rejected()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        var result = await admin.SubmitAsync(await admin.GetAsync("/verwaltung/einstellungen/vermieter"), "vermieter",
            new Dictionary<string, string>(ValidDetails) { ["Data.PostalCode"] = "1234" });

        Assert.Contains("Die Postleitzahl hat 5 Ziffern.", result.Text, StringComparison.Ordinal);
        Assert.Null(await App.WithScopeAsync(sp => sp.GetRequiredService<ILandlordProfileService>().GetAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Invalid_email_is_rejected_empty_is_allowed()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var page = await admin.GetAsync("/verwaltung/einstellungen/vermieter");

        var invalid = await admin.SubmitAsync(page, "vermieter", new Dictionary<string, string>(ValidDetails) { ["Data.Email"] = "kein-at" });
        var empty = await admin.SubmitAsync(page, "vermieter", new Dictionary<string, string>(ValidDetails) { ["Data.Email"] = "" });

        Assert.Contains("Bitte eine gültige E-Mail-Adresse angeben.", invalid.Text, StringComparison.Ordinal);
        Assert.Contains("Die Vermieterangaben wurden gespeichert.", empty.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Manager_without_system_permission_has_no_access()
    {
        var (manager, _) = await SignedInAsAsync(Roles.Manager);
        using var _ = manager;

        var response = await manager.GetAsync("/verwaltung/einstellungen/vermieter", followRedirects: false);

        Assert.Equal(HttpStatusCode.Redirect, response.Status);
        Assert.Equal("/konto/zugriff-verweigert", response.RedirectLocation!.AbsolutePath);
    }
}
