using libfintx.FinTS.Camt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Bank;
using MietHahn.Application.Common;
using MietHahn.Domain.Bank;
using MietHahn.Infrastructure.Bank.FinTs;
using MietHahn.Infrastructure.Bank.Import;
using MietHahn.Infrastructure.Persistence;
using MietHahn.Integration.Tests.Infrastructure;

namespace MietHahn.Integration.Tests.Bank;

/// <summary>Mapping and payload of the FinTS retrieval (no bank connection needed).</summary>
public class FinTsMappingTests
{
    [Fact]
    public void Camt_transaction_becomes_parsed_transaction()
    {
        var read = FinTsMapping.FromCamt(new CamtTransaction
        {
            InputDate = new DateTime(2026, 3, 2),
            ValueDate = new DateTime(2026, 3, 3),
            Amount = 770m,
            Text = "Gutschrift",
            Description = "  Miete  März\n2026 ",
            PartnerName = "Anna Schmidt",
            AccountCode = "DE93370501981234567890",
            BankCode = "COLSDE33XXX",
            EndToEndId = "MIETE-2026-03",
            MandateId = "NOTPROVIDED",
        })!;

        Assert.Equal(new ParsedTransaction(new DateOnly(2026, 3, 2), 770m, "Miete März 2026", new DateOnly(2026, 3, 3), "EUR",
            "Anna Schmidt", "DE93370501981234567890", "COLSDE33XXX", "Gutschrift", "MIETE-2026-03"), read);
    }

    [Fact]
    public void Pending_items_and_invalid_counterparty_data_are_dropped()
    {
        var statement = new CamtStatement { Currency = "EUR" };
        statement.Transactions.Add(new CamtTransaction { Pending = true, Amount = 5m, InputDate = new DateTime(2026, 3, 2) });
        statement.Transactions.Add(new CamtTransaction { Amount = -84.50m, InputDate = new DateTime(2026, 3, 4), AccountCode = "keine IBAN", BankCode = "xx", Description = "Strom" });

        var read = Assert.Single(FinTsMapping.FromCamt([statement]));

        Assert.Equal((-84.50m, null, null, null), (read.Amount, read.CounterpartyIban, read.CounterpartyBic, read.ValueDate));
    }

    [Fact]
    public void Payload_is_recognized_and_read_like_a_file()
    {
        var transactions = new[] { new ParsedTransaction(new DateOnly(2026, 3, 2), 770m, "Miete", CounterpartyName: "Anna Schmidt") };

        var content = FinTsPayload.Write("DE89370400440532013000", transactions);
        var file = BankFileReader.Read(content, CsvDefaults.All);

        Assert.True(FinTsPayload.Detects(content));
        Assert.Equal(BankFormat.FinTs, file.Format);
        Assert.Equal("DE89370400440532013000", file.MoveOut.Iban);
        Assert.Equal(transactions, file.MoveOut.Transactions);
        Assert.False(FinTsPayload.Detects("{\"a\":1}"u8.ToArray()));
    }

    [Theory]
    [InlineData("DE89370400440532013000", "532013000")]
    [InlineData("DE02120300000000202051", "202051")]
    [InlineData("AT611904300234573201", "")]
    public void Account_number_comes_from_german_IBANs_only(string iban, string expected) =>
        Assert.Equal(expected, FinTsService.GermanAccountNumber(iban));
}

