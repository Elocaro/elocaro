using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.Properties;
using MietHahn.Web.Tests.Account;
using SkiaSharp;

namespace MietHahn.Web.Tests.Documents;

public class DocumentsTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private static readonly byte[] Pdf = "%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF\n"u8.ToArray();

    [Fact]
    public async Task Upload_search_edit_and_delete_property_document()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var propertyId = await PropertyAsync(admin);

        var property = await admin.GetAsync($"/verwaltung/objekte/{propertyId}");
        Assert.Contains("Noch keine Dokumente.", property.Text, StringComparison.Ordinal);
        var newItem = await admin.GetAsync(property.Document.QuerySelectorAll("a").Single(a => a.TextContent == "Dokument hochladen").GetAttribute("href")!);
        Assert.True(newItem.Document.QuerySelector($"#dokument-objekt option[value='{propertyId}']")!.HasAttribute("selected"));

        // Without a title the file name is used.
        var details = await admin.SubmitWithFileAsync(newItem, "dokument", "Upload", Pdf, "Energieausweis_2024.pdf", "application/pdf", new Dictionary<string, string>
        {
            ["Data.Category"] = "EnergyCertificate",
            ["Data.ExpiresOn"] = "2034-05-31",
            ["Data.Tags"] = "Energie, Pflicht",
        });
        Assert.Contains("Das Dokument wurde abgelegt.", details.Text, StringComparison.Ordinal);
        Assert.Equal("Energieausweis_2024", details.Document.QuerySelector("h1")!.TextContent.Trim());
        Assert.NotNull(details.Document.QuerySelector("iframe.dokument-vorschau"));
        var documentPath = details.Path;

        var list = await admin.GetAsync("/verwaltung/dokumente?q=pflicht");
        Assert.Contains("Energieausweis_2024", list.Text, StringComparison.Ordinal);
        Assert.Contains("gültig bis 31.05.2034", list.Text, StringComparison.Ordinal);
        Assert.Contains("Keine passenden Dokumente.", (await admin.GetAsync("/verwaltung/dokumente?kategorie=Invoice")).Text, StringComparison.Ordinal);
        Assert.Contains("Energieausweis_2024", (await admin.GetAsync($"/verwaltung/objekte/{propertyId}")).Text, StringComparison.Ordinal);

        details = await admin.SubmitAsync(details, "dokument", new Dictionary<string, string> { ["Data.Title"] = "Energieausweis (Bedarf)", ["Data.SharedWithCaretakers"] = "true" });
        Assert.Contains("Die Angaben wurden gespeichert.", details.Text, StringComparison.Ordinal);
        Assert.Contains("Hausmeister", details.Document.QuerySelector("dl.angaben")!.TextContent, StringComparison.Ordinal);

        var deleted = await admin.SubmitAsync(await admin.GetAsync(documentPath), "loeschen");
        Assert.Contains("Das Dokument wurde gelöscht.", deleted.Text, StringComparison.Ordinal);
        Assert.Contains("Noch keine Dokumente.", deleted.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_reference_or_file_nothing_is_stored()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        await PropertyAsync(admin);

        var withoutReference = await admin.SubmitWithFileAsync(await admin.GetAsync("/verwaltung/dokumente/neu"), "dokument", "Upload", Pdf, "lose.pdf", "application/pdf");
        Assert.Contains("mindestens einen Bezug", withoutReference.Text, StringComparison.Ordinal);

        var executable = await admin.SubmitWithFileAsync(await admin.GetAsync("/verwaltung/dokumente/neu"), "dokument", "Upload", "MZ"u8.ToArray(), "tool.exe", "application/octet-stream");
        Assert.NotNull(executable.Document.QuerySelector(".meldung-fehler"));
        Assert.Contains("Noch keine Dokumente.", (await admin.GetAsync("/verwaltung/dokumente")).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Caretaker_sees_shared_documents_in_the_service_area()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var propertyId = await PropertyAsync(admin);
        var newItem = await admin.GetAsync($"/verwaltung/dokumente/neu?objekt={propertyId}");
        var shared = await admin.SubmitWithFileAsync(newItem, "dokument", "Upload", Pdf, "maintenance.pdf", "application/pdf",
            new Dictionary<string, string> { ["Data.Title"] = "Wartungsplan Heizung", ["Data.SharedWithCaretakers"] = "true" });
        await admin.SubmitWithFileAsync(newItem, "dokument", "Upload", Pdf, "kalkulation.pdf", "application/pdf",
            new Dictionary<string, string> { ["Data.Title"] = "Interne Kalkulation" });
        var fileLink = shared.Document.QuerySelectorAll("a").Single(a => a.TextContent == "Herunterladen").GetAttribute("href")!;
        var internalFile = (await admin.GetAsync("/verwaltung/dokumente?q=Kalkulation")).Document.QuerySelectorAll("a").Single(a => a.TextContent == "Interne Kalkulation").GetAttribute("href")!;
        internalFile = (await admin.GetAsync(internalFile)).Document.QuerySelectorAll("a").Single(a => a.TextContent == "Herunterladen").GetAttribute("href")!;

        var (caretaker, caretakerId) = await SignedInAsAsync(Roles.Caretaker);
        using var __ = caretaker;
        Assert.True((await App.WithScopeAsync(sp => sp.GetRequiredService<IPropertyAssignments>().SetAsync(caretakerId, [propertyId], TestContext.Current.CancellationToken))).Succeeded);

        var service = await caretaker.GetAsync($"/service/objekte/{propertyId}");
        Assert.Contains("Wartungsplan Heizung", service.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Interne Kalkulation", service.Text, StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.OK, (await caretaker.GetAsync(fileLink)).Status);
        Assert.Equal("/konto/zugriff-verweigert", (await caretaker.GetAsync(internalFile, followRedirects: false)).RedirectLocation!.AbsolutePath);
    }

    [Fact]
    public async Task PDF_preview_may_be_embedded_but_not_by_third_parties()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var propertyId = await PropertyAsync(admin);
        var details = await admin.SubmitWithFileAsync(await admin.GetAsync($"/verwaltung/dokumente/neu?objekt={propertyId}"), "dokument", "Upload", Pdf, "plan.pdf", "application/pdf");
        var filePath = details.Document.QuerySelector("iframe.dokument-vorschau")!.GetAttribute("src")!;

        using var response = await admin.Http.GetAsync(new Uri(filePath, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal("SAMEORIGIN", response.Headers.GetValues("X-Frame-Options").Single());
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("frame-ancestors 'self'", csp, StringComparison.Ordinal);
        Assert.Contains("default-src 'none'", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("sandbox", csp, StringComparison.Ordinal);

        // Images (and everything else) stay in the sandbox.
        var photo = await admin.SubmitWithFileAsync(await admin.GetAsync($"/verwaltung/dokumente/neu?objekt={propertyId}"), "dokument", "Upload", Jpeg(), "keller.jpg");
        var imagePath = photo.Document.QuerySelector("img.dokument-vorschau")!.GetAttribute("src")!;
        using var image = await admin.Http.GetAsync(new Uri(imagePath, UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.EndsWith("; sandbox", image.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
    }

    private static byte[] Jpeg()
    {
        using var bitmap = new SKBitmap(40, 30);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Jpeg, 80).ToArray();
    }

    [Fact]
    public async Task Without_master_data_permission_no_document_library()
    {
        var (manager, _) = await SignedInAsAsync(Roles.Manager, [AccessRight.Finance]);
        using var _ = manager;

        Assert.Equal("/konto/zugriff-verweigert", (await manager.GetAsync("/verwaltung/dokumente", followRedirects: false)).RedirectLocation!.AbsolutePath);
        Assert.Equal("/konto/zugriff-verweigert", (await manager.GetAsync("/verwaltung/dokumente/neu", followRedirects: false)).RedirectLocation!.AbsolutePath);
    }

    private static async Task<Guid> PropertyAsync(TestBrowser admin)
    {
        var property = await admin.SubmitAsync(await admin.GetAsync("/verwaltung/objekte/neu"), "objekt", new Dictionary<string, string>
        {
            ["Data.Name"] = "Lindenweg 3",
            ["Data.Street"] = "Lindenweg 3",
            ["Data.PostalCode"] = "50667",
            ["Data.City"] = "Köln",
        });
        return Guid.Parse(property.Path.Split('/')[^1]);
    }
}
