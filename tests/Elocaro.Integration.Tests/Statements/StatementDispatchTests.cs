using Elocaro.Application.Accounts;
using Elocaro.Application.Common;
using Elocaro.Application.Files;
using Elocaro.Application.Finance;
using Elocaro.Application.Heating;
using Elocaro.Application.Leases;
using Elocaro.Application.Metering;
using Elocaro.Application.OperatingCosts;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Application.Receipts;
using Elocaro.Application.Statements;
using Elocaro.Domain.Admin;
using Elocaro.Domain.Common;
using Elocaro.Domain.Communication;
using Elocaro.Domain.Documents;
using Elocaro.Domain.Finance;
using Elocaro.Domain.Heating;
using Elocaro.Domain.Leases;
using Elocaro.Domain.Metering;
using Elocaro.Domain.OperatingCosts;
using Elocaro.Domain.Receipts;
using Elocaro.Domain.Statements;
using Elocaro.Infrastructure.Mail;
using Elocaro.Infrastructure.Operations;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Infrastructure.Statements;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elocaro.Integration.Tests.Statements;

/// <summary>Letter (PDF), booking, dispatch with proof, corrections and prepayment proposals of released statements.</summary>
public class StatementDispatchTests(PostgresFixture postgres, GotenbergFixture gotenberg) : IClassFixture<GotenbergFixture>, IAsyncLifetime
{
    private InfrastructureEnvironment environment = null!;
    private Guid house;
    private Guid apartment;
    private Guid leaseAnna;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Period Year2026 => Period.CalendarYear(2026);

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres, new Dictionary<string, string?> { ["Pdf:GotenbergUrl"] = gotenberg.Url });
        await using var scope = environment.Services.CreateAsyncScope();
        var properties = scope.ServiceProvider.GetRequiredService<IPropertyService>();
        house = Validate(await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct));
        apartment = Validate(await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 1" }, Ct));
        Validate(await properties.SetUnitAreaAsync(apartment, new AreaInput { From = new DateOnly(2020, 1, 1), Area = 60m }, Ct));
        var anna = Validate(await scope.ServiceProvider.GetRequiredService<IPersonService>().CreateAsync(
            new PersonInput { FirstName = "Anna", LastName = "Schmidt", Salutation = Elocaro.Domain.People.Salutation.Mrs, Email = "anna@example.de" }, null, Ct));
        leaseAnna = Validate(await scope.ServiceProvider.GetRequiredService<ILeaseService>().CreateAsync(
            new LeaseCreation { RentalUnitId = apartment, Start = new DateOnly(2025, 1, 1), TenantIds = [anna], BaseRent = 500m, OperatingCostPrepayment = 100m, People = 1 }, Ct));
        Validate(await scope.ServiceProvider.GetRequiredService<ICostAllocation>().SetRuleAsync(house, new CostRuleInput { CostType = "betrkv-01", From = new DateOnly(2020, 1, 1), Key = AllocationKey.LivingArea }, Ct));

        var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();
        db.Add(new LandlordProfile("Max Mustermann", "Musterweg 1", "12345", "Musterstadt") { Email = "vermieter@example.de", Iban = "DE02 1203 0000 0000 2020 51", Bank = "Musterbank" });
        await db.SaveChangesAsync(Ct);
        environment.Clock.SetUtcNow(new DateTimeOffset(2027, 2, 10, 10, 0, 0, TimeSpan.Zero));
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Letter_contains_the_formal_requirements_and_dispatch_books_mails_and_shares()
    {
        await ReceiptAsync(1200m);
        var statementId = await ReleaseAsync();

        var preview = await WithAsync<IStatementDispatch, byte[]>(s => s.PreviewAsync(statementId, leaseAnna, new DispatchInput(), Ct));
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(preview, 0, 4), StringComparison.Ordinal);
        await using (var check = environment.Services.CreateAsyncScope())
        {
            Assert.Empty(await check.ServiceProvider.GetRequiredService<ElocaroDbContext>().Set<StatementDispatch>().ToListAsync(Ct)); // preview stores nothing
        }

        var dispatchId = Validate(await WithAsync<IStatementDispatch, Result>(s => s.DispatchAsync(statementId, leaseAnna, new DispatchInput(), Ct)));

        await using var scope = environment.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();
        var dispatch = await db.Set<StatementDispatch>().SingleAsync(Ct);
        Assert.Equal(dispatchId, dispatch.Id);
        Assert.Equal(DispatchChannel.Email, dispatch.Channel);
        Assert.Equal(new DateOnly(2027, 2, 10), dispatch.SentOn);
        Assert.Equal(1200m, dispatch.Balance);
        Assert.Equal(1200m, dispatch.BookedAmount);

        var text = await PdfTextAsync(dispatch.FileId);
        // Formal requirements: period, total costs per cost type, allocation key with explanation, share, prepayments, balance.
        Assert.Contains("vom 01.01.2026 bis zum 31.12.2026", text, StringComparison.Ordinal);
        Assert.Contains("Grundsteuer", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Heizkosten", text, StringComparison.Ordinal); // no heating part without heating costs
        Assert.Contains("1.200,00 €", text, StringComparison.Ordinal);
        Assert.Contains("Wohnfläche", text, StringComparison.Ordinal);
        Assert.Contains("Ihr Anteil am Schlüssel", text, StringComparison.Ordinal);
        Assert.Contains("abzüglich geleisteter Vorauszahlungen", text, StringComparison.Ordinal);
        Assert.Contains("Nachzahlung Betriebskosten", text, StringComparison.Ordinal);
        Assert.Contains("Girocode", text, StringComparison.Ordinal); // payment due and the landlord has an IBAN
        // Inspection of receipts, due date, payment details.
        Assert.Contains("Belegeinsicht", text, StringComparison.Ordinal);
        Assert.Contains("bis zum 12.03.2027", text, StringComparison.Ordinal);
        Assert.Contains("IBAN DE02 1203 0000 0000 2020 51", text, StringComparison.Ordinal);
        Assert.Contains("Sehr geehrte Frau Schmidt,", text, StringComparison.Ordinal);

        var charge = await db.Set<Charge>().SingleAsync(c => c.Kind == ChargeType.AdditionalPayment, Ct);
        Assert.Equal(1200m, charge.Amount);
        Assert.Equal(new DateOnly(2027, 3, 12), charge.Due);
        Assert.Equal(charge.Id, dispatch.ChargeId);

        var mail = await db.Set<OutgoingMail>().SingleAsync(Ct);
        Assert.Equal(["anna@example.de"], mail.To);
        Assert.Equal("Betriebskostenabrechnung 2026", mail.Subject);
        Assert.Equal([dispatch.FileId], mail.AttachmentFileIds);
        Assert.Equal($"statement:{statementId}:{leaseAnna}", mail.Reference);
        Assert.Equal(mail.Id, dispatch.MailId);

        var document = await db.Set<Document>().SingleAsync(Ct);
        Assert.True(document.SharedWithTenants);
        Assert.Equal(DocumentCategory.Statement, document.Category);
        Assert.Equal(leaseAnna, document.LeaseId);

        var twice = await WithAsync<IStatementDispatch, Result>(s => s.DispatchAsync(statementId, leaseAnna, new DispatchInput(), Ct));
        Assert.Contains("bereits versendet", twice.Errors.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Retention_keeps_the_mail_of_a_statement_as_proof_without_its_content()
    {
        await ReceiptAsync(1200m);
        var statementId = await ReleaseAsync();
        Validate(await WithAsync<IStatementDispatch, Result>(s => s.DispatchAsync(statementId, leaseAnna, new DispatchInput(), Ct)));
        await using var scope = environment.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();
        var mailId = (await db.Set<StatementDispatch>().SingleAsync(Ct)).MailId!.Value;
        await db.Set<OutgoingMail>().Where(m => m.Id == mailId).ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, MailStatus.Sent), Ct);
        environment.Clock.Advance(TimeSpan.FromDays(1096));

        await new MailRetention(db, environment.Clock, Options.Create(new MailOptions())).CleanUpAsync(Ct);

        var mail = await db.Set<OutgoingMail>().AsNoTracking().SingleAsync(m => m.Id == mailId, Ct);
        Assert.Equal(OutgoingMail.PurgedContent, mail.Text);
        Assert.Equal(OutgoingMail.PurgedContent, mail.Html);
        Assert.Equal(["anna@example.de"], mail.To);
        Assert.NotEmpty(mail.AttachmentFileIds);
    }

    [Fact]
    public async Task Proof_of_receipt_is_recorded_and_cannot_precede_the_dispatch()
    {
        await ReceiptAsync(1200m);
        var statementId = await ReleaseAsync();
        var dispatchId = Validate(await WithAsync<IStatementDispatch, Result>(s => s.DispatchAsync(statementId, leaseAnna, new DispatchInput { Channel = DispatchChannel.Post }, Ct)));

        var early = await WithAsync<IStatementDispatch, Result>(s => s.ConfirmReceiptAsync(dispatchId, new DeliveryConfirmation { ReceivedOn = new DateOnly(2027, 2, 9) }, Ct));
        Assert.Contains("nicht vor dem Versand", early.Errors.Single(), StringComparison.Ordinal);
        Validate(await WithAsync<IStatementDispatch, Result>(s => s.ConfirmReceiptAsync(dispatchId, new DeliveryConfirmation { ReceivedOn = new DateOnly(2027, 2, 12), Note = "Einwurf-Einschreiben" }, Ct)));

        var row = (await WithAsync<IStatementDispatch, DispatchOverview?>(s => s.OverviewAsync(statementId, Ct)))!.Tenancies.Single().Dispatch!;
        Assert.Equal(DispatchChannel.Post, row.Channel);
        Assert.Null(row.MailStatus);
        Assert.Equal(new DateOnly(2027, 2, 12), row.ReceivedOn);
        Assert.Equal("Einwurf-Einschreiben", row.ReceiptNote);
        await using var scope = environment.Services.CreateAsyncScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<ElocaroDbContext>().Set<OutgoingMail>().ToListAsync(Ct));
    }

    [Fact]
    public async Task Prepayment_proposal_is_part_of_the_letter_and_creates_a_rent_component_only_after_confirmation()
    {
        await ReceiptAsync(1200m);
        var statementId = await ReleaseAsync();
        Validate(await WithAsync<IStatementDispatch, Result>(s => s.DispatchAsync(statementId, leaseAnna, new DispatchInput(), Ct)));

        var row = (await WithAsync<IStatementDispatch, DispatchOverview?>(s => s.OverviewAsync(statementId, Ct)))!.Tenancies.Single().Dispatch!;
        var proposal = Assert.Single(row.Proposals);
        // 1,200.00 € ÷ 12 = 100.00 € + 5 % safety margin = 105.00 €, from the first of the month after next
        Assert.Equal((RentComponentType.OperatingCostPrepayment, 100m, 105m, new DateOnly(2027, 4, 1), ProposalStatus.Open),
            (proposal.Component, proposal.Current, proposal.Proposed, proposal.EffectiveFrom, proposal.Status));
        Assert.Contains("Anpassung Ihrer Vorauszahlungen", await PdfTextAsync(row.FileId), StringComparison.Ordinal);
        Assert.Equal(100m, (await LeaseAsync()).AmountAt(RentComponentType.OperatingCostPrepayment, new DateOnly(2027, 4, 1)));

        Validate(await WithAsync<IStatementDispatch, Result>(s => s.ApplyProposalAsync(proposal.Id, 110m, Ct)));

        var lease = await LeaseAsync();
        Assert.Equal(110m, lease.AmountAt(RentComponentType.OperatingCostPrepayment, new DateOnly(2027, 4, 1)));
        Assert.Equal(100m, lease.AmountAt(RentComponentType.OperatingCostPrepayment, new DateOnly(2027, 3, 31)));
        var decided = (await WithAsync<IStatementDispatch, DispatchOverview?>(s => s.OverviewAsync(statementId, Ct)))!.Tenancies.Single().Dispatch!.Proposals.Single();
        Assert.Equal(ProposalStatus.Applied, decided.Status);
        Assert.False((await WithAsync<IStatementDispatch, Result>(s => s.ApplyProposalAsync(proposal.Id, null, Ct))).Succeeded);
        Assert.False((await WithAsync<IStatementDispatch, Result>(s => s.DeclineProposalAsync(proposal.Id, Ct))).Succeeded);
    }

    [Fact]
    public async Task Declined_or_suppressed_proposal_changes_nothing()
    {
        await ReceiptAsync(1200m);
        var statementId = await ReleaseAsync();
        Validate(await WithAsync<IStatementDispatch, Result>(s => s.DispatchAsync(statementId, leaseAnna, new DispatchInput(), Ct)));
        var proposal = (await WithAsync<IStatementDispatch, DispatchOverview?>(s => s.OverviewAsync(statementId, Ct)))!.Tenancies.Single().Dispatch!.Proposals.Single();

        Validate(await WithAsync<IStatementDispatch, Result>(s => s.DeclineProposalAsync(proposal.Id, Ct)));

        Assert.Equal(100m, (await LeaseAsync()).AmountAt(RentComponentType.OperatingCostPrepayment, new DateOnly(2027, 6, 1)));
    }

    [Fact]
    public async Task Correction_books_only_the_difference_and_after_the_deadline_no_additional_payment()
    {
        await ReceiptAsync(1200m);
        var firstId = await ReleaseAsync();
        Validate(await WithAsync<IStatementDispatch, Result>(s => s.DispatchAsync(firstId, leaseAnna, new DispatchInput { ProposePrepayments = false }, Ct)));

        await ReceiptAsync(600m);
        var secondId = Validate(await WithAsync<IStatements, Result>(s => s.ReleaseAsync(house, Year2026, true, "Nachträgliche Rechnung", Ct)));
        var overview = (await WithAsync<IStatementDispatch, DispatchOverview?>(s => s.OverviewAsync(secondId, Ct)))!.Tenancies.Single();
        Assert.Equal((1800m, 1200m, 600m, false), (overview.Balance, overview.EarlierBooked, overview.ToBook, overview.ClaimExcluded));

        var oldDispatch = await WithAsync<IStatementDispatch, Result>(s => s.DispatchAsync(firstId, leaseAnna, new DispatchInput(), Ct));
        Assert.Contains("ersetzt", oldDispatch.Errors.Single(), StringComparison.Ordinal);

        Validate(await WithAsync<IStatementDispatch, Result>(s => s.DispatchAsync(secondId, leaseAnna, new DispatchInput { ProposePrepayments = false }, Ct)));
        var second = (await WithAsync<IStatementDispatch, DispatchOverview?>(s => s.OverviewAsync(secondId, Ct)))!.Tenancies.Single().Dispatch!;
        Assert.Equal(600m, second.BookedAmount);
        var text = await PdfTextAsync(second.FileId);
        Assert.Contains("Korrigierte Abrechnung (Fassung 2)", text, StringComparison.Ordinal);
        Assert.Contains("Nachträgliche Rechnung", text, StringComparison.Ordinal);
        Assert.Contains("Differenz aus der Korrektur", text, StringComparison.Ordinal);

        await using (var scope = environment.Services.CreateAsyncScope())
        {
            var charges = await scope.ServiceProvider.GetRequiredService<ElocaroDbContext>().Set<Charge>().Where(c => c.Kind == ChargeType.AdditionalPayment).OrderBy(c => c.Amount).ToListAsync(Ct);
            Assert.Equal([600m, 1200m], charges.Select(c => c.Amount));
        }

        // After 31.12.2027 an additional payment can no longer be claimed.
        environment.Clock.SetUtcNow(new DateTimeOffset(2028, 1, 10, 10, 0, 0, TimeSpan.Zero));
        await ReceiptAsync(300m);
        var thirdId = Validate(await WithAsync<IStatements, Result>(s => s.ReleaseAsync(house, Year2026, true, "zu spät", Ct)));
        var late = (await WithAsync<IStatementDispatch, DispatchOverview?>(s => s.OverviewAsync(thirdId, Ct)))!.Tenancies.Single();
        Assert.Equal((0m, true), (late.ToBook, late.ClaimExcluded));
        Validate(await WithAsync<IStatementDispatch, Result>(s => s.DispatchAsync(thirdId, leaseAnna, new DispatchInput { ProposePrepayments = false }, Ct)));
        var third = (await WithAsync<IStatementDispatch, DispatchOverview?>(s => s.OverviewAsync(thirdId, Ct)))!.Tenancies.Single().Dispatch!;
        Assert.Equal(0m, third.BookedAmount);
        Assert.Contains("abgelaufener Frist", third.Note, StringComparison.Ordinal);
        Assert.Contains("nicht geltend gemacht", await PdfTextAsync(third.FileId), StringComparison.Ordinal);
        await using var check = environment.Services.CreateAsyncScope();
        Assert.Equal(2, await check.ServiceProvider.GetRequiredService<ElocaroDbContext>().Set<Charge>().CountAsync(c => c.Kind == ChargeType.AdditionalPayment, Ct));
    }

    [Fact]
    public async Task Credit_is_booked_as_refund_and_a_missing_landlord_profile_blocks_the_dispatch()
    {
        await ReceiptAsync(1000m);
        await WithAsync<IFinanceSettings, bool>(async s =>
        {
            await s.SaveAsync(new FinanceSettingsInput { AccountingStart = new DateOnly(2026, 1, 1) }, Ct);
            return true;
        });
        await WithAsync<ITenantAccounts, int>(s => s.GenerateChargesAsync(null, Ct));
        for (var month = 1; month <= 12; month++)
        {
            Validate(await WithAsync<ITenantAccounts, Result>(s => s.RecordPaymentAsync(leaseAnna, new PaymentInput { Date = new DateOnly(2026, month, 5), Amount = 600m }, Ct)));
        }

        var statementId = Validate(await WithAsync<IStatements, Result>(s => s.ReleaseAsync(house, Year2026, true, null, Ct)));
        var tenancy = (await WithAsync<IStatementDispatch, DispatchOverview?>(s => s.OverviewAsync(statementId, Ct)))!.Tenancies.Single();
        Assert.Equal((-200m, -200m), (tenancy.Balance, tenancy.ToBook));

        await using (var scope = environment.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();
            var profile = await db.Set<LandlordProfile>().SingleAsync(Ct);
            db.Remove(profile);
            await db.SaveChangesAsync(Ct);
        }

        var noProfile = await WithAsync<IStatementDispatch, Result>(s => s.DispatchAsync(statementId, leaseAnna, new DispatchInput(), Ct));
        Assert.Contains("Vermieterprofil", noProfile.Errors.Single(), StringComparison.Ordinal);
        Assert.True((await WithAsync<IStatementDispatch, DispatchOverview?>(s => s.OverviewAsync(statementId, Ct)))!.LandlordProfileMissing);

        await using (var scope = environment.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();
            db.Add(new LandlordProfile("Max Mustermann", "Musterweg 1", "12345", "Musterstadt") { Iban = "DE02 1203 0000 0000 2020 51" });
            await db.SaveChangesAsync(Ct);
        }

        Validate(await WithAsync<IStatementDispatch, Result>(s => s.DispatchAsync(statementId, leaseAnna, new DispatchInput { ProposePrepayments = false }, Ct)));
        await using var check = environment.Services.CreateAsyncScope();
        var refund = await check.ServiceProvider.GetRequiredService<ElocaroDbContext>().Set<Charge>().SingleAsync(c => c.Kind == ChargeType.Refund, Ct);
        Assert.Equal(-200m, refund.Amount);
        var dispatch = await check.ServiceProvider.GetRequiredService<ElocaroDbContext>().Set<StatementDispatch>().SingleAsync(Ct);
        Assert.Contains("Guthaben von 200,00 €", await PdfTextAsync(dispatch.FileId), StringComparison.Ordinal);
        Assert.DoesNotContain("Girocode", await PdfTextAsync(dispatch.FileId), StringComparison.Ordinal); // a refund needs no transfer from the tenant
    }

    [Fact]
    public async Task Email_dispatch_needs_an_address()
    {
        await ReceiptAsync(1200m);
        var properties = await WithAsync<IPropertyService, Guid>(async s => Validate(await s.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 2" }, Ct)));
        await WithAsync<IPropertyService, Result>(s => s.SetUnitAreaAsync(properties, new AreaInput { From = new DateOnly(2020, 1, 1), Area = 40m }, Ct));
        var ben = await WithAsync<IPersonService, Guid>(async s => Validate(await s.CreateAsync(new PersonInput { FirstName = "Ben", LastName = "Meier" }, null, Ct)));
        var leaseBen = Validate(await WithAsync<ILeaseService, Result>(s => s.CreateAsync(new LeaseCreation { RentalUnitId = properties, Start = new DateOnly(2025, 1, 1), TenantIds = [ben], BaseRent = 400m, OperatingCostPrepayment = 50m }, Ct)));
        var statementId = await ReleaseAsync();

        var byMail = await WithAsync<IStatementDispatch, Result>(s => s.DispatchAsync(statementId, leaseBen, new DispatchInput(), Ct));
        Assert.Contains("keine E-Mail-Adresse", byMail.Errors.Single(), StringComparison.Ordinal);
        Validate(await WithAsync<IStatementDispatch, Result>(s => s.DispatchAsync(statementId, leaseBen, new DispatchInput { Channel = DispatchChannel.Post }, Ct)));

        var overview = (await WithAsync<IStatementDispatch, DispatchOverview?>(s => s.OverviewAsync(statementId, Ct)))!;
        Assert.Empty(overview.Tenancies.Single(t => t.LeaseId == leaseBen).Emails);
        Assert.Null(overview.Tenancies.Single(t => t.LeaseId == leaseAnna).Dispatch);
    }

    [Fact]
    public async Task Heating_annex_with_meter_readings_and_section_35a_certificate_are_part_of_the_letter()
    {
        await ReceiptAsync(1200m);
        // Household-related service: 357.00 € of which labor 357.00 € → whole amount for the only tenant.
        Validate(await WithAsync<IReceiptService, Result>(s => s.CreateAsync(
            new ReceiptInput { SupplierName = "Gartenbau", InvoiceDate = new DateOnly(2026, 12, 1), Gross = 357m },
            [],
            new CostItemInput { CostType = "betrkv-10", PropertyId = house, From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 12, 31), Section35a = Section35a.HouseholdService, LaborCosts = 357m },
            Ct)));
        var receipt = Validate(await WithAsync<IReceiptService, Result>(s => s.CreateAsync(
            new ReceiptInput { SupplierName = "Stadtwerke", InvoiceNumber = "G-2026", InvoiceDate = new DateOnly(2026, 12, 15), Gross = 1350m, FuelQuantity = 12000m, FuelUnit = "kWh", Co2Kg = 2400m },
            [],
            new CostItemInput { CostType = "betrkv-06", PropertyId = house, Amount = 1200m, HeatingKind = HeatingCostKind.Fuel, From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 12, 31) },
            Ct)));
        Validate(await WithAsync<IReceiptService, Result>(s => s.AddItemAsync(
            receipt,
            new CostItemInput { CostType = "betrkv-06", PropertyId = house, Amount = 150m, HeatingKind = HeatingCostKind.CarbonPrice, From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 12, 31) },
            Ct)));
        var circuit = await MeterAsync(null, MeterType.Heat, "WW-WMZ", 2400m);
        var heat = await MeterAsync(apartment, MeterType.Heat, "WMZ-1", 0m);
        var water = await MeterAsync(apartment, MeterType.HotWater, "WW-1", 0m);
        await ReadingAsync(circuit, 4800m);
        await ReadingAsync(heat, 4000m);
        await ReadingAsync(water, 30m);
        Validate(await WithAsync<IHeatingSettings, Result>(s => s.SaveAsync(house, new HeatingSettingsInput { ConsumptionPercent = 60m, JointPlant = true, HotWaterHeatMeterId = circuit, ApplyCarbonCosts = true }, Ct)));
        var statementId = await ReleaseAsync();

        Validate(await WithAsync<IStatementDispatch, Result>(s => s.DispatchAsync(statementId, leaseAnna, new DispatchInput { ProposePrepayments = false }, Ct)));

        var dispatch = (await WithAsync<IStatementDispatch, DispatchOverview?>(s => s.OverviewAsync(statementId, Ct)))!.Tenancies.Single().Dispatch!;
        var text = await PdfTextAsync(dispatch.FileId);
        Assert.Contains("Betriebskosten und Heizkosten", text, StringComparison.Ordinal);
        Assert.Contains("Heiz- und Warmwasserkosten", text, StringComparison.Ordinal);
        Assert.Contains("Anlage: Heizkostenabrechnung 2026", text, StringComparison.Ordinal);
        Assert.Contains("Zählerstände Ihrer Wohnung", text, StringComparison.Ordinal);
        Assert.Contains("WMZ-1", text, StringComparison.Ordinal);
        Assert.Contains("4.000,000 am 31.12.2026", text, StringComparison.Ordinal);
        Assert.Contains("CO₂", text, StringComparison.Ordinal);
        Assert.Contains("Bescheinigung nach § 35a EStG für 2026", text, StringComparison.Ordinal);
        Assert.Contains("Haushaltsnahe Dienstleistungen", text, StringComparison.Ordinal);
        Assert.Contains("357,00 €", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Amounts_book_the_difference_to_earlier_dispatches()
    {
        // Pure rule: balance minus earlier booked; claims after the deadline are dropped, credits are not.
        Assert.Equal((600m, false), StatementDispatchService.Amounts(1800m, 1200m, false));
        Assert.Equal((0m, true), StatementDispatchService.Amounts(1800m, 1200m, true));
        Assert.Equal((-50m, false), StatementDispatchService.Amounts(1150m, 1200m, true));
        Assert.Equal((0m, false), StatementDispatchService.Amounts(0m, 0m, true));
    }

    // ---- helpers ---------------------------------------------------------------------------------------------------

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }

    private async Task<Guid> ReleaseAsync() =>
        Validate(await WithAsync<IStatements, Result>(s => s.ReleaseAsync(house, Year2026, true, null, Ct)));

    private async Task<TResult> WithAsync<TService, TResult>(Func<TService, Task<TResult>> action)
        where TService : notnull
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private async Task<Lease> LeaseAsync()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ElocaroDbContext>().Set<Lease>().AsNoTracking().Include(v => v.Components).SingleAsync(v => v.Id == leaseAnna, Ct);
    }

    private async Task<Guid> MeterAsync(Guid? assignment, MeterType type, string number, decimal initial) =>
        Validate(await WithAsync<IMeterService, Result>(s => s.CreateAsync(
            house,
            new MeterCreation { Data = new MeterInput { Type = type, Name = type.ToString(), Number = number, AssignmentId = assignment }, InstalledOn = new DateOnly(2025, 12, 31), InitialReading = initial },
            Ct)));

    private async Task ReadingAsync(Guid meter, decimal value)
    {
        var result = await WithAsync<IMeterService, ReadingResult>(s => s.RecordReadingAsync(meter, new ReadingInput { Date = new DateOnly(2026, 12, 31), Value = value, Confirmed = true }, null, Ct));
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
    }

    private async Task ReceiptAsync(decimal amount) =>
        Validate(await WithAsync<IReceiptService, Result>(s => s.CreateAsync(
            new ReceiptInput { SupplierName = "Stadt Köln", InvoiceDate = new DateOnly(2026, 12, 15), Gross = amount },
            [],
            new CostItemInput { CostType = "betrkv-01", PropertyId = house, From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 12, 31) },
            Ct)));

    private async Task<string> PdfTextAsync(Guid fileId)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        await using var opened = (await scope.ServiceProvider.GetRequiredService<IFileStore>().OpenAsync(fileId, Ct))!;
        using var buffer = new MemoryStream();
        await opened.Content.CopyToAsync(buffer, Ct);
        using var document = UglyToad.PdfPig.PdfDocument.Open(buffer.ToArray());
        // Chromium sets ligatures (ﬁ, ﬂ, ﬀ) as single glyphs.
        return string.Join(' ', document.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text)).Replace("ﬁ", "fi", StringComparison.Ordinal).Replace("ﬂ", "fl", StringComparison.Ordinal).Replace("ﬀ", "ff", StringComparison.Ordinal);
    }
}
