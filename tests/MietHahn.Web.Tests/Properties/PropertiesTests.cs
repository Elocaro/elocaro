using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.Properties;
using MietHahn.Web.Tests.Account;
using SkiaSharp;

namespace MietHahn.Web.Tests.Properties;

public class PropertiesTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private static readonly Dictionary<string, string> House = new()
    {
        ["Data.Name"] = "Lindenweg 3",
        ["Data.Street"] = "Lindenweg 3",
        ["Data.PostalCode"] = "50667",
        ["Data.City"] = "Köln",
        ["Data.YearBuilt"] = "1965",
        ["Data.EnergySource"] = "NaturalGas",
    };

    [Fact]
    public async Task Create_building_with_apartments_and_shared_flat_via_the_UI()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        Assert.Contains("Noch keine Objekte", (await admin.GetAsync("/verwaltung/objekte")).Text, StringComparison.Ordinal);

        var property = await admin.SubmitAsync(await admin.GetAsync("/verwaltung/objekte/neu"), "property", House);
        Assert.Matches("^/verwaltung/objekte/[0-9a-f-]{36}$", property.Path);
        Assert.Contains("Das Objekt wurde angelegt.", property.Text, StringComparison.Ordinal);
        Assert.Contains("Lindenweg 3, 50667 Köln", property.Text, StringComparison.Ordinal);
        Assert.Contains("Zentralheizung, Erdgas", property.Text, StringComparison.Ordinal);

        property = await admin.SubmitAsync(property, "area", new Dictionary<string, string> { ["Input.From"] = "2026-01-01", ["Input.Area"] = "400", ["Input.SecondaryArea"] = "30.5" });
        Assert.Contains("Die Fläche wurde gespeichert.", property.Text, StringComparison.Ordinal);
        Assert.Contains("30,5 m²", property.Text, StringComparison.Ordinal);

        // Number field: browser displays it German-style, value in the HTML always with a dot (prefilled with the current value)
        var supplement = property.Document.QuerySelector("#area-supplement")!;
        Assert.Equal("number", supplement.GetAttribute("type"));
        Assert.Equal("0.01", supplement.GetAttribute("step"));
        Assert.Equal("30.5", supplement.GetAttribute("value"));

        foreach (var number in new[] { 1, 2, 3, 4 })
        {
            property = await admin.SubmitAsync(property, "unit", new Dictionary<string, string> { ["Data.Name"] = $"Wohnung {number}", ["Data.Location"] = number <= 2 ? "EG" : "OG" });
            Assert.Contains("Die Einheit wurde hinzugefügt.", property.Text, StringComparison.Ordinal);
        }

        var propertyPath = property.Path;
        var sharedFlatLink = property.Document.QuerySelectorAll("a").Single(a => a.TextContent == "Wohnung 4").GetAttribute("href")!;
        var sharedFlat = await admin.GetAsync(sharedFlatLink);
        sharedFlat = await admin.SubmitAsync(sharedFlat, "area", new Dictionary<string, string> { ["Input.From"] = "2026-01-01", ["Input.Area"] = "110" });
        Assert.Contains("110 m²", sharedFlat.Text, StringComparison.Ordinal);
        foreach (var number in new[] { 1, 2, 3, 4 })
        {
            sharedFlat = await admin.SubmitAsync(sharedFlat, "room", new Dictionary<string, string> { ["Data.Name"] = $"Zimmer {number}", ["Data.Furnishing"] = "Furnished" });
            Assert.Contains("Das Zimmer wurde hinzugefügt.", sharedFlat.Text, StringComparison.Ordinal);
        }

        var overview = await admin.GetAsync(propertyPath);
        Assert.Equal(7, overview.Document.QuerySelectorAll("tbody .status-pending").Count(e => e.TextContent == "Leerstand"));
        Assert.Contains("WG", overview.Text, StringComparison.Ordinal);
        // Without living area for apartments 1–3 and rooms: plausibility hints
        Assert.Contains("Wohnung 1: Keine Wohnfläche", overview.Text, StringComparison.Ordinal);

        var list = await admin.GetAsync("/verwaltung/objekte");
        Assert.Contains("0 von 7 vermietet", list.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Area_change_from_effective_date_shows_the_history()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var property = await admin.SubmitAsync(await admin.GetAsync("/verwaltung/objekte/neu"), "property", House);
        property = await admin.SubmitAsync(property, "unit", new Dictionary<string, string> { ["Data.Name"] = "Wohnung 1" });
        var unit = await admin.GetAsync(property.Document.QuerySelectorAll("a").Single(a => a.TextContent == "Wohnung 1").GetAttribute("href")!);

        unit = await admin.SubmitAsync(unit, "area", new Dictionary<string, string> { ["Input.From"] = "2025-01-01", ["Input.Area"] = "75.5" });
        unit = await admin.SubmitAsync(unit, "area", new Dictionary<string, string> { ["Input.From"] = "2026-02-01", ["Input.Area"] = "82.4", ["Input.SecondaryArea"] = "80" });

        Assert.Contains("Verlauf", unit.Text, StringComparison.Ordinal);
        Assert.Contains("01.01.2025 – 31.01.2026", unit.Text, StringComparison.Ordinal);
        Assert.Contains("seit 01.02.2026", unit.Text, StringComparison.Ordinal);

        var retroactive = await admin.SubmitAsync(unit, "area", new Dictionary<string, string> { ["Input.From"] = "2025-06-01", ["Input.Area"] = "70" });
        Assert.Contains("Der Stichtag muss am oder nach dem 01.02.2026 liegen", retroactive.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_inputs_are_reported()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        var errors = await admin.SubmitAsync(await admin.GetAsync("/verwaltung/objekte/neu"), "property",
            new Dictionary<string, string>(House) { ["Data.PostalCode"] = "123", ["Data.YearBuilt"] = "1200" });
        Assert.Contains("Die Postleitzahl hat 5 Ziffern.", errors.Text, StringComparison.Ordinal);
        Assert.Contains("Baujahr zwischen 1500 und 2100", errors.Text, StringComparison.Ordinal);

        var property = await admin.SubmitAsync(await admin.GetAsync("/verwaltung/objekte/neu"), "property", House);
        // type="number" always submits with a dot; the domain rejects implausible values.
        var area = await admin.SubmitAsync(property, "area", new Dictionary<string, string> { ["Input.From"] = "2026-01-01", ["Input.Area"] = "-5" });
        Assert.Contains("Die Gesamtfläche muss größer als 0 sein.", area.Text, StringComparison.Ordinal);
        var withoutValue = await admin.SubmitAsync(property, "area", new Dictionary<string, string> { ["Input.From"] = "2026-01-01", ["Input.Area"] = "" });
        Assert.Contains("Bitte die Fläche angeben.", withoutValue.Text, StringComparison.Ordinal);

        var duplicate = await admin.SubmitAsync(property, "unit", new Dictionary<string, string> { ["Data.Name"] = "W1" });
        duplicate = await admin.SubmitAsync(duplicate, "unit", new Dictionary<string, string> { ["Data.Name"] = "w1" });
        Assert.Contains("Im Objekt gibt es bereits eine Einheit „w1“.", duplicate.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Upload_and_display_photo()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var property = await admin.SubmitAsync(await admin.GetAsync("/verwaltung/objekte/neu"), "property", House);
        var edit = await admin.GetAsync(property.Path + "/bearbeiten");

        var afterUpload = await admin.SubmitWithFileAsync(edit, "image", "Image", Jpeg(), "house.jpg");

        Assert.Contains("Das Foto wurde gespeichert.", afterUpload.Text, StringComparison.Ordinal);
        var image = afterUpload.Document.QuerySelector("img.property-image")!.GetAttribute("src")!;
        using var response = await admin.Http.GetAsync(new Uri(image, UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType!.MediaType);

        var noImage = await admin.SubmitWithFileAsync(edit, "image", "Image", "%PDF-1.4"u8.ToArray(), "plan.pdf", "application/pdf");
        Assert.Contains("Das Foto muss ein Bild sein.", noImage.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_master_data_permission_no_access()
    {
        var (manager, _) = await SignedInAsAsync(Roles.Manager, [AccessRight.Finance]);
        using var _ = manager;

        var response = await manager.GetAsync("/verwaltung/objekte", followRedirects: false);

        Assert.Equal("/konto/zugriff-verweigert", response.RedirectLocation!.AbsolutePath);
        Assert.DoesNotContain("/verwaltung/objekte", (await manager.GetAsync("/verwaltung")).Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Caretaker_sees_only_assigned_properties()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var houseA = await admin.SubmitAsync(await admin.GetAsync("/verwaltung/objekte/neu"), "property", House);
        houseA = await admin.SubmitAsync(houseA, "unit", new Dictionary<string, string> { ["Data.Name"] = "Wohnung 1", ["Data.Location"] = "EG links" });
        var houseB = await admin.SubmitAsync(await admin.GetAsync("/verwaltung/objekte/neu"), "property",
            new Dictionary<string, string>(House) { ["Data.Name"] = "Birkenallee 9", ["Data.Street"] = "Birkenallee 9" });
        var idA = Guid.Parse(houseA.Path.Split('/')[^1]);
        var idB = Guid.Parse(houseB.Path.Split('/')[^1]);

        var (caretaker, caretakerId) = await SignedInAsAsync(Roles.Caretaker);
        using var __ = caretaker;

        // Assignment via user management
        var account = await admin.GetAsync($"/verwaltung/benutzer/{caretakerId}");
        Assert.Contains("Betreute Objekte", account.Text, StringComparison.Ordinal);
        account = await admin.SubmitAsync(account, "properties", new Dictionary<string, string> { ["PropertiesInput.Options"] = idA.ToString() });
        Assert.Contains("Die betreuten Objekte wurden gespeichert.", account.Text, StringComparison.Ordinal);
        Assert.True(account.Document.QuerySelector($"#property-{idA}")!.HasAttribute("checked"));
        Assert.False(account.Document.QuerySelector($"#property-{idB}")!.HasAttribute("checked"));

        var list = await caretaker.GetAsync("/service/objekte");
        Assert.Contains("Lindenweg 3", list.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Birkenallee", list.Text, StringComparison.Ordinal);

        var allowed = await caretaker.GetAsync($"/service/objekte/{idA}");
        Assert.Contains("EG links", allowed.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Leerstand", allowed.Text, StringComparison.Ordinal); // no occupancy/tenant data

        var forbidden = await caretaker.GetAsync($"/service/objekte/{idB}");
        Assert.Contains("Objekt nicht gefunden", forbidden.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Birkenallee", forbidden.Text, StringComparison.Ordinal);

        Assert.Equal("/konto/zugriff-verweigert", (await caretaker.GetAsync($"/verwaltung/objekte/{idA}", followRedirects: false)).RedirectLocation!.AbsolutePath);

        // Tenants have no property assignment
        var (_, tenantId) = await SignedInAsAsync(Roles.Tenant);
        var result = await App.WithScopeAsync(sp => sp.GetRequiredService<IPropertyAssignments>().SetAsync(tenantId, [idA], TestContext.Current.CancellationToken));
        Assert.False(result.Succeeded);
    }

    private static byte[] Jpeg()
    {
        using var bitmap = new SKBitmap(40, 30);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Jpeg, 80).ToArray();
    }
}
