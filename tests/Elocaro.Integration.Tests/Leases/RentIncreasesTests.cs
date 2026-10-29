using Elocaro.Application.Common;
using Elocaro.Application.Documents;
using Elocaro.Application.Files;
using Elocaro.Application.Leases;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Domain.Admin;
using Elocaro.Domain.Documents;
using Elocaro.Domain.Leases;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Leases;

/// <summary>Rent increase assistant with real letters (Gotenberg): comparative rent, index, flat rate and graduated information.</summary>
public class RentIncreasesTests(PostgresFixture postgres, GotenbergFixture gotenberg) : IClassFixture<GotenbergFixture>, IAsyncLifetime
{
    private InfrastructureEnvironment environment = null!;
    private Guid standardLease;
    private Guid indexLease;
    private Guid roomLease;
    private Guid graduatedLease;
    private IReadOnlyList<TemplateRow> templates = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres, new Dictionary<string, string?> { ["Pdf:GotenbergUrl"] = gotenberg.Url });
        environment.Clock.SetUtcNow(new DateTimeOffset(2026, 3, 10, 10, 0, 0, TimeSpan.Zero));
        await using var scope = environment.Services.CreateAsyncScope();
        var properties = scope.ServiceProvider.GetRequiredService<IPropertyService>();
        var people = scope.ServiceProvider.GetRequiredService<IPersonService>();
        var leases = scope.ServiceProvider.GetRequiredService<ILeaseService>();

        var house = Validate(await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln", RentCapPercent = 20 }, Ct));
        var start = new DateOnly(2022, 4, 1);

        async Task<Guid> LeaseAsync(string unitName, string lastName, decimal rent, Action<LeaseCreation>? tweak = null, Guid? rentalUnit = null)
        {
            var unit = rentalUnit ?? Validate(await properties.CreateUnitAsync(house, new UnitInput { Name = unitName }, Ct));
            if (rentalUnit is null)
            {
                Validate(await properties.SetUnitAreaAsync(unit, new AreaInput { From = new DateOnly(2020, 1, 1), Area = 60m }, Ct));
            }

            var person = Validate(await people.CreateAsync(new PersonInput { FirstName = "Test", LastName = lastName, Salutation = Elocaro.Domain.People.Salutation.Mrs }, null, Ct));
            var creation = new LeaseCreation { RentalUnitId = unit, Start = start, TenantIds = [person], BaseRent = rent, OperatingCostPrepayment = 100m, People = 1 };
            tweak?.Invoke(creation);
            return Validate(await leases.CreateAsync(creation, Ct));
        }

        standardLease = await LeaseAsync("Wohnung 1", "Standard", 500m);
        indexLease = await LeaseAsync("Wohnung 2", "Index", 500m);
        Validate(await leases.SetRentModelAsync(indexLease, new RentModelInput { Model = RentModel.Index, IndexBaseMonth = new DateOnly(2024, 1, 1), IndexBaseValue = 116.3m }, Ct));
        graduatedLease = await LeaseAsync("Wohnung 3", "Staffel", 500m);
        Validate(await leases.SetRentModelAsync(graduatedLease, new RentModelInput { Model = RentModel.Graduated }, Ct));
        Validate(await leases.AddRentStepAsync(graduatedLease, new RentStepInput { From = new DateOnly(2027, 4, 1), BaseRent = 540m }, Ct));

        var flat = Validate(await properties.CreateUnitAsync(house, new UnitInput { Name = "WG Nord" }, Ct));
        var room = Validate(await properties.CreateRoomAsync(flat, new RoomInput { Name = "Zimmer 1" }, Ct));
        Validate(await properties.SetRoomAreaAsync(room, new AreaInput { From = new DateOnly(2020, 1, 1), Area = 14m, SecondaryArea = 11.5m }, Ct));
        roomLease = await LeaseAsync("", "Zimmer", 400m, c =>
        {
            c.BillingType = BillingType.FlatRate;
            c.OperatingCostFlatRate = 80m;
            c.OperatingCostPrepayment = null;
        }, room);

        var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();
        db.Add(new LandlordProfile("Max Mustermann", "Musterweg 1", "12345", "Musterstadt") { Iban = "DE02 1203 0000 0000 2020 51", Bank = "Musterbank" });
        db.Add(new CpiValue(new DateOnly(2026, 1, 1), 121.2m));
        await db.SaveChangesAsync(Ct);

        var documents = scope.ServiceProvider.GetRequiredService<IDocumentTemplates>();
        await documents.InstallDefaultsAsync(Ct);
        templates = await documents.ListAsync(Ct);
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Comparative_demand_checks_the_limits_writes_the_letter_and_enters_the_rent_after_consent()
    {
        var input = ComparativeInput();
        var check = await WithAsync<IRentIncreases, RentIncreaseCheck>(r => r.CheckAsync(standardLease, input, Ct));

        // 9.30 €/m² × 60 m² = 558.00 €; cap limit 500 € + 20 % = 600 €; effective on 1 June.
        Assert.True(check.IsAllowed, string.Join(' ', check.Errors));
        Assert.Equal((558m, 558m, 600m, new DateOnly(2026, 6, 1), new DateOnly(2026, 5, 31)), (check.ComparativeRent, check.NewAmount, check.CapLimit, check.EffectiveFrom, check.ConsentDeadline));

        var id = Validate(await WithAsync<IRentIncreases, Result>(r => r.CreateDraftAsync(standardLease, input, Ct)));
        var row = (await Overview(standardLease)).Increases.Single();
        Assert.Equal((RentIncreaseStatus.Draft, 500m, 558m), (row.Status, row.CurrentAmount, row.NewAmount));

        var text = await PdfTextAsync(row.DocumentId!.Value);
        Assert.Contains("Mieterhöhungsverlangen nach § 558 BGB", text, StringComparison.Ordinal);
        Assert.Contains("Köln 2025", text, StringComparison.Ordinal);
        Assert.Contains("558,00 €", text, StringComparison.Ordinal);
        Assert.Contains("01.06.2026", text, StringComparison.Ordinal);
        Assert.Contains("31.05.2026", text, StringComparison.Ordinal);
        Assert.Contains("Kappungsgrenze", text, StringComparison.Ordinal);
        Assert.Contains("600,00 €", text, StringComparison.Ordinal);
        Assert.Contains("Zustimmungserklärung", text, StringComparison.Ordinal);

        Validate(await WithAsync<IRentIncreases, Result>(r => r.DeclareAsync(id, new DateOnly(2026, 3, 14), Ct)));
        var declared = (await Overview(standardLease)).Increases.Single();
        Assert.Equal((RentIncreaseStatus.Declared, new DateOnly(2026, 6, 1), new DateOnly(2026, 5, 31)), (declared.Status, declared.EffectiveFrom, declared.ConsentDeadline));
        Assert.Equal(500m, (await WithAsync<ILeaseService, LeaseDetails?>(l => l.GetAsync(standardLease, Ct)))!.CurrentComponents.Single(c => c.Kind == RentComponentType.BaseRent).Amount); // nothing before consent

        var late = await WithAsync<IRentIncreases, Result>(r => r.AgreeAsync(id, new DateOnly(2026, 6, 2), Ct));
        Assert.Contains("Zustimmungsfrist", late.Errors.Single(), StringComparison.Ordinal);

        Validate(await WithAsync<IRentIncreases, Result>(r => r.AgreeAsync(id, new DateOnly(2026, 5, 20), Ct)));
        var lease = (await WithAsync<ILeaseService, LeaseDetails?>(l => l.GetAsync(standardLease, Ct)))!;
        var baseRents = lease.Components.Where(c => c.Kind == RentComponentType.BaseRent).OrderBy(c => c.Validity.From).ToList();
        Assert.Equal([(500m, new DateOnly(2022, 4, 1)), (558m, new DateOnly(2026, 6, 1))], baseRents.Select(c => (c.Amount, c.Validity.From)));
        Assert.Equal(RentIncreaseStatus.Agreed, (await Overview(standardLease)).Increases.Single().Status);

        await using var scope = environment.Services.CreateAsyncScope();
        var document = await scope.ServiceProvider.GetRequiredService<ElocaroDbContext>().Set<Document>().SingleAsync(d => d.Id == row.DocumentId, Ct);
        Assert.Equal((DocumentCategory.Correspondence, standardLease), (document.Category, document.LeaseId!.Value));
        Assert.NotNull(document.TemplateRevisionId);
    }

    [Fact]
    public async Task Demands_above_the_cap_limit_or_the_comparative_rent_and_during_a_graduated_or_index_rent_are_refused()
    {
        var tooHigh = ComparativeInput();
        tooHigh.NewAmount = 620m; // above 600 € cap limit and above the comparative rent
        var check = await WithAsync<IRentIncreases, RentIncreaseCheck>(r => r.CheckAsync(standardLease, tooHigh, Ct));
        var refused = await WithAsync<IRentIncreases, Result>(r => r.CreateDraftAsync(standardLease, tooHigh, Ct));
        var onIndex = await WithAsync<IRentIncreases, RentIncreaseCheck>(r => r.CheckAsync(indexLease, ComparativeInput(), Ct));
        var onGraduated = await WithAsync<IRentIncreases, RentIncreaseCheck>(r => r.CheckAsync(graduatedLease, ComparativeInput(), Ct));

        Assert.Contains(check.Errors, e => e.Contains("Kappungsgrenze von 20 %", StringComparison.Ordinal));
        Assert.Contains(check.Errors, e => e.Contains("Vergleichsmiete von 558,00 €", StringComparison.Ordinal));
        Assert.False(refused.Succeeded);
        Assert.Empty((await Overview(standardLease)).Increases);
        Assert.Contains(onIndex.Errors, e => e.Contains("§ 557b Abs. 2", StringComparison.Ordinal));
        Assert.Contains(onGraduated.Errors, e => e.Contains("§ 557a Abs. 2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Waiting_periods_follow_the_last_increase()
    {
        await WithAsync<ILeaseService, Result>(l => l.SetComponentAsync(standardLease, new ComponentInput { Kind = RentComponentType.BaseRent, From = new DateOnly(2025, 9, 1), Amount = 520m }, Ct));

        var check = await WithAsync<IRentIncreases, RentIncreaseCheck>(r => r.CheckAsync(standardLease, ComparativeInput(), Ct));

        Assert.Contains(check.Errors, e => e.Contains("frühestens ein Jahr nach der letzten Mieterhöhung", StringComparison.Ordinal));
        Assert.Equal(new DateOnly(2026, 9, 1), check.EarliestDeclaration);
        Assert.Equal(520m, (await Overview(standardLease)).BaseRent);
    }

    [Fact]
    public async Task Index_declaration_names_the_index_values_and_becomes_effective_in_the_lease_on_receipt()
    {
        var input = new RentIncreaseInput { Kind = RentIncreaseKind.Index, TemplateId = TemplateId("Indexmiete") };

        var id = Validate(await WithAsync<IRentIncreases, Result>(r => r.CreateDraftAsync(indexLease, input, Ct)));
        var row = (await Overview(indexLease)).Increases.Single();
        var text = await PdfTextAsync(row.DocumentId!.Value);
        Assert.Contains("Mieterhöhung nach § 557b BGB", text, StringComparison.Ordinal);
        Assert.Contains("116,3", text, StringComparison.Ordinal);
        Assert.Contains("121,2", text, StringComparison.Ordinal);
        Assert.Contains("521,07 €", text, StringComparison.Ordinal); // 500 × 121.2 / 116.3
        Assert.Contains("01.05.2026", text, StringComparison.Ordinal); // month after next after receipt

        Validate(await WithAsync<IRentIncreases, Result>(r => r.DeclareAsync(id, new DateOnly(2026, 3, 14), Ct)));

        var overview = await Overview(indexLease);
        Assert.Equal(RentIncreaseStatus.InForce, overview.Increases.Single().Status);
        var lease = (await WithAsync<ILeaseService, LeaseDetails?>(l => l.GetAsync(indexLease, Ct)))!;
        Assert.Equal(521.07m, lease.Components.Where(c => c.Kind == RentComponentType.BaseRent).OrderBy(c => c.Validity.From).Last().Amount);
        Assert.Equal((new DateOnly(2026, 1, 1), 121.2m), (lease.RentModel.IndexBaseMonth, lease.RentModel.IndexBaseValue));
    }

    [Fact]
    public async Task Flat_rate_increase_needs_the_reservation_and_a_reason_and_applies_from_the_month_after_next()
    {
        var input = new RentIncreaseInput { Kind = RentIncreaseKind.FlatRate, TemplateId = TemplateId("Betriebskostenpauschale"), NewAmount = 95m };
        var without = await WithAsync<IRentIncreases, RentIncreaseCheck>(r => r.CheckAsync(roomLease, input, Ct));
        Assert.Equal(2, without.Errors.Count);

        input.ReservationConfirmed = true;
        input.CostReason = "Die Energiekosten der Wohnung sind seit Mietbeginn von 2.400 € auf 3.100 € im Jahr gestiegen.";
        var id = Validate(await WithAsync<IRentIncreases, Result>(r => r.CreateDraftAsync(roomLease, input, Ct)));

        var row = (await Overview(roomLease)).Increases.Single();
        var text = await PdfTextAsync(row.DocumentId!.Value);
        Assert.Contains("Betriebskostenpauschale", text, StringComparison.Ordinal);
        Assert.Contains("95,00 €", text, StringComparison.Ordinal);
        Assert.Contains("3.100", text, StringComparison.Ordinal);

        Validate(await WithAsync<IRentIncreases, Result>(r => r.DeclareAsync(id, new DateOnly(2026, 3, 14), Ct)));
        var lease = (await WithAsync<ILeaseService, LeaseDetails?>(l => l.GetAsync(roomLease, Ct)))!;
        Assert.Equal([(80m, new DateOnly(2022, 4, 1)), (95m, new DateOnly(2026, 5, 1))], lease.Components.Where(c => c.Kind == RentComponentType.OperatingCostFlatRate).OrderBy(c => c.Validity.From).Select(c => (c.Amount, c.Validity.From)));
    }

    [Fact]
    public async Task Graduated_information_names_the_next_step_and_changes_nothing_in_the_lease()
    {
        var input = new RentIncreaseInput { Kind = RentIncreaseKind.Graduated, TemplateId = TemplateId("Staffelmiete") };

        var id = Validate(await WithAsync<IRentIncreases, Result>(r => r.CreateDraftAsync(graduatedLease, input, Ct)));
        var row = (await Overview(graduatedLease)).Increases.Single();
        var text = await PdfTextAsync(row.DocumentId!.Value);
        Assert.Contains("Staffelmiete", text, StringComparison.Ordinal);
        Assert.Contains("540,00 €", text, StringComparison.Ordinal);
        Assert.Contains("01.04.2027", text, StringComparison.Ordinal);

        Validate(await WithAsync<IRentIncreases, Result>(r => r.DeclareAsync(id, new DateOnly(2026, 3, 14), Ct)));
        Assert.Equal(RentIncreaseStatus.Declared, (await Overview(graduatedLease)).Increases.Single().Status);
        Assert.Equal(500m, (await Overview(graduatedLease)).BaseRent);
    }

    [Fact]
    public async Task A_draft_can_be_withdrawn_and_a_refused_demand_changes_no_rent()
    {
        var first = Validate(await WithAsync<IRentIncreases, Result>(r => r.CreateDraftAsync(standardLease, ComparativeInput(), Ct)));
        Validate(await WithAsync<IRentIncreases, Result>(r => r.WithdrawAsync(first, Ct)));
        var second = Validate(await WithAsync<IRentIncreases, Result>(r => r.CreateDraftAsync(standardLease, ComparativeInput(), Ct)));
        Validate(await WithAsync<IRentIncreases, Result>(r => r.DeclareAsync(second, new DateOnly(2026, 3, 14), Ct)));
        Validate(await WithAsync<IRentIncreases, Result>(r => r.RefuseAsync(second, Ct)));

        var overview = await Overview(standardLease);
        Assert.Equal([RentIncreaseStatus.Refused, RentIncreaseStatus.Withdrawn], overview.Increases.Select(i => i.Status));
        Assert.Equal(500m, overview.BaseRent);
        Assert.False((await WithAsync<IRentIncreases, Result>(r => r.WithdrawAsync(second, Ct))).Succeeded);
    }

    [Fact]
    public async Task Modernisation_levy_is_calculated_with_the_cap_and_enters_the_rent_from_the_third_month()
    {
        var input = new RentIncreaseInput
        {
            Kind = RentIncreaseKind.Modernisation,
            TemplateId = TemplateId("Modernisierung"),
            ModernisationDescription = "Austausch der Fenster (Energieeinsparung).",
            ModernisationCosts = 6000m,
            MaintenanceShare = 1000m,
        };
        var check = await WithAsync<IRentIncreases, RentIncreaseCheck>(r => r.CheckAsync(standardLease, input, Ct));
        Assert.True(check.IsAllowed, string.Join(" ", check.Errors));
        Assert.Equal((533.33m, new DateOnly(2026, 6, 1)), (check.NewAmount, check.EffectiveFrom));

        var id = Validate(await WithAsync<IRentIncreases, Result>(r => r.CreateDraftAsync(standardLease, input, Ct)));
        var row = Assert.Single((await Overview(standardLease)).Increases);
        var text = await PdfTextAsync(row.DocumentId!.Value);
        Assert.Contains("Austausch der Fenster", text, StringComparison.Ordinal);
        Assert.Contains("5.000,00", text, StringComparison.Ordinal);
        Assert.Contains("533,33", text, StringComparison.Ordinal);
        Assert.Contains("Härte", text, StringComparison.Ordinal);

        Validate(await WithAsync<IRentIncreases, Result>(r => r.DeclareAsync(id, new DateOnly(2026, 3, 14), Ct)));
        var lease = (await WithAsync<ILeaseService, LeaseDetails?>(l => l.GetAsync(standardLease, Ct)))!;
        Assert.Contains(lease.Components, c => c.Kind == RentComponentType.BaseRent && c.Amount == 533.33m && c.Validity.From == new DateOnly(2026, 6, 1));
        Assert.Equal(RentIncreaseStatus.InForce, (await Overview(standardLease)).Increases.Single().Status);

        // The first levy counts against the cap of the next one.
        var second = await WithAsync<IRentIncreases, RentIncreaseCheck>(r => r.CheckAsync(standardLease, input, Ct));
        Assert.True(second.IsAllowed);
    }

    [Fact]
    public async Task Modernisation_without_description_or_costs_is_refused()
    {
        var input = new RentIncreaseInput { Kind = RentIncreaseKind.Modernisation, TemplateId = TemplateId("Modernisierung") };

        var check = await WithAsync<IRentIncreases, RentIncreaseCheck>(r => r.CheckAsync(standardLease, input, Ct));

        Assert.False(check.IsAllowed);
        Assert.False((await WithAsync<IRentIncreases, Result>(r => r.CreateDraftAsync(standardLease, input, Ct))).Succeeded);
    }

    private RentIncreaseInput ComparativeInput() => new()
    {
        Kind = RentIncreaseKind.Comparative,
        TemplateId = TemplateId("Vergleichsmiete"),
        Basis = ComparativeBasis.RentIndex,
        RentIndexName = "Köln 2025",
        PerSquareMetreFrom = 7.5m,
        PerSquareMetreTo = 10.5m,
        PerSquareMetre = 9.30m,
        FeatureText = "Baujahr 1965, normale Lage, Bad mit Fenster.",
    };

    private Guid TemplateId(string part) => templates.Single(t => t.Kind == TemplateKind.RentIncrease && t.Name.Contains(part, StringComparison.Ordinal)).Id;

    private Task<RentIncreaseOverview> Overview(Guid leaseId) =>
        WithAsync<IRentIncreases, RentIncreaseOverview>(async r => (await r.OverviewAsync(leaseId, Ct))!);

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

    private async Task<string> PdfTextAsync(Guid documentId)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var fileId = (await scope.ServiceProvider.GetRequiredService<ElocaroDbContext>().Set<Document>().SingleAsync(d => d.Id == documentId, Ct)).FileId;
        await using var opened = (await scope.ServiceProvider.GetRequiredService<IFileStore>().OpenAsync(fileId, Ct))!;
        using var buffer = new MemoryStream();
        await opened.Content.CopyToAsync(buffer, Ct);
        using var document = UglyToad.PdfPig.PdfDocument.Open(buffer.ToArray());
        return string.Join(' ', document.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text)).Replace("ﬁ", "fi", StringComparison.Ordinal).Replace("ﬂ", "fl", StringComparison.Ordinal).Replace("ﬀ", "ff", StringComparison.Ordinal);
    }
}
