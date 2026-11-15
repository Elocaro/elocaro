using System.Text;
using System.Xml.Linq;
using Elocaro.Application.Admin;
using Elocaro.Application.Common;
using Elocaro.Application.Files;
using Elocaro.Application.Finance;
using Elocaro.Application.Leases;
using Elocaro.Application.Payments;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Application.Receipts;
using Elocaro.Domain.Finance;
using Elocaro.Infrastructure.Audit;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Payments;

public class PaymentOrderTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string SupplierIban = "DE02120300000000202051";
    private const string TenantIban = "DE89370400440532013000";
    private static readonly DateOnly Today = new(2026, 12, 16);
    private static readonly XNamespace Ns = "urn:iso:std:iso:20022:tech:xsd:pain.001.001.09";

    private InfrastructureEnvironment environment = null!;
    private Guid supplier;
    private Guid tenant;
    private Guid receipt;
    private Guid endedLease;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres);
        environment.Clock.SetUtcNow(new DateTimeOffset(2026, 12, 16, 8, 0, 0, TimeSpan.Zero));
        await With<ILandlordProfileService, bool>(async s =>
        {
            await s.SaveAsync(new LandlordProfileInput { Name = "Hans Müller", Street = "Musterweg 1", PostalCode = "50667", City = "Köln", Iban = "DE89 3704 0044 0532 0130 00", Bic = "COBADEFFXXX" }, Ct);
            return true;
        });

        supplier = Validate(await With<IPersonService, Result>(s => s.CreateAsync(new PersonInput { LastName = "Schmidt Heizung", Company = "Schmidt Heizung GmbH" }, null, Ct)));
        Validate(await With<IPersonService, Result>(s => s.AddBankDetailsAsync(supplier, new BankDetailsInput { Iban = SupplierIban }, Ct)));
        tenant = Validate(await With<IPersonService, Result>(s => s.CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schütz" }, null, Ct)));
        Validate(await With<IPersonService, Result>(s => s.AddBankDetailsAsync(tenant, new BankDetailsInput { Iban = TenantIban, AccountHolder = "Anna Schütz" }, Ct)));

        var property = Validate(await With<IPropertyService, Result>(s => s.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct)));
        var unit = Validate(await With<IPropertyService, Result>(s => s.CreateUnitAsync(property, new UnitInput { Name = "Wohnung 1" }, Ct)));
        endedLease = Validate(await With<ILeaseService, Result>(s => s.CreateAsync(
            new LeaseCreation { RentalUnitId = unit, Start = new DateOnly(2024, 1, 1), FixedTermUntil = new DateOnly(2025, 12, 31), TenantIds = [tenant], BaseRent = 500m, People = 1 }, Ct)));
        receipt = Validate(await With<IReceiptService, Result>(s => s.CreateAsync(new ReceiptInput
        {
            SupplierId = supplier,
            InvoiceNumber = "R-2026-117",
            InvoiceDate = new DateOnly(2026, 11, 20),
            Gross = 595m,
            Net = 500m,
            Vat = 95m,
            DueOn = new DateOnly(2026, 12, 4),
        }, [], null, Ct)));
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Open_receipts_credits_and_deposits_of_ended_leases_are_offered_with_their_amounts()
    {
        Validate(await With<ITenantAccounts, Result>(s => s.CreateChargeAsync(endedLease, new ChargeInput { Kind = ChargeType.Refund, Due = new DateOnly(2026, 6, 1), Amount = 80.40m, Name = "Guthaben Nebenkosten 2025" }, Ct)));
        Validate(await With<IDeposits, Result>(s => s.AddAsync(endedLease, new DepositEntryInput { Kind = Domain.Leases.DepositEntryKind.Received, Date = new DateOnly(2024, 1, 1), Amount = 1500m }, Ct)));

        var candidates = await With<IPaymentOrders, IReadOnlyList<PaymentCandidate>>(s => s.CandidatesAsync(Ct));

        var invoice = Assert.Single(candidates, c => c.Source == PaymentSource.Receipt);
        Assert.Equal(($"receipt:{receipt}", 595m, "Rechnung R-2026-117", null), (invoice.Key, invoice.Amount, invoice.Purpose, invoice.Problem));
        Assert.Contains("Schmidt Heizung", invoice.Payee, StringComparison.Ordinal);
        Assert.Equal(80.40m, Assert.Single(candidates, c => c.Source == PaymentSource.TenantCredit).Amount);
        var deposit = Assert.Single(candidates, c => c.Source == PaymentSource.Deposit);
        Assert.Equal(1500m, deposit.Amount);
        Assert.NotNull(deposit.Notice);
    }

    [Fact]
    public async Task A_paid_receipt_and_a_payee_without_iban_are_handled()
    {
        var noIban = Validate(await With<IPersonService, Result>(s => s.CreateAsync(new PersonInput { LastName = "Ohne Bank" }, null, Ct)));
        var open = Validate(await With<IReceiptService, Result>(s => s.CreateAsync(new ReceiptInput { SupplierId = noIban, InvoiceDate = new DateOnly(2026, 11, 1), Gross = 50m, Net = 42.02m, Vat = 7.98m }, [], null, Ct)));
        var details = (await With<IReceiptService, ReceiptDetails?>(s => s.GetAsync(receipt, Ct)))!;
        details.Data.PaidOn = new DateOnly(2026, 12, 1);
        Validate(await With<IReceiptService, Result>(s => s.UpdateAsync(receipt, details.Data, Ct)));

        var candidates = await With<IPaymentOrders, IReadOnlyList<PaymentCandidate>>(s => s.CandidatesAsync(Ct));

        Assert.DoesNotContain(candidates, c => c.Key == $"receipt:{receipt}");
        var problem = Assert.Single(candidates, c => c.Key == $"receipt:{open}");
        Assert.Contains("IBAN", problem.Problem, StringComparison.Ordinal);
        var refused = await With<IPaymentOrders, PaymentOrderResult>(s => s.CreateFileAsync([problem.Key], Today, Ct));
        Assert.False(refused.Succeeded);
    }

    [Fact]
    public async Task The_file_is_built_from_the_books_and_each_person_with_an_iban_is_logged()
    {
        Validate(await With<ITenantAccounts, Result>(s => s.CreateChargeAsync(endedLease, new ChargeInput { Kind = ChargeType.Refund, Due = new DateOnly(2026, 6, 1), Amount = 80.40m, Name = "Guthaben" }, Ct)));

        var result = await With<IPaymentOrders, PaymentOrderResult>(s => s.CreateFileAsync([$"receipt:{receipt}", $"credit:{endedLease}", $"receipt:{receipt}"], Today.AddDays(2), Ct));

        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        Assert.Equal(("ueberweisungen-2026-12-18.xml", 2, 675.40m), (result.FileName, result.Count, result.Total));
        var xml = XDocument.Parse(Encoding.UTF8.GetString(result.Content!));
        Assert.Equal("Hans Mueller", xml.Descendants(Ns + "Dbtr").Single().Element(Ns + "Nm")!.Value);
        Assert.Equal("COBADEFFXXX", xml.Descendants(Ns + "BICFI").Single().Value);
        var transfers = xml.Descendants(Ns + "CdtTrfTxInf").ToList();
        Assert.Equal(["595.00", "80.40"], transfers.Select(t => t.Descendants(Ns + "InstdAmt").Single().Value));
        Assert.Equal([SupplierIban, TenantIban], transfers.Select(t => t.Element(Ns + "CdtrAcct")!.Descendants(Ns + "IBAN").Single().Value));
        Assert.Equal("Anna Schuetz", transfers[1].Element(Ns + "Cdtr")!.Element(Ns + "Nm")!.Value);

        var logged = await With<ElocaroDbContext, List<Guid>>(db => db.Set<AuditEntry>().Where(a => a.Action == Elocaro.Application.Audit.AuditAction.Read && a.Purpose == "IBAN für Überweisungsdatei").Select(a => a.EntityId).ToListAsync(Ct));
        Assert.Equivalent(new[] { supplier, tenant }, logged);
    }

    [Fact]
    public async Task A_past_date_an_unknown_choice_and_a_missing_account_of_the_landlord_are_refused()
    {
        Assert.False((await With<IPaymentOrders, PaymentOrderResult>(s => s.CreateFileAsync([$"receipt:{receipt}"], Today.AddDays(-1), Ct))).Succeeded);
        Assert.False((await With<IPaymentOrders, PaymentOrderResult>(s => s.CreateFileAsync([$"receipt:{Guid.NewGuid()}"], Today, Ct))).Succeeded);
        Assert.False((await With<IPaymentOrders, PaymentOrderResult>(s => s.CreateFileAsync([], Today, Ct))).Succeeded);

        await With<ILandlordProfileService, bool>(async s =>
        {
            await s.SaveAsync(new LandlordProfileInput { Name = "Hans Müller", Street = "Musterweg 1", PostalCode = "50667", City = "Köln" }, Ct);
            return true;
        });
        var noAccount = await With<IPaymentOrders, PaymentOrderResult>(s => s.CreateFileAsync([$"receipt:{receipt}"], Today, Ct));
        Assert.False(noAccount.Succeeded);
        Assert.Contains("IBAN", noAccount.Errors.Single(), StringComparison.Ordinal);
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
