using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Common;
using MietHahn.Application.Documents;
using MietHahn.Application.Files;
using MietHahn.Application.Leases;
using MietHahn.Application.People;
using MietHahn.Application.Properties;
using MietHahn.Domain.Admin;
using MietHahn.Domain.Documents;
using MietHahn.Domain.Leases;
using MietHahn.Domain.Properties;
using MietHahn.Infrastructure.Persistence;
using MietHahn.Integration.Tests.Infrastructure;

namespace MietHahn.Integration.Tests.Leases;

/// <summary>Contract generator with real PDFs (Gotenberg): apartment and shared-flat room contract, draft → signed copy.</summary>
public class LeaseContractsTests(PostgresFixture postgres, GotenbergFixture gotenberg) : IClassFixture<GotenbergFixture>, IAsyncLifetime
{
    private InfrastructureEnvironment environment = null!;
    private Guid apartmentLease;
    private Guid roomLease;
    private Guid apartmentTemplate;
    private Guid roomTemplate;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres, new Dictionary<string, string?> { ["Pdf:GotenbergUrl"] = gotenberg.Url });
        await using var scope = environment.Services.CreateAsyncScope();
        var properties = scope.ServiceProvider.GetRequiredService<IPropertyService>();
        var people = scope.ServiceProvider.GetRequiredService<IPersonService>();
        var leases = scope.ServiceProvider.GetRequiredService<ILeaseService>();

        var house = Validate(await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln", YearBuilt = 1965 }, Ct));
        var apartment = Validate(await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 1" }, Ct));
        Validate(await properties.SetUnitAreaAsync(apartment, new AreaInput { From = new DateOnly(2020, 1, 1), Area = 60m }, Ct));
        var anna = Validate(await people.CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt", Salutation = MietHahn.Domain.People.Salutation.Mrs, DateOfBirth = new DateOnly(1985, 3, 14) }, null, Ct));
        apartmentLease = Validate(await leases.CreateAsync(new LeaseCreation
        {
            RentalUnitId = apartment,
            Start = new DateOnly(2026, 11, 1),
            TenantIds = [anna],
            BaseRent = 500m,
            OperatingCostPrepayment = 100m,
            HeatingCostPrepayment = 60m,
            People = 2,
            Data = new LeaseInput { Deposit = 1500m },
        }, Ct));

        var flat = Validate(await properties.CreateUnitAsync(house, new UnitInput { Name = "WG Nord" }, Ct));
        var room1 = Validate(await properties.CreateRoomAsync(flat, new RoomInput { Name = "Zimmer 1", Furnishing = Furnishing.PartlyFurnished, FurnishingList = "Bett\nSchrank" }, Ct));
        Validate(await properties.CreateRoomAsync(flat, new RoomInput { Name = "Zimmer 2" }, Ct));
        Validate(await properties.CreateRoomAsync(flat, new RoomInput { Name = "Zimmer 3" }, Ct));
        Validate(await properties.SetRoomAreaAsync(room1, new AreaInput { From = new DateOnly(2020, 1, 1), Area = 14m, SecondaryArea = 11.5m }, Ct));
        var ben = Validate(await people.CreateAsync(new PersonInput { FirstName = "Ben", LastName = "Weber" }, null, Ct));
        roomLease = Validate(await leases.CreateAsync(new LeaseCreation
        {
            RentalUnitId = room1,
            Start = new DateOnly(2026, 11, 1),
            TenantIds = [ben],
            BillingType = BillingType.FlatRate,
            BaseRent = 400m,
            OperatingCostFlatRate = 80m,
            People = 1,
        }, Ct));

        var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
        db.Add(new LandlordProfile("Andy Hahn", "Musterweg 1", "12345", "Musterstadt") { Iban = "DE02 1203 0000 0000 2020 51", Bank = "Musterbank" });
        await db.SaveChangesAsync(Ct);

        var templates = scope.ServiceProvider.GetRequiredService<IDocumentTemplates>();
        await templates.InstallDefaultsAsync(Ct);
        var all = await templates.ListAsync(Ct);
        apartmentTemplate = all.Single(t => t.Name == "Mietvertrag Wohnraum").Id;
        roomTemplate = all.Single(t => t.Name == "Zimmervertrag WG").Id;
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Apartment_contract_contains_parties_areas_rent_costs_deposit_and_annexes_from_the_master_data()
    {
        var contract = Validate(await WithAsync<ILeaseContracts, Result>(c => c.CreateDraftAsync(apartmentLease, apartmentTemplate, Ct)));

        var text = await PdfTextAsync((await WithAsync<ILeaseContracts, IReadOnlyList<ContractRow>>(c => c.ListAsync(apartmentLease, Ct))).Single(r => r.Id == contract).DraftFileId);
        Assert.Contains("Mietvertrag über Wohnraum", text, StringComparison.Ordinal);
        Assert.Contains("Anna Schmidt", text, StringComparison.Ordinal);
        Assert.Contains("geboren am 14.03.1985", text, StringComparison.Ordinal);
        Assert.Contains("Wohnung 1", text, StringComparison.Ordinal);
        Assert.Contains("60,00 m²", text, StringComparison.Ordinal);
        Assert.Contains("Baujahr 1965", text, StringComparison.Ordinal);
        Assert.Contains("beginnt am 01.11.2026", text, StringComparison.Ordinal);
        Assert.Contains("500,00 €", text, StringComparison.Ordinal);
        Assert.Contains("660,00 €", text, StringComparison.Ordinal); // total: 500 + 100 + 60
        Assert.Contains("Wasserversorgung", text, StringComparison.Ordinal);
        Assert.Contains("1.500,00 €", text, StringComparison.Ordinal); // deposit and legal maximum (3 × 500)
        Assert.Contains("Hausordnung für Lindenweg 3", text, StringComparison.Ordinal);
        Assert.Contains("Datenschutzinformation für Mieter", text, StringComparison.Ordinal);
        Assert.Contains("Übergabeprotokoll", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Gemeinschaftsflächen", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Room_contract_takes_flat_rate_common_areas_and_furnishing_from_the_master_data()
    {
        var contract = Validate(await WithAsync<ILeaseContracts, Result>(c => c.CreateDraftAsync(roomLease, roomTemplate, Ct)));

        var text = await PdfTextAsync((await WithAsync<ILeaseContracts, IReadOnlyList<ContractRow>>(c => c.ListAsync(roomLease, Ct))).Single(r => r.Id == contract).DraftFileId);
        Assert.Contains("Zimmer in einer Wohngemeinschaft", text, StringComparison.Ordinal);
        Assert.Contains("Ben Weber", text, StringComparison.Ordinal);
        Assert.Contains("Zimmer 1", text, StringComparison.Ordinal);
        Assert.Contains("14,00 m²", text, StringComparison.Ordinal);
        Assert.Contains("11,50 m²", text, StringComparison.Ordinal); // share of the common areas
        Assert.Contains("Zimmer 2, Zimmer 3", text, StringComparison.Ordinal);
        Assert.Contains("teilmöbliert", text, StringComparison.Ordinal);
        Assert.Contains("Bett", text, StringComparison.Ordinal);
        Assert.Contains("Schrank", text, StringComparison.Ordinal);
        Assert.Contains("Pauschale", text, StringComparison.Ordinal);
        Assert.Contains("80,00 €", text, StringComparison.Ordinal); // flat rate
        Assert.Contains("480,00 €", text, StringComparison.Ordinal); // total: 400 + 80
        Assert.DoesNotContain("Wasserversorgung", text, StringComparison.Ordinal); // no statement with a list of cost types
    }

    [Fact]
    public async Task Draft_to_signed_copy_files_the_document_with_the_template_version_and_a_new_draft_replaces_the_old_one()
    {
        var first = Validate(await WithAsync<ILeaseContracts, Result>(c => c.CreateDraftAsync(apartmentLease, apartmentTemplate, Ct)));
        var second = Validate(await WithAsync<ILeaseContracts, Result>(c => c.CreateDraftAsync(apartmentLease, apartmentTemplate, Ct)));

        var listed = await WithAsync<ILeaseContracts, IReadOnlyList<ContractRow>>(c => c.ListAsync(apartmentLease, Ct));
        Assert.Equal(ContractStatus.Withdrawn, listed.Single(r => r.Id == first).Status);
        Assert.Equal(ContractStatus.Draft, listed.Single(r => r.Id == second).Status);

        using var signed = new MemoryStream("%PDF-1.4 unterschrieben"u8.ToArray());
        var concluded = await WithAsync<ILeaseContracts, Result>(c => c.ConcludeAsync(second, new SignedContractInput { SignedOn = new DateOnly(2026, 10, 25), ShareWithTenants = true }, "vertrag-unterschrieben.pdf", signed, Ct));
        Assert.True(concluded.Succeeded, string.Join(", ", concluded.Errors));

        var row = (await WithAsync<ILeaseContracts, IReadOnlyList<ContractRow>>(c => c.ListAsync(apartmentLease, Ct))).Single(r => r.Id == second);
        Assert.Equal((ContractStatus.Concluded, new DateOnly(2026, 10, 25), "Mietvertrag Wohnraum", 1), (row.Status, row.SignedOn, row.TemplateName, row.TemplateRevision));
        await using var scope = environment.Services.CreateAsyncScope();
        var document = await scope.ServiceProvider.GetRequiredService<MietHahnDbContext>().Set<Document>().SingleAsync(d => d.Id == row.SignedDocumentId, Ct);
        Assert.Equal((DocumentCategory.Lease, apartmentLease, true), (document.Category, document.LeaseId!.Value, document.SharedWithTenants));
        Assert.NotNull(document.TemplateRevisionId);

        using var again = new MemoryStream("x"u8.ToArray());
        var twice = await WithAsync<ILeaseContracts, Result>(c => c.ConcludeAsync(second, new SignedContractInput { SignedOn = new DateOnly(2026, 10, 26) }, "nochmal.pdf", again, Ct));
        var withdraw = await WithAsync<ILeaseContracts, Result>(c => c.WithdrawAsync(second, Ct));
        Assert.Contains("bereits", twice.Errors.Single(), StringComparison.Ordinal);
        Assert.False(withdraw.Succeeded);
    }

    [Fact]
    public async Task Only_contract_templates_can_be_used_and_the_preview_stores_nothing()
    {
        var letter = (await WithAsync<IDocumentTemplates, IReadOnlyList<TemplateRow>>(t => t.ListAsync(Ct))).First(t => t.Kind == TemplateKind.Letter).Id;

        var refused = await WithAsync<ILeaseContracts, Result>(c => c.CreateDraftAsync(apartmentLease, letter, Ct));
        var preview = await WithAsync<ILeaseContracts, TemplatePreview>(c => c.PreviewAsync(apartmentTemplate, apartmentLease, Ct));

        Assert.Contains("Mietvertrag", refused.Errors.Single(), StringComparison.Ordinal);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(preview.Pdf!, 0, 4), StringComparison.Ordinal);
        Assert.Empty(await WithAsync<ILeaseContracts, IReadOnlyList<ContractRow>>(c => c.ListAsync(apartmentLease, Ct)));
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
