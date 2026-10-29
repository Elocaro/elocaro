using Elocaro.Application.Common;
using Elocaro.Application.Finance;
using Elocaro.Application.Leases;
using Elocaro.Application.Metering;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Application.Receipts;
using Elocaro.Domain.Deposits;
using Elocaro.Domain.Finance;
using Elocaro.Domain.Leases;
using Elocaro.Domain.Metering;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Corrections;

/// <summary>Typing errors are corrected in place: lease term, rent step, payment and charge, deposit entry, receipt item and reading.</summary>
public class CorrectionServiceTests(PostgresFixture postgres) : IAsyncLifetime
{
    private InfrastructureEnvironment environment = null!;
    private Guid house;
    private Guid apartment;
    private Guid lease;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres);
        environment.Clock.SetUtcNow(new DateTimeOffset(2027, 1, 20, 10, 0, 0, TimeSpan.Zero));
        house = Validate(await WithAsync<IPropertyService, Result>(s => s.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct)));
        apartment = Validate(await WithAsync<IPropertyService, Result>(s => s.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 1" }, Ct)));
        var anna = Validate(await WithAsync<IPersonService, Result>(s => s.CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt" }, null, Ct)));
        lease = Validate(await WithAsync<ILeaseService, Result>(s => s.CreateAsync(new LeaseCreation { RentalUnitId = apartment, Start = new DateOnly(2026, 2, 1), TenantIds = [anna], BaseRent = 500m, OperatingCostPrepayment = 100m }, Ct)));
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Lease_start_and_end_can_be_corrected_and_the_components_move_along()
    {
        Validate(await WithAsync<ILeaseService, Result>(s => s.CorrectTermAsync(lease, new LeaseTermInput { Start = new DateOnly(2026, 1, 1), FixedTermUntil = new DateOnly(2027, 12, 31) }, Ct)));

        var details = (await WithAsync<ILeaseService, LeaseDetails?>(s => s.GetAsync(lease, Ct)))!;

        Assert.Equal(new DateOnly(2026, 1, 1), details.Term.From);
        Assert.Equal(new DateOnly(2027, 12, 31), details.FixedTermUntil);
        Assert.Equal(new DateOnly(2026, 1, 1), details.CurrentComponents.Min(c => c.Validity.From));

        var refused = await WithAsync<ILeaseService, Result>(s => s.CorrectTermAsync(lease, new LeaseTermInput { Start = new DateOnly(2028, 1, 1), FixedTermUntil = new DateOnly(2027, 12, 31) }, Ct));
        Assert.False(refused.Succeeded);
    }

    [Fact]
    public async Task Payment_and_charge_of_the_tenant_account_can_be_corrected_but_not_the_automatic_rent()
    {
        var payment = Validate(await WithAsync<ITenantAccounts, Result>(s => s.RecordPaymentAsync(lease, new PaymentInput { Date = new DateOnly(2026, 3, 1), Amount = 600m }, Ct)));
        var charge = Validate(await WithAsync<ITenantAccounts, Result>(s => s.CreateChargeAsync(lease, new ChargeInput { Kind = ChargeType.AdditionalPayment, Due = new DateOnly(2026, 4, 1), Amount = 100m, Name = "Nachzahlung" }, Ct)));

        Validate(await WithAsync<ITenantAccounts, Result>(s => s.UpdatePaymentAsync(payment, new PaymentInput { Date = new DateOnly(2026, 3, 2), Amount = 650m, Purpose = "März" }, Ct)));
        Validate(await WithAsync<ITenantAccounts, Result>(s => s.UpdateChargeAsync(charge, new ChargeInput { Kind = ChargeType.AdditionalPayment, Due = new DateOnly(2026, 4, 5), Amount = 120m, Name = "Nachzahlung 2025" }, Ct)));

        var account = (await WithAsync<ITenantAccounts, TenantAccount?>(s => s.GetAsync(lease, Ct)))!;
        var paymentEntry = account.Entries.Single(e => e.Id == payment);
        var chargeEntry = account.Entries.Single(e => e.Id == charge);
        Assert.Equal((new DateOnly(2026, 3, 2), 650m, "März"), (paymentEntry.Date, paymentEntry.Credit, paymentEntry.Note));
        Assert.Equal((new DateOnly(2026, 4, 5), 120m, "Nachzahlung 2025"), (chargeEntry.Date, chargeEntry.Debit, chargeEntry.Text));

        await WithAsync<IFinanceSettings, bool>(async s =>
        {
            await s.SaveAsync(new FinanceSettingsInput { AccountingStart = new DateOnly(2026, 2, 1) }, Ct);
            return true;
        });
        await WithAsync<ITenantAccounts, int>(s => s.GenerateChargesAsync(null, Ct));
        var rent = (await WithAsync<ITenantAccounts, TenantAccount?>(s => s.GetAsync(lease, Ct)))!.Entries.First(e => e.Automatic);
        var automatic = await WithAsync<ITenantAccounts, Result>(s => s.UpdateChargeAsync(rent.Id, new ChargeInput { Kind = ChargeType.AdditionalPayment, Due = rent.Date, Amount = 1m }, Ct));
        Assert.False(automatic.Succeeded);
    }

    [Fact]
    public async Task Deposit_entry_can_be_corrected_but_not_below_what_was_refunded()
    {
        var received = Validate(await WithAsync<IDeposits, Result>(s => s.AddAsync(lease, new DepositEntryInput { Kind = DepositEntryKind.Received, Date = new DateOnly(2026, 2, 1), Amount = 500m }, Ct)));
        Validate(await WithAsync<IDeposits, Result>(s => s.AddAsync(lease, new DepositEntryInput { Kind = DepositEntryKind.Refund, Date = new DateOnly(2027, 1, 5), Amount = 400m }, Ct)));

        Validate(await WithAsync<IDeposits, Result>(s => s.UpdateAsync(received, new DepositEntryInput { Kind = DepositEntryKind.Received, Date = new DateOnly(2026, 2, 3), Amount = 600m, Note = "Rate 1" }, Ct)));
        var tooLow = await WithAsync<IDeposits, Result>(s => s.UpdateAsync(received, new DepositEntryInput { Kind = DepositEntryKind.Received, Date = new DateOnly(2026, 2, 3), Amount = 300m }, Ct));

        var view = (await WithAsync<IDeposits, DepositView?>(s => s.GetAsync(lease, Ct)))!;
        Assert.False(tooLow.Succeeded);
        Assert.Equal(600m, view.Entries.Single(e => e.Kind == DepositEntryKind.Received).Amount);
        Assert.Equal(200m, view.Account.Balance);
    }

    [Fact]
    public async Task Cost_item_of_a_receipt_and_a_reading_can_be_corrected()
    {
        var receipt = Validate(await WithAsync<IReceiptService, Result>(s => s.CreateAsync(
            new ReceiptInput { SupplierName = "Stadt", InvoiceDate = new DateOnly(2026, 12, 1), Gross = 1000m },
            [],
            new CostItemInput { CostType = "betrkv-01", PropertyId = house, Amount = 600m, From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 12, 31) },
            Ct)));
        var item = (await WithAsync<IReceiptService, ReceiptDetails?>(s => s.GetAsync(receipt, Ct)))!.Items.Single();

        Validate(await WithAsync<IReceiptService, Result>(s => s.UpdateCostItemAsync(
            receipt,
            item.Id,
            new CostItemInput { CostType = "betrkv-13", PropertyId = house, Amount = 900m, From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 6, 30), Note = "korrigiert" },
            Ct)));

        var changed = (await WithAsync<IReceiptService, ReceiptDetails?>(s => s.GetAsync(receipt, Ct)))!.Items.Single();
        Assert.Equal((item.Id, "betrkv-13", 900m, new DateOnly(2026, 6, 30)), (changed.Id, changed.CostType, changed.Amount, changed.BillingPeriod.To!.Value));
        Assert.Equal("korrigiert", changed.Note);
        var tooMuch = await WithAsync<IReceiptService, Result>(s => s.UpdateCostItemAsync(receipt, item.Id, new CostItemInput { CostType = "betrkv-13", PropertyId = house, Amount = 1200m }, Ct));
        Assert.False(tooMuch.Succeeded);

        var meter = Validate(await WithAsync<IMeterService, Result>(s => s.CreateAsync(
            house,
            new MeterCreation { Data = new MeterInput { Type = MeterType.ColdWater, Name = "Kaltwasser", Number = "K-1", AssignmentId = apartment }, InstalledOn = new DateOnly(2026, 1, 1), InitialReading = 10m },
            Ct)));
        var reading = await WithAsync<IMeterService, ReadingResult>(s => s.RecordReadingAsync(meter, new ReadingInput { Date = new DateOnly(2026, 12, 31), Value = 550m, Confirmed = true }, null, Ct));
        Assert.True(reading.Succeeded);
        var entry = (await WithAsync<IMeterService, MeterDetails?>(s => s.GetAsync(meter, Ct)))!.Readings.Single(r => r.Kind == ReadingType.Reading);

        var corrected = await WithAsync<IMeterService, ReadingResult>(s => s.UpdateReadingAsync(meter, entry.Id, new ReadingInput { Date = new DateOnly(2026, 12, 31), Value = 55m, Confirmed = true }, Ct));

        Assert.True(corrected.Succeeded, string.Join(", ", corrected.Errors));
        Assert.Equal(55m, (await WithAsync<IMeterService, MeterDetails?>(s => s.GetAsync(meter, Ct)))!.Readings.Single(r => r.Id == entry.Id).Value);
    }

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }

    private async Task<TResult> WithAsync<TService, TResult>(Func<TService, Task<TResult>> action)
        where TService : notnull
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
