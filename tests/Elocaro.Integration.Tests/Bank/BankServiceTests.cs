using System.Text;
using Elocaro.Application.Bank;
using Elocaro.Application.Common;
using Elocaro.Application.Properties;
using Elocaro.Domain.Bank;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Bank;

public class BankServiceTests(PostgresFixture postgres) : IAsyncLifetime
{
    private InfrastructureEnvironment environment = null!;
    private Guid house;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres);
        await using var scope = environment.Services.CreateAsyncScope();
        house = Validate(await scope.ServiceProvider.GetRequiredService<IPropertyService>().CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct));
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Account_with_properties_and_valid_unique_IBAN()
    {
        var id = Validate(await WithAsync(b => b.CreateAccountAsync(new BankAccountInput { Name = "Mietkonto", Iban = "de89 3704 0044 0532 0130 00", Bic = "cobadeffxxx", PropertyIds = [house] }, Ct)));

        var account = Assert.Single(await WithAsync(b => b.AccountsAsync(Ct)));
        Assert.Equal(("Mietkonto", "DE89 3704 0044 0532 0130 00", AccountType.RentAccount), (account.Name, account.Iban, account.Type));
        Assert.Equal(["Lindenweg 3"], account.Properties);
        Assert.Equal("COBADEFFXXX", (await WithAsync(b => b.GetAccountAsync(id, Ct)))!.Bic);

        var invalid = await WithAsync(b => b.CreateAccountAsync(new BankAccountInput { Name = "Falsch", Iban = "DE89370400440532013001" }, Ct));
        Assert.Contains("Prüfsumme", invalid.Errors.Single(), StringComparison.Ordinal);
        var duplicate = await WithAsync(b => b.CreateAccountAsync(new BankAccountInput { Name = "Nochmal", Iban = "DE89370400440532013000" }, Ct));
        Assert.False(duplicate.Succeeded);
    }

    [Fact]
    public async Task Importing_the_same_file_twice_creates_no_duplicates()
    {
        var account = await AccountAsync();
        var file = TestFile("camt053_v02.xml");

        var first = Validate(await WithAsync(b => b.CreatePreviewAsync(account, "moveOut.xml", file, null, Ct)));
        var preview = (await WithAsync(b => b.PreviewAsync(first, Ct)))!;
        Assert.Equal((BankFormat.Camt053, 4, 0), (preview.Format, preview.Rows.Count, preview.Rows.Count(z => z.Duplicate)));
        Assert.Null(preview.DifferentIban);
        Validate(await WithAsync(b => b.ImportAsync(first, Ct)));
        Assert.False((await WithAsync(b => b.ImportAsync(first, Ct))).Succeeded);

        var second = Validate(await WithAsync(b => b.CreatePreviewAsync(account, "moveOut.xml", file, null, Ct)));
        Assert.All((await WithAsync(b => b.PreviewAsync(second, Ct)))!.Rows, z => Assert.True(z.Duplicate));
        Validate(await WithAsync(b => b.ImportAsync(second, Ct)));

        Assert.Equal(4, (await WithAsync(b => b.TransactionsAsync(new TransactionQuery(account), Ct))).Count);
        var log = await WithAsync(b => b.ImportsAsync(account, Ct));
        Assert.Equal([(4, 0, 4), (4, 4, 0)], log.Select(p => (p.Read, p.Duplicates, p.Added)));
        Assert.Equal((new DateOnly(2026, 2, 2), new DateOnly(2026, 2, 6)), (log[1].From!.Value, log[1].To!.Value));
        Assert.All(log, p => Assert.Equal(ImportStatus.Imported, p.Status));
    }

    [Fact]
    public async Task File_of_another_account_is_reported_and_not_imported()
    {
        var other = Validate(await WithAsync(b => b.CreateAccountAsync(new BankAccountInput { Name = "Rücklage", Iban = "DE02120300000000202051", Type = AccountType.Reserve }, Ct)));

        var id = Validate(await WithAsync(b => b.CreatePreviewAsync(other, "moveOut.sta", TestFile("mt940.sta"), null, Ct)));

        Assert.Null((await WithAsync(b => b.PreviewAsync(id, Ct)))!.DifferentIban); // MT940 with bank code/account number but no IBAN
        var camt = Validate(await WithAsync(b => b.CreatePreviewAsync(other, "moveOut.xml", TestFile("camt053_v08.xml"), null, Ct)));
        Assert.Equal("DE89 3704 0044 0532 0130 00", (await WithAsync(b => b.PreviewAsync(camt, Ct)))!.DifferentIban);
        var result = await WithAsync(b => b.ImportAsync(camt, Ct));
        Assert.Contains("gehört zum Konto DE89", result.Errors.Single(), StringComparison.Ordinal);

        Validate(await WithAsync(b => b.DiscardAsync(camt, Ct)));
        Assert.Null(await WithAsync(b => b.PreviewAsync(camt, Ct)));
    }

    [Fact]
    public async Task Unreadable_file_is_rejected()
    {
        var account = await AccountAsync();

        var result = await WithAsync(b => b.CreatePreviewAsync(account, "image.png", [0x89, 0x50, 0x4E, 0x47], null, Ct));

        Assert.Contains("nicht erkannt", result.Errors.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_ignore_and_reopen_transactions()
    {
        var account = await AccountAsync();
        var import = Validate(await WithAsync(b => b.CreatePreviewAsync(account, "sparkasse.csv", TestFile("sparkasse.csv"), null, Ct)));
        Assert.Equal("Sparkasse (CSV-CAMT)", (await WithAsync(b => b.PreviewAsync(import, Ct)))!.Profile);
        Validate(await WithAsync(b => b.ImportAsync(import, Ct)));

        var stream = Assert.Single(await WithAsync(b => b.TransactionsAsync(new TransactionQuery(Text: "müllerstadt"), Ct)));
        Assert.Equal(TransactionStatus.Open, stream.Status);
        Assert.Single(await WithAsync(b => b.TransactionsAsync(new TransactionQuery(Text: "DE9337"), Ct)));

        Validate(await WithAsync(b => b.IgnoreAsync(stream.Id, "Hausstrom, wird über Belege erfasst", Ct)));
        Assert.Single(await WithAsync(b => b.TransactionsAsync(new TransactionQuery(account, TransactionStatus.Open), Ct)));
        var ignored = Assert.Single(await WithAsync(b => b.TransactionsAsync(new TransactionQuery(account, TransactionStatus.Ignored), Ct)));
        Assert.Equal("Hausstrom, wird über Belege erfasst", ignored.Note);
        Assert.Equal(1, Assert.Single(await WithAsync(b => b.AccountsAsync(Ct))).OpenTransactions);

        Validate(await WithAsync(b => b.ReopenAsync(stream.Id, Ct)));
        Assert.Equal(2, (await WithAsync(b => b.TransactionsAsync(new TransactionQuery(account, TransactionStatus.Open), Ct))).Count);
    }

    [Fact]
    public async Task Custom_CSV_profile_with_debit_and_credit_columns()
    {
        var account = await AccountAsync();
        Validate(await WithAsync(b => b.SaveCsvProfileAsync(null, new CsvProfileInput
        {
            Name = "Hausbank",
            Delimiter = ",",
            DecimalSeparator = ".",
            DateFormat = "yyyy-MM-dd",
            BookingDateColumn = "Date",
            DebitColumn = "Debit",
            CreditColumn = "Credit",
            NameColumn = "Payee",
            PurposeColumn = "Memo",
        }, Ct)));
        Assert.False((await WithAsync(b => b.SaveCsvProfileAsync(null, new CsvProfileInput { Name = "DKB", BookingDateColumn = "x", AmountColumn = "y" }, Ct))).Succeeded);
        Assert.False((await WithAsync(b => b.SaveCsvProfileAsync(null, new CsvProfileInput { Name = "Ohne Betrag", BookingDateColumn = "x" }, Ct))).Succeeded);

        var csv = Encoding.UTF8.GetBytes("Date,Payee,Memo,Debit,Credit\n2026-02-02,Anna Schmidt,\"Miete Februar, Wohnung 1\",,770.00\n2026-02-05,Stadtwerke,Strom,84.50,\n");
        var import = Validate(await WithAsync(b => b.CreatePreviewAsync(account, "export.csv", csv, null, Ct)));
        var preview = (await WithAsync(b => b.PreviewAsync(import, Ct)))!;

        Assert.Equal("Hausbank", preview.Profile);
        Assert.Equal([(770m, "Miete Februar, Wohnung 1"), (-84.50m, "Strom")], preview.Rows.Select(z => (z.Transaction.Amount, z.Transaction.Purpose)));
        Assert.Contains(await WithAsync(b => b.CsvProfilesAsync(Ct)), p => p is { Name: "Hausbank", IsDefault: false });
        Assert.Contains(await WithAsync(b => b.CsvProfilesAsync(Ct)), p => p is { Name: "Sparkasse (CSV-CAMT)", IsDefault: true });
    }

    private async Task<Guid> AccountAsync() =>
        Validate(await WithAsync(b => b.CreateAccountAsync(new BankAccountInput { Name = "Mietkonto", Iban = "DE89370400440532013000" }, Ct)));

    private static byte[] TestFile(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Bank", "TestFiles", name));

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }

    private async Task<T> WithAsync<T>(Func<IBankService, Task<T>> action)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IBankService>());
    }
}