public class FinTsServiceTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Iban = "DE89370400440532013000";
    private InfrastructureEnvironment environment = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() =>
        environment = await InfrastructureEnvironment.CreateAsync(postgres, new Dictionary<string, string?> { ["FinTs:ProductId"] = "TEST1234567890" });

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task FinTs_access_data_is_stored_with_encrypted_user_id()
    {
        var id = await CreateAccountAsync(new BankAccountInput { Name = "Mietkonto", Iban = Iban, FinTsUrl = "https://banking.example.de/fints30", FinTsBankCode = "37040044", FinTsUserId = "meinLogin" });

        var account = (await WithAsync<IBankService, BankAccountInput?>(b => b.GetAccountAsync(id, Ct)))!;
        Assert.Equal(("https://banking.example.de/fints30", "37040044", "meinLogin"), (account.FinTsUrl, account.FinTsBankCode, account.FinTsUserId));
        Assert.True(Assert.Single(await WithAsync<IBankService, IReadOnlyList<BankAccountRow>>(b => b.AccountsAsync(Ct))).HasFinTs);

        await using var scope = environment.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<MietHahnDbContext>().Database
            .SqlQueryRaw<string>("select fin_ts_user_id as \"Value\" from bank_accounts").SingleAsync(Ct);
        Assert.DoesNotContain("meinLogin", stored, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://banking.example.de/fints30", "37040044", null, "alle drei")]
    [InlineData("http://banking.example.de/fints30", "37040044", "x", "https://")]
    [InlineData("https://banking.example.de/fints30", "3704", "x", "8 Ziffern")]
    public async Task Incomplete_or_insecure_FinTS_data_is_rejected(string url, string bankCode, string? user, string message)
    {
        var result = await WithAsync<IBankService, Result>(b => b.CreateAccountAsync(new BankAccountInput { Name = "Mietkonto", Iban = Iban, FinTsUrl = url, FinTsBankCode = bankCode, FinTsUserId = user }, Ct));

        Assert.False(result.Succeeded);
        Assert.Contains(message, result.Errors.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Retrieval_needs_FinTS_access_data_and_a_PIN()
    {
        var plain = await CreateAccountAsync(new BankAccountInput { Name = "Mietkonto", Iban = Iban });

        var withoutAccess = await WithAsync<IFinTsService, Result>(f => f.StartAsync(plain, "12345", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1), Ct));
        var withoutPin = await WithAsync<IFinTsService, Result>(f => f.StartAsync(plain, "", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1), Ct));

        Assert.Contains("nicht eingerichtet", withoutAccess.Errors.Single(), StringComparison.Ordinal);
        Assert.Contains("PIN", withoutPin.Errors.Single(), StringComparison.Ordinal);
        Assert.Null(await WithAsync<IFinTsService, FinTsFetchState?>(f => f.StateAsync(Guid.NewGuid(), Ct)));
    }

    [Fact]
    public async Task Delivered_transactions_land_in_the_preview_and_can_be_imported_once()
    {
        var account = await CreateAccountAsync(new BankAccountInput { Name = "Mietkonto", Iban = Iban });
        var sessions = environment.Services.GetRequiredService<FinTsSessions>();
        var session = new FinTsSession(account, Iban, environment.Clock.GetUtcNow());
        var id = sessions.Add(session, environment.Clock.GetUtcNow());
        session.Succeed([new ParsedTransaction(new DateOnly(2026, 3, 2), 770m, "Miete März", CounterpartyName: "Anna Schmidt")]);

        var state = (await WithAsync<IFinTsService, FinTsFetchState?>(f => f.StateAsync(id, Ct)))!;
        var again = (await WithAsync<IFinTsService, FinTsFetchState?>(f => f.StateAsync(id, Ct)))!;

        Assert.Equal(FinTsPhase.Completed, state.Phase);
        Assert.Equal(state.ImportId, again.ImportId); // polling again creates no second preview
        var preview = (await WithAsync<IBankService, ImportPreview?>(b => b.PreviewAsync(state.ImportId!.Value, Ct)))!;
        Assert.Equal(BankFormat.FinTs, preview.Format);
        Assert.Equal(("Miete März", false), (preview.Rows.Single().Transaction.Purpose, preview.Rows.Single().Duplicate));

        Assert.True((await WithAsync<IBankService, Result>(b => b.ImportAsync(state.ImportId!.Value, Ct))).Succeeded);
        Assert.Single(await WithAsync<IBankService, IReadOnlyList<TransactionRow>>(b => b.TransactionsAsync(new TransactionQuery(account), Ct)));
    }

    private async Task<Guid> CreateAccountAsync(BankAccountInput input)
    {
        var result = await WithAsync<IBankService, Result>(b => b.CreateAccountAsync(input, Ct));
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }

    private async Task<T> WithAsync<TService, T>(Func<TService, Task<T>> action)
        where TService : notnull
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
