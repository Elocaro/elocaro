using System.Text;
using System.Xml.Linq;
using Elocaro.Application.Admin;
using Elocaro.Application.Common;
using Elocaro.Application.Finance;
using Elocaro.Application.Leases;
using Elocaro.Application.Payments;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Domain.Payments;
using Elocaro.Infrastructure.Audit;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Payments;

public class DirectDebitTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string TenantIban = "DE02 1203 0000 0000 2020 51";
    private static readonly DateOnly Today = new(2026, 12, 1);
    private static readonly XNamespace Ns = "urn:iso:std:iso:20022:tech:xsd:pain.008.001.08";

    private InfrastructureEnvironment environment = null!;
    private Guid lease;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres);
        environment.Clock.SetUtcNow(new DateTimeOffset(2026, 12, 1, 8, 0, 0, TimeSpan.Zero));
        await With<ILandlordProfileService, bool>(async s =>
        {
            await s.SaveAsync(new LandlordProfileInput { Name = "Hans Müller", Street = "Musterweg 1", PostalCode = "50667", City = "Köln", Iban = "DE89 3704 0044 0532 0130 00", CreditorId = "DE98ZZZ09999999999" }, Ct);
            return true;
        });
        var property = Validate(await With<IPropertyService, Result>(s => s.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct)));
        var unit = Validate(await With<IPropertyService, Result>(s => s.CreateUnitAsync(property, new UnitInput { Name = "Wohnung 1" }, Ct)));
        var tenant = Validate(await With<IPersonService, Result>(s => s.CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schütz" }, null, Ct)));
        lease = Validate(await With<ILeaseService, Result>(s => s.CreateAsync(
            new LeaseCreation { RentalUnitId = unit, Start = new DateOnly(2026, 9, 1), TenantIds = [tenant], BaseRent = 500m, OperatingCostPrepayment = 100m, HeatingCostPrepayment = 50m, People = 1 }, Ct)));
        var details = (await With<ILeaseService, LeaseDetails?>(s => s.GetAsync(lease, Ct)))!;
        details.Data.PaymentMethod = Domain.Leases.PaymentMethod.DirectDebit;
        Validate(await With<ILeaseService, Result>(s => s.UpdateAsync(lease, details.Data, Ct)));
        await With<IFinanceSettings, bool>(async s =>
        {
            await s.SaveAsync(new FinanceSettingsInput { AccountingStart = new DateOnly(2026, 9, 1) }, Ct);
            return true;
        });
        await With<ITenantAccounts, int>(s => s.GenerateChargesAsync(null, Ct));
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    private static MandateInput Mandate(string? reference = null) =>
        new() { Reference = reference, DebtorName = "Anna Schütz", DebtorIban = TenantIban, SignedOn = new DateOnly(2026, 8, 20), Sequence = MandateSequence.Recurring };

    [Fact]
    public async Task A_mandate_gets_a_generated_unique_reference_and_can_be_changed_revoked_and_revived()
    {
        var id = Validate(await With<IDirectDebitMandates, Result>(s => s.CreateAsync(lease, Mandate(), Ct)));

        var row = Assert.Single(await With<IDirectDebitMandates, IReadOnlyList<MandateRow>>(s => s.ListAsync(Ct)));
        Assert.StartsWith("MND-20260820-", row.Reference, StringComparison.Ordinal);
        Assert.Equal(("…2051", true, "Anna Schütz"), (row.IbanEnding, row.Active, row.DebtorName));
        Assert.False((await With<IDirectDebitMandates, Result>(s => s.CreateAsync(lease, Mandate(row.Reference), Ct))).Succeeded);

        var input = (await With<IDirectDebitMandates, MandateInput?>(s => s.GetAsync(id, Ct)))!;
        input.DebtorName = "Anna und Max Schütz";
        Validate(await With<IDirectDebitMandates, Result>(s => s.UpdateAsync(id, input, Ct)));
        Validate(await With<IDirectDebitMandates, Result>(s => s.RevokeAsync(id, Today, Ct)));
        Assert.False(Assert.Single(await With<IDirectDebitMandates, IReadOnlyList<MandateRow>>(s => s.ListAsync(Ct))).Active);
        Validate(await With<IDirectDebitMandates, Result>(s => s.ReactivateAsync(id, Ct)));
        Assert.True(Assert.Single(await With<IDirectDebitMandates, IReadOnlyList<MandateRow>>(s => s.ListAsync(Ct))).Active);
        Assert.False((await With<IDirectDebitMandates, Result>(s => s.CreateAsync(lease, new MandateInput { DebtorName = "A", DebtorIban = "DE00", SignedOn = Today }, Ct))).Succeeded);
    }

    [Fact]
    public async Task Rents_without_a_valid_mandate_are_listed_with_the_reason_and_not_collectable()
    {
        var candidates = await With<IDirectDebits, IReadOnlyList<DebitCandidate>>(s => s.CandidatesAsync(Ct));

        Assert.NotEmpty(candidates);
        Assert.All(candidates, c => Assert.Contains("Mandat", c.Problem, StringComparison.Ordinal));
        var refused = await With<IDirectDebits, PaymentOrderResult>(s => s.CreateFileAsync([candidates[0].Key], Today.AddDays(20), Ct));
        Assert.False(refused.Succeeded);
    }

    [Fact]
    public async Task With_a_mandate_the_open_rent_is_collected_and_the_file_carries_mandate_and_creditor_data()
    {
        Validate(await With<IDirectDebitMandates, Result>(s => s.CreateAsync(lease, Mandate("MND-TEST-1"), Ct)));
        var candidates = await With<IDirectDebits, IReadOnlyList<DebitCandidate>>(s => s.CandidatesAsync(Ct));
        var next = candidates.Single(c => c.Due.Month == 12);
        Assert.Null(next.Problem);
        Assert.Equal((650m, "MND-TEST-1"), (next.Amount, next.MandateReference));

        var result = await With<IDirectDebits, PaymentOrderResult>(s => s.CreateFileAsync([next.Key, next.Key], next.Due, Ct));

        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        Assert.Equal(($"lastschriften-{next.Due:yyyy-MM-dd}.xml", 1, 650m), (result.FileName, result.Count, result.Total));
        var xml = XDocument.Parse(Encoding.UTF8.GetString(result.Content!));
        Assert.Equal("DE98ZZZ09999999999", xml.Descendants(Ns + "CdtrSchmeId").Single().Descendants(Ns + "Id").Last().Value);
        Assert.Equal("MND-TEST-1", xml.Descendants(Ns + "MndtId").Single().Value);
        Assert.Equal("DE02120300000000202051", xml.Descendants(Ns + "DbtrAcct").Single().Descendants(Ns + "IBAN").Single().Value);
        Assert.Equal("650.00", xml.Descendants(Ns + "InstdAmt").Single().Value);
        Assert.Equal("RCUR", xml.Descendants(Ns + "SeqTp").Single().Value);
        var logged = await With<ElocaroDbContext, List<string>>(db => db.Set<AuditEntry>().Where(a => a.Purpose == "IBAN für Lastschriftdatei").Select(a => a.EntityType).ToListAsync(Ct));
        Assert.Equal(["DirectDebitMandate"], logged);
        var row = Assert.Single(await With<IDirectDebitMandates, IReadOnlyList<MandateRow>>(s => s.ListAsync(Ct)));
        Assert.Equal(next.Due.AddMonths(36), row.ExpiresOn);
    }

    [Fact]
    public async Task A_paid_rent_is_not_collected_again_and_a_wrong_collection_date_or_choice_is_refused()
    {
        Validate(await With<IDirectDebitMandates, Result>(s => s.CreateAsync(lease, Mandate(), Ct)));
        var next = (await With<IDirectDebits, IReadOnlyList<DebitCandidate>>(s => s.CandidatesAsync(Ct))).Single(c => c.Due.Month == 12);

        Assert.False((await With<IDirectDebits, PaymentOrderResult>(s => s.CreateFileAsync([next.Key], Today, Ct))).Succeeded);
        Assert.False((await With<IDirectDebits, PaymentOrderResult>(s => s.CreateFileAsync([$"debit:{lease}:2030-01-01"], next.Due, Ct))).Succeeded);

        Validate(await With<ITenantAccounts, Result>(s => s.RecordPaymentAsync(lease, new PaymentInput { Date = Today, Amount = 2600m, ChargeId = null }, Ct)));
        var after = await With<IDirectDebits, IReadOnlyList<DebitCandidate>>(s => s.CandidatesAsync(Ct));
        Assert.Empty(after);
    }

    [Fact]
    public async Task Without_the_creditor_id_in_the_profile_no_file_is_made()
    {
        Validate(await With<IDirectDebitMandates, Result>(s => s.CreateAsync(lease, Mandate(), Ct)));
        var next = (await With<IDirectDebits, IReadOnlyList<DebitCandidate>>(s => s.CandidatesAsync(Ct))).Single(c => c.Due.Month == 12);
        await With<ILandlordProfileService, bool>(async s =>
        {
            await s.SaveAsync(new LandlordProfileInput { Name = "Hans Müller", Street = "Musterweg 1", PostalCode = "50667", City = "Köln", Iban = "DE89 3704 0044 0532 0130 00" }, Ct);
            return true;
        });

        var result = await With<IDirectDebits, PaymentOrderResult>(s => s.CreateFileAsync([next.Key], next.Due, Ct));

        Assert.False(result.Succeeded);
        Assert.Contains("Gläubiger", result.Errors.Single(), StringComparison.Ordinal);
    }

    private async Task<T> With<TService, T>(Func<TService, Task<T>> action)
        where TService : notnull
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }
}
