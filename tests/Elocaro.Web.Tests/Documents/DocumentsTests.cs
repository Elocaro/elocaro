using System.Net;
using Elocaro.Application.Accounts;
using Elocaro.Application.Documents;
using Elocaro.Application.Files;
using Elocaro.Application.Properties;
using Elocaro.Domain.Documents;
using Elocaro.Web.Tests.Account;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace Elocaro.Web.Tests.Documents;

public class DocumentsTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private static readonly byte[] Pdf = "%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF\n"u8.ToArray();

    // Documents are stored through the services; uploading, validation and deleting in the UI are covered by DocumentsBrowserTests.
    private async Task<Guid> StoreAsync(Guid propertyId, string fileName, byte[] content, Action<DocumentInput>? more = null) =>
        await App.WithScopeAsync(async sp =>
        {
            var ct = TestContext.Current.CancellationToken;
            await using var stream = new MemoryStream(content);
            var file = await sp.GetRequiredService<IFileStore>().SaveAsync(stream, fileName, ct);
            var data = new DocumentInput { Title = Path.GetFileNameWithoutExtension(fileName), PropertyId = propertyId };
            more?.Invoke(data);
            var result = await sp.GetRequiredService<IDocumentLibrary>().CreateAsync(file.Id, data, ct);
            Assert.True(result.Succeeded, string.Join(" ", result.Errors));
            return result.Id!.Value;
        });

    [Fact]
    public async Task Stored_document_is_listed_searchable_and_shown_with_preview()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var propertyId = await PropertyAsync(admin);

        var property = await admin.GetAsync($"/verwaltung/objekte/{propertyId}");
        Assert.Contains("Noch keine Dokumente.", property.Text, StringComparison.Ordinal);
        var upload = property.Document.QuerySelectorAll("a").Single(a => a.TextContent == "Dokument hochladen").GetAttribute("href")!;
        Assert.Contains($"objekt={propertyId}", upload, StringComparison.Ordinal);

        var id = await StoreAsync(propertyId, "Energieausweis_2024.pdf", Pdf, d =>
        {
            d.Category = DocumentCategory.EnergyCertificate;
            d.ExpiresOn = new DateOnly(2034, 5, 31);
            d.Tags = "Energie, Pflicht";
        });

        var details = await admin.GetAsync($"/verwaltung/dokumente/{id}");
        Assert.Equal("Energieausweis_2024", details.Document.QuerySelector("h1")!.TextContent.Trim());
        Assert.NotNull(details.Document.QuerySelector("iframe.document-preview"));

        var list = await admin.GetAsync("/verwaltung/dokumente?q=pflicht");
        Assert.Contains("Energieausweis_2024", list.Text, StringComparison.Ordinal);
        Assert.Contains("gültig bis 31.05.2034", list.Text, StringComparison.Ordinal);
        Assert.Contains("Keine passenden Dokumente.", (await admin.GetAsync("/verwaltung/dokumente?kategorie=Invoice")).Text, StringComparison.Ordinal);
        Assert.Contains("Energieausweis_2024", (await admin.GetAsync($"/verwaltung/objekte/{propertyId}")).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Caretaker_sees_shared_documents_in_the_service_area()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var propertyId = await PropertyAsync(admin);
        var sharedId = await StoreAsync(propertyId, "maintenance.pdf", Pdf, d =>
        {
            d.Title = "Wartungsplan Heizung";
            d.SharedWithCaretakers = true;
        });
        var internalId = await StoreAsync(propertyId, "kalkulation.pdf", Pdf, d => d.Title = "Interne Kalkulation");
        static string FileLink(Page page) => page.Document.QuerySelectorAll("a").Single(a => a.TextContent == "Herunterladen").GetAttribute("href")!;
        var fileLink = FileLink(await admin.GetAsync($"/verwaltung/dokumente/{sharedId}"));
        var internalFile = FileLink(await admin.GetAsync($"/verwaltung/dokumente/{internalId}"));

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
        var details = await admin.GetAsync($"/verwaltung/dokumente/{await StoreAsync(propertyId, "plan.pdf", Pdf)}");
        var filePath = details.Document.QuerySelector("iframe.document-preview")!.GetAttribute("src")!;

        using var response = await admin.Http.GetAsync(new Uri(filePath, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal("SAMEORIGIN", response.Headers.GetValues("X-Frame-Options").Single());
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("frame-ancestors 'self'", csp, StringComparison.Ordinal);
        Assert.Contains("default-src 'none'", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("sandbox", csp, StringComparison.Ordinal);

        // Images (and everything else) stay in the sandbox.
        var photo = await admin.GetAsync($"/verwaltung/dokumente/{await StoreAsync(propertyId, "keller.jpg", Jpeg())}");
        var imagePath = photo.Document.QuerySelector("img.document-preview")!.GetAttribute("src")!;
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

    private async Task<Guid> PropertyAsync(TestBrowser admin)
    {
        _ = admin;
        return await App.WithScopeAsync(async sp => (await sp.GetRequiredService<Elocaro.Application.Properties.IPropertyService>().CreateAsync(
            new Elocaro.Application.Properties.PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" },
            TestContext.Current.CancellationToken)).Id!.Value);
    }
}
