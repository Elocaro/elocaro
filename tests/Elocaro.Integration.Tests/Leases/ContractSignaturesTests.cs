using Elocaro.Application.Common;
using Elocaro.Application.Documents;
using Elocaro.Application.Files;
using Elocaro.Application.Leases;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Domain.Admin;
using Elocaro.Domain.Communication;
using Elocaro.Domain.Documents;
using Elocaro.Domain.Leases;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Leases;

/// <summary>Digital signature of the contract draft with real PDFs (Gotenberg).</summary>
public class ContractSignaturesTests(PostgresFixture postgres, GotenbergFixture gotenberg) : IClassFixture<GotenbergFixture>, IAsyncLifetime
{
    private static readonly byte[] Png =
        Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private InfrastructureEnvironment environment = null!;
    private Guid lease;
    private Guid contract;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres, new Dictionary<string, string?> { ["Pdf:GotenbergUrl"] = gotenberg.Url });
        environment.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero));
        await using var scope = environment.Services.CreateAsyncScope();
        var properties = scope.ServiceProvider.GetRequiredService<IPropertyService>();
        var people = scope.ServiceProvider.GetRequiredService<IPersonService>();
        var house = Validate(await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct));
        var apartment = Validate(await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 1" }, Ct));
        Validate(await properties.SetUnitAreaAsync(apartment, new AreaInput { From = new DateOnly(2020, 1, 1), Area = 60m }, Ct));
        var anna = Validate(await people.CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt", Salutation = Elocaro.Domain.People.Salutation.Mrs, Email = "anna@example.de" }, null, Ct));
        var ben = Validate(await people.CreateAsync(new PersonInput { FirstName = "Ben", LastName = "Weber" }, null, Ct));
        lease = Validate(await scope.ServiceProvider.GetRequiredService<ILeaseService>().CreateAsync(new LeaseCreation
        {
            RentalUnitId = apartment,
            Start = new DateOnly(2026, 11, 1),
            TenantIds = [anna, ben],
            BaseRent = 500m,
            People = 2,
        }, Ct));
        var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();
        db.Add(new LandlordProfile("Max Mustermann", "Musterweg 1", "12345", "Musterstadt") { Iban = "DE02 1203 0000 0000 2020 51", Bank = "Musterbank" });
        await db.SaveChangesAsync(Ct);
        var templates = scope.ServiceProvider.GetRequiredService<IDocumentTemplates>();
        await templates.InstallDefaultsAsync(Ct);
        var template = (await templates.ListAsync(Ct)).Single(t => t.Name == "Mietvertrag Wohnraum").Id;
        contract = Validate(await scope.ServiceProvider.GetRequiredService<ILeaseContracts>().CreateDraftAsync(lease, template, Ct));
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    private static string TokenOf(SignatureLink link) => link.Url[(link.Url.LastIndexOf('/') + 1)..];

    private static SigningInput Input(string name, string? drawing = null) => new() { TypedName = name, SignatureData = drawing, Accepted = true };

    private static readonly SigningEvidence Evidence = new("203.0.113.7", "Testbrowser");

    [Fact]
    public async Task Everyone_signs_and_the_contract_is_filed_with_a_protocol_page()
    {
        var started = await WithAsync<IContractSignatures, SignatureLinks>(s => s.StartAsync(contract, true, Ct));

        Assert.True(started.Succeeded, string.Join(", ", started.Errors));
        Assert.Equal(3, started.Links.Count);
        var mail = Assert.Single(await WithDbAsync(db => db.Set<OutgoingMail>().Where(m => m.Template == "signature-request").ToListAsync(Ct)));
        Assert.Equal("anna@example.de", mail.To.Single());
        Assert.Contains("/unterschrift/", mail.Html, StringComparison.Ordinal);

        var results = new List<SigningResult>();
        foreach (var link in started.Links)
        {
            results.Add(await WithAsync<IContractSignatures, SigningResult>(s => s.SignAsync(TokenOf(link), Input(link.SignerName, Convert.ToBase64String(Png)), Evidence, Ct)));
        }

        Assert.All(results, r => Assert.True(r.Succeeded, string.Join(", ", r.Errors)));
        Assert.True(results[^1].AllSigned);
        var row = (await WithAsync<ILeaseContracts, IReadOnlyList<ContractRow>>(c => c.ListAsync(lease, Ct))).Single(r => r.Id == contract);
        Assert.Equal(ContractStatus.Concluded, row.Status);
        var fileId = (await WithDbAsync(db => db.Set<Document>().SingleAsync(d => d.Id == row.SignedDocumentId, Ct))).FileId;
        var text = await PdfTextAsync(fileId);
        Assert.Contains("Mietvertrag über Wohnraum", text, StringComparison.Ordinal);
        Assert.Contains("Signaturprotokoll", text, StringComparison.Ordinal);
        Assert.Contains("203.0.113.7", text, StringComparison.Ordinal);
        var signatures = await WithAsync<IContractSignatures, IReadOnlyList<SignatureRow>>(s => s.ListAsync(contract, Ct));
        Assert.All(signatures, s => Assert.NotNull(s.SignedAt));
        Assert.Single(signatures.Select(s => s.DocumentHash).Distinct());
        var compact = text.Replace(" ", "", StringComparison.Ordinal);
        Assert.Contains(signatures[0].DocumentHash![..40], compact, StringComparison.Ordinal);
        Assert.Contains(signatures[0].DocumentHash![^8..], compact, StringComparison.Ordinal);
        Assert.True(compact.IndexOf("Mieter", compact.IndexOf("Unterschriften", StringComparison.Ordinal), StringComparison.Ordinal) < compact.IndexOf("Vermieter", compact.IndexOf("Unterschriften", StringComparison.Ordinal), StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_typed_name_alone_is_enough_but_without_name_or_confirmation_nothing_is_signed()
    {
        var link = (await WithAsync<IContractSignatures, SignatureLinks>(s => s.StartAsync(contract, false, Ct))).Links[0];

        var noName = await WithAsync<IContractSignatures, SigningResult>(s => s.SignAsync(TokenOf(link), Input(""), Evidence, Ct));
        var unconfirmed = await WithAsync<IContractSignatures, SigningResult>(s => s.SignAsync(TokenOf(link), new SigningInput { TypedName = "Anna Schmidt" }, Evidence, Ct));
        var typed = await WithAsync<IContractSignatures, SigningResult>(s => s.SignAsync(TokenOf(link), Input("Anna Schmidt"), Evidence, Ct));
        var again = await WithAsync<IContractSignatures, SigningResult>(s => s.SignAsync(TokenOf(link), Input("Anna Schmidt"), Evidence, Ct));

        Assert.False(noName.Succeeded);
        Assert.False(unconfirmed.Succeeded);
        Assert.True(typed.Succeeded);
        Assert.False(typed.AllSigned); // Ben is still missing
        Assert.False(again.Succeeded);
    }

    [Fact]
    public async Task Unknown_or_expired_links_and_other_data_than_a_png_are_refused()
    {
        var links = (await WithAsync<IContractSignatures, SignatureLinks>(s => s.StartAsync(contract, false, Ct))).Links;

        Assert.Null(await WithAsync<IContractSignatures, SigningView?>(s => s.ForSigningAsync("gibt-es-nicht", Ct)));
        Assert.False((await WithAsync<IContractSignatures, SigningResult>(s => s.SignAsync("gibt-es-nicht", Input("X"), Evidence, Ct))).Succeeded);
        var notPng = await WithAsync<IContractSignatures, SigningResult>(s => s.SignAsync(TokenOf(links[0]), Input("Anna", Convert.ToBase64String("kein Bild"u8.ToArray())), Evidence, Ct));
        Assert.False(notPng.Succeeded);

        environment.Clock.Advance(TimeSpan.FromDays(15));
        var view = (await WithAsync<IContractSignatures, SigningView?>(s => s.ForSigningAsync(TokenOf(links[0]), Ct)))!;
        Assert.True(view.Expired);
        Assert.False((await WithAsync<IContractSignatures, SigningResult>(s => s.SignAsync(TokenOf(links[0]), Input("Anna"), Evidence, Ct))).Succeeded);
        Assert.Null(await WithAsync<IContractSignatures, Application.Files.OpenedFile?>(s => s.OpenDocumentAsync(TokenOf(links[0]), Ct)));

        // A new link replaces the old one.
        var fresh = (await WithAsync<IContractSignatures, SignatureLinks>(s => s.ReissueAsync(links[0].RequestId, Ct))).Links[0];
        Assert.Null(await WithAsync<IContractSignatures, SigningView?>(s => s.ForSigningAsync(TokenOf(links[0]), Ct)));
        Assert.True((await WithAsync<IContractSignatures, SigningResult>(s => s.SignAsync(TokenOf(fresh), Input("Anna"), Evidence, Ct))).Succeeded);
    }

    [Fact]
    public async Task The_request_can_be_withdrawn_only_before_anyone_signed_and_not_started_twice()
    {
        var links = (await WithAsync<IContractSignatures, SignatureLinks>(s => s.StartAsync(contract, false, Ct))).Links;

        Assert.False((await WithAsync<IContractSignatures, SignatureLinks>(s => s.StartAsync(contract, false, Ct))).Succeeded);
        var document = await WithAsync<IContractSignatures, Application.Files.OpenedFile?>(s => s.OpenDocumentAsync(TokenOf(links[0]), Ct));
        Assert.Equal("application/pdf", document!.StoredFile.MimeType);
        await document.DisposeAsync();

        await WithAsync<IContractSignatures, SigningResult>(s => s.SignAsync(TokenOf(links[0]), Input("Anna"), Evidence, Ct));
        Assert.False((await WithAsync<IContractSignatures, Result>(s => s.CancelAsync(contract, Ct))).Succeeded);
    }

    private async Task<T> WithAsync<TService, T>(Func<TService, Task<T>> action)
        where TService : notnull
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private async Task<T> WithDbAsync<T>(Func<ElocaroDbContext, Task<T>> action)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ElocaroDbContext>());
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

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }
}
