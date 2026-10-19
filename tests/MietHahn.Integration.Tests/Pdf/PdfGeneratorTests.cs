using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Documents;
using MietHahn.Domain.Admin;
using MietHahn.Infrastructure.Pdf;
using MietHahn.Infrastructure.Persistence;
using MietHahn.Integration.Tests.Infrastructure;
using UglyToad.PdfPig;

namespace MietHahn.Integration.Tests.Pdf;

public class PdfGeneratorTests(PostgresFixture postgres, GotenbergFixture gotenberg) : IClassFixture<GotenbergFixture>, IAsyncLifetime
{
    private const double PointsPerMm = 72 / 25.4;
    private const double A4HeightMm = 297;

    private static readonly PostalAddress Erika = new(["Erika Mustermann", "Hauptstraße 5, EG links", "12345 Musterstadt"], "Persönlich");

    private InfrastructureEnvironment environment = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres, new Dictionary<string, string?> { ["Pdf:GotenbergUrl"] = gotenberg.Url });
        await using var scope = environment.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
        db.Add(new LandlordProfile("Andy Hahn", "Musterweg 1", "12345", "Musterstadt")
        {
            Email = "vermieter@example.de",
            Iban = "DE02 1203 0000 0000 2020 51",
            Bank = "Musterbank",
        });
        await db.SaveChangesAsync(Ct);
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Letter_HTML_contains_letterhead_address_field_and_footer()
    {
        var (html, footer) = await HtmlAsync(new PdfRequest("test", "Testbrief <1>", TestModel(), Erika, Date: new DateOnly(2026, 10, 18)));

        Assert.Contains("<title>Testbrief &lt;1&gt;</title>", html, StringComparison.Ordinal);
        Assert.Contains("class=\"anschrift\"", html, StringComparison.Ordinal);
        Assert.Contains("Andy Hahn · Musterweg 1 · 12345 Musterstadt", html, StringComparison.Ordinal);
        Assert.Contains("<div>Hauptstraße 5, EG links</div>", html, StringComparison.Ordinal);
        Assert.Contains("Persönlich", html, StringComparison.Ordinal);
        Assert.Contains("Datum: 18.10.2026", html, StringComparison.Ordinal);
        Assert.Contains("1.234,56 €", html, StringComparison.Ordinal);
        Assert.Contains("IBAN DE02 1203 0000 0000 2020 51", footer, StringComparison.Ordinal);
        Assert.Contains("<span class=\"pageNumber\"></span>", footer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Document_without_recipient_has_no_address_field()
    {
        var (html, _) = await HtmlAsync(new PdfRequest("test", "Anlage", TestModel()));

        Assert.DoesNotContain("class=\"anschrift\"", html, StringComparison.Ordinal);
        Assert.Contains("<h1>Anlage</h1>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Template_text_can_be_rendered_directly()
    {
        var (html, _) = await HtmlAsync(new PdfRequest(null, "Freitext", new { Name = "<Mieter>" }, TemplateText: "<p>Hallo {{ name }}</p>"));

        Assert.Contains("<p>Hallo &lt;Mieter&gt;</p>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_landlord_profile_there_is_a_clear_message()
    {
        await using var withoutProfile = await InfrastructureEnvironment.CreateAsync(postgres);
        await using var scope = withoutProfile.Services.CreateAsyncScope();

        var errors = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<IPdfGenerator>().GenerateAsync(new PdfRequest("test", "x", TestModel()), Ct));
        Assert.Contains("Vermieterprofil", errors.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Too_many_address_lines_are_rejected()
    {
        var address = new PostalAddress(["1", "2", "3", "4", "5", "6", "7"]);

        await Assert.ThrowsAsync<ArgumentException>(() => HtmlAsync(new PdfRequest("test", "x", TestModel(), address)));
    }

    [Fact]
    public async Task Letter_is_generated_as_PDF_A_with_address_in_window_area()
    {
        var pdf = await PdfAsync(new PdfRequest("test", "Testbrief", TestModel(), Erika));

        Assert.True(pdf.AsSpan().StartsWith("%PDF-"u8));
        Assert.Contains("pdfaid:part>2<", System.Text.Encoding.ASCII.GetString(pdf), StringComparison.Ordinal);

        using var document = PdfDocument.Open(pdf);
        Assert.Equal("Testbrief", document.Information.Title);
        Assert.Equal(1, document.NumberOfPages);
        var page = document.GetPage(1);
        Assert.Contains("Testdokument", page.Text, StringComparison.Ordinal);
        Assert.Contains("Seite 1 von 1", page.Text, StringComparison.Ordinal);

        // DIN 5008 Form B: Empfänger im Bereich 20–105 mm von links, 62,7–90 mm von oben.
        var erika = page.GetWords().First(w => w.Text == "Erika");
        var links = erika.BoundingBox.Left / PointsPerMm;
        var fromTop = A4HeightMm - (erika.BoundingBox.Top / PointsPerMm);
        Assert.InRange(links, 20, 25);
        Assert.InRange(fromTop, 62.7, 90);

        // Rücksendezeile oberhalb des Empfängers (45–50 mm von oben).
        var returnAddress = page.GetWords().Where(w => w.Text == "Musterweg").MinBy(w => w.BoundingBox.Height)!;
        Assert.InRange(A4HeightMm - (returnAddress.BoundingBox.Top / PointsPerMm), 45, 50);
    }

    [Fact]
    public async Task Long_documents_wrap_with_page_numbers()
    {
        var positions = Enumerable.Range(1, 120).Select(i => new { Name = $"Position {i}", Amount = i * 1.5m }).ToList();

        var pdf = await PdfAsync(new PdfRequest("test", "Lang", new { Items = positions, Total = positions.Sum(p => p.Amount) }, Erika));

        using var document = PdfDocument.Open(pdf);
        Assert.True(document.NumberOfPages >= 3, $"Erwartet mindestens 3 Seiten, erhalten {document.NumberOfPages}.");
        Assert.Contains($"Seite 2 von {document.NumberOfPages}", document.GetPage(2).Text, StringComparison.Ordinal);
        Assert.Contains("Position", document.GetPage(2).Text, StringComparison.Ordinal); // Tabellenkopf wiederholt sich
        Assert.Contains("Position 120", document.GetPage(document.NumberOfPages).Text, StringComparison.Ordinal);

        // Die Summenzeile steht nur auf der letzten Seite.
        var pagesWithTotal = Enumerable.Range(1, document.NumberOfPages).Where(i => document.GetPage(i).GetWords().Any(w => w.Text == "Summe")).ToList();
        Assert.Equal([document.NumberOfPages], pagesWithTotal);
    }

    [Fact]
    public async Task PDF_is_stored_in_the_file_store()
    {
        await using var scope = environment.Services.CreateAsyncScope();

        var file = await scope.ServiceProvider.GetRequiredService<IPdfGenerator>()
            .GenerateAndStoreAsync(new PdfRequest("test", "Ablage", TestModel(), Erika), "Testbrief.pdf", Ct);

        Assert.Equal("application/pdf", file.MimeType);
        Assert.Equal("Testbrief.pdf", file.Name);
        Assert.True(file.Size > 1000);
    }

    private static object TestModel() => new
    {
        Items = new[] { new { Name = "Grundsteuer", Amount = 1234.56m } },
        Total = 1234.56m,
    };

    private async Task<(string Html, string Footer)> HtmlAsync(PdfRequest job)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var generation = (PdfGenerator)scope.ServiceProvider.GetRequiredService<IPdfGenerator>();
        var (html, footer, _) = await generation.GenerateHtmlAsync(job, Ct);
        return (html, footer);
    }

    private async Task<byte[]> PdfAsync(PdfRequest job)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPdfGenerator>().GenerateAsync(job, Ct);
    }
}
