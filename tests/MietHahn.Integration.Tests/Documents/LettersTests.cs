using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Common;
using MietHahn.Application.Documents;
using MietHahn.Application.Files;
using MietHahn.Application.Leases;
using MietHahn.Application.People;
using MietHahn.Application.Properties;
using MietHahn.Domain.Admin;
using MietHahn.Domain.Communication;
using MietHahn.Domain.Documents;
using MietHahn.Infrastructure.Persistence;
using MietHahn.Integration.Tests.Infrastructure;

namespace MietHahn.Integration.Tests.Documents;

/// <summary>Mass letter from a template with a real PDF (Gotenberg): one document per tenant, template version, mail.</summary>
public class LettersTests(PostgresFixture postgres, GotenbergFixture gotenberg) : IClassFixture<GotenbergFixture>, IAsyncLifetime
{
    private InfrastructureEnvironment environment = null!;
    private Guid house;
    private Guid templateId;
    private readonly List<Guid> leases = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres, new Dictionary<string, string?> { ["Pdf:GotenbergUrl"] = gotenberg.Url });
        await using var scope = environment.Services.CreateAsyncScope();
        var properties = scope.ServiceProvider.GetRequiredService<IPropertyService>();
        house = Validate(await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct));
        var people = scope.ServiceProvider.GetRequiredService<IPersonService>();
        var leaseService = scope.ServiceProvider.GetRequiredService<ILeaseService>();
        for (var n = 1; n <= 5; n++)
        {
            var unit = Validate(await properties.CreateUnitAsync(house, new UnitInput { Name = $"Wohnung {n}" }, Ct));
            var person = Validate(await people.CreateAsync(new PersonInput
            {
                FirstName = $"Vorname{n}",
                LastName = $"Muster{n}",
                Salutation = MietHahn.Domain.People.Salutation.Mrs,
                Email = n == 5 ? null : $"mieter{n}@example.de",
            }, null, Ct));
            leases.Add(Validate(await leaseService.CreateAsync(new LeaseCreation { RentalUnitId = unit, Start = new DateOnly(2025, 1, 1), TenantIds = [person], BaseRent = 500m, People = 1 }, Ct)));
        }

        var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
        db.Add(new LandlordProfile("Andy Hahn", "Musterweg 1", "12345", "Musterstadt") { Iban = "DE02 1203 0000 0000 2020 51", Bank = "Musterbank" });
        await db.SaveChangesAsync(Ct);

        var templates = scope.ServiceProvider.GetRequiredService<IDocumentTemplates>();
        await templates.InstallDefaultsAsync(Ct);
        templateId = (await templates.ListAsync(Ct)).Single(t => t.Name == "Ankündigung von Arbeiten").Id;
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Mass_letter_to_five_tenants_creates_five_documents_that_a_later_template_change_does_not_touch()
    {
        var run = await WithAsync<ILetters, LetterRunResult>(l => l.CreateAsync(new LetterRequest { TemplateId = templateId, PropertyId = house, LeaseIds = leases, ShareWithTenants = true }, Ct));

        Assert.Equal(5, run.Created);
        Assert.All(run.Outcomes, o => Assert.True(o.Succeeded, o.Message));

        // Change the template afterwards: new revision, the generated documents keep pointing to the first one.
        var edit = (await WithAsync<IDocumentTemplates, TemplateInput?>(t => t.GetAsync(templateId, Ct)))!;
        edit.Body += "<p>Nachtrag</p>";
        Validate(await WithAsync<IDocumentTemplates, Result>(t => t.SaveAsync(templateId, edit, Ct)));

        await using var scope = environment.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
        var documents = await db.Set<Document>().ToListAsync(Ct);
        var first = await db.Set<DocumentTemplateRevision>().SingleAsync(r => r.Number == 1 && r.Body.Contains("Art der Arbeiten"), Ct);
        Assert.Equal(5, documents.Count);
        Assert.All(documents, d => Assert.Equal((first.Id, DocumentCategory.Correspondence, true), (d.TemplateRevisionId!.Value, d.Category, d.SharedWithTenants)));
        Assert.Equal(leases.Order(), documents.Select(d => d.LeaseId!.Value).Order());

        var text = await PdfTextAsync(documents.Single(d => d.Title.Contains("Muster3", StringComparison.Ordinal)).FileId);
        Assert.Contains("Ankündigung von Arbeiten am Haus Lindenweg 3", text, StringComparison.Ordinal);
        Assert.Contains("Sehr geehrte Frau Muster3,", text, StringComparison.Ordinal);
        Assert.Contains("Wohnung 3, Lindenweg 3", text, StringComparison.Ordinal);
        Assert.Contains("Mit freundlichen Grüßen", text, StringComparison.Ordinal); // from the included snippet
        Assert.DoesNotContain("Nachtrag", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Residence_confirmation_names_landlord_tenant_address_and_move_in_date()
    {
        var confirmation = (await WithAsync<IDocumentTemplates, IReadOnlyList<TemplateRow>>(t => t.ListAsync(Ct))).Single(t => t.Name == "Wohnungsgeberbestätigung Einzug (§ 19 BMG)").Id;

        var run = await WithAsync<ILetters, LetterRunResult>(l => l.CreateAsync(new LetterRequest { TemplateId = confirmation, PropertyId = house, LeaseIds = [leases[1]] }, Ct));

        Assert.Equal(1, run.Created);
        await using var scope = environment.Services.CreateAsyncScope();
        var document = await scope.ServiceProvider.GetRequiredService<MietHahnDbContext>().Set<Document>().SingleAsync(Ct);
        var text = await PdfTextAsync(document.FileId);
        Assert.Contains("Wohnungsgeberbestätigung nach § 19 Bundesmeldegesetz (Einzug)", text, StringComparison.Ordinal);
        Assert.Contains("Andy Hahn", text, StringComparison.Ordinal);
        Assert.Contains("Vorname2 Muster2", text, StringComparison.Ordinal);
        Assert.Contains("Lindenweg 3, 50667 Köln", text, StringComparison.Ordinal);
        Assert.Contains("01.01.2025", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rent_certificate_and_deposit_statement_use_the_current_amounts_of_the_lease()
    {
        var names = new[] { "Mietbescheinigung", "Kautionsabrechnung und -rückzahlung" };
        var rows = await WithAsync<IDocumentTemplates, IReadOnlyList<TemplateRow>>(t => t.ListAsync(Ct));
        foreach (var name in names)
        {
            var run = await WithAsync<ILetters, LetterRunResult>(l => l.CreateAsync(new LetterRequest { TemplateId = rows.Single(t => t.Name == name).Id, PropertyId = house, LeaseIds = [leases[0]] }, Ct));
            Assert.Equal(1, run.Created);
        }

        await using var scope = environment.Services.CreateAsyncScope();
        var documents = await scope.ServiceProvider.GetRequiredService<MietHahnDbContext>().Set<Document>().ToListAsync(Ct);
        var certificate = await PdfTextAsync(documents.Single(d => d.Title.StartsWith("Mietbescheinigung", StringComparison.Ordinal)).FileId);
        Assert.True(certificate.Contains("500,00", StringComparison.Ordinal), certificate);
        Assert.Contains("Gesamtmiete monatlich", certificate, StringComparison.Ordinal);
        var statement = await PdfTextAsync(documents.Single(d => d.Title.StartsWith("Abrechnung Ihrer Mietkaution", StringComparison.Ordinal)).FileId);
        Assert.Contains("Zu erstattender Betrag", statement, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mail_goes_only_to_tenants_with_an_address_and_the_rest_is_reported()
    {
        var run = await WithAsync<ILetters, LetterRunResult>(l => l.CreateAsync(new LetterRequest { TemplateId = templateId, PropertyId = house, LeaseIds = leases, SendByMail = true }, Ct));

        Assert.Equal((5, 4), (run.Created, run.Mailed));
        Assert.Contains("Keine E-Mail-Adresse", run.Outcomes.Single(o => !o.Mailed).Message, StringComparison.Ordinal);
        await using var scope = environment.Services.CreateAsyncScope();
        var mails = await scope.ServiceProvider.GetRequiredService<MietHahnDbContext>().Set<OutgoingMail>().ToListAsync(Ct);
        Assert.Equal(4, mails.Count);
        Assert.All(mails, m => Assert.Single(m.AttachmentFileIds));
        Assert.Contains("Ankündigung von Arbeiten am Haus Lindenweg 3", mails[0].Subject, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Recipients_are_the_current_leases_of_the_property_and_foreign_leases_are_refused()
    {
        var recipients = await WithAsync<ILetters, IReadOnlyList<LetterRecipient>>(l => l.RecipientsAsync(house, Ct));
        var run = await WithAsync<ILetters, LetterRunResult>(l => l.CreateAsync(new LetterRequest { TemplateId = templateId, PropertyId = house, LeaseIds = [Guid.NewGuid()] }, Ct));

        Assert.Equal(["Wohnung 1", "Wohnung 2", "Wohnung 3", "Wohnung 4", "Wohnung 5"], recipients.Select(r => r.RentalUnit));
        Assert.Equal([true, true, true, true, false], recipients.Select(r => r.HasEmail));
        Assert.Equal(0, run.Created);
        Assert.False(run.Outcomes.Single().Succeeded);
    }

    [Fact]
    public async Task Preview_uses_the_real_data_of_the_lease_and_a_snippet_cannot_be_sent()
    {
        var preview = await WithAsync<ILetters, TemplatePreview>(l => l.PreviewAsync(templateId, leases[1], Ct));
        var snippet = (await WithAsync<IDocumentTemplates, IReadOnlyList<TemplateRow>>(t => t.ListAsync(Ct))).First(t => t.Kind == TemplateKind.Snippet).Id;
        var refused = await WithAsync<ILetters, LetterRunResult>(l => l.CreateAsync(new LetterRequest { TemplateId = snippet, PropertyId = house, LeaseIds = leases }, Ct));

        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(preview.Pdf!, 0, 4), StringComparison.Ordinal);
        Assert.Contains("Baustein", refused.Errors.Single(), StringComparison.Ordinal);
    }

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }

    private async Task<T> WithAsync<TService, T>(Func<TService, Task<T>> action)
        where TService : notnull
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private async Task<string> PdfTextAsync(Guid fileId)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        await using var opened = (await scope.ServiceProvider.GetRequiredService<IFileStore>().OpenAsync(fileId, Ct))!;
        using var buffer = new MemoryStream();
        await opened.Content.CopyToAsync(buffer, Ct);
        using var document = UglyToad.PdfPig.PdfDocument.Open(buffer.ToArray());
        return string.Join(' ', document.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text)).Replace("ﬁ", "fi", StringComparison.Ordinal).Replace("ﬂ", "fl", StringComparison.Ordinal);
    }
}
