using MietHahn.Application.Accounts;
using MietHahn.Web.Tests.Account;

namespace MietHahn.Web.Tests.Bank;

public class BankImportTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    [Fact]
    public async Task Create_account_import_statement_with_preview_and_ignore_transaction()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        var account = await admin.SubmitAsync(await admin.GetAsync("/verwaltung/bank/konten/neu"), "account", new Dictionary<string, string>
        {
            ["Data.Name"] = "Mietkonto",
            ["Data.Iban"] = "DE89 3704 0044 0532 0130 00",
            ["Data.Type"] = "RentAccount",
        });
        Assert.Contains("Das Konto wurde angelegt.", account.Text, StringComparison.Ordinal);
        var accountPath = account.Path;

        var preview = await admin.SubmitWithFileAsync(account, "import", "Upload", TestFile("camt053_v02.xml"), "februar.xml", "application/xml");
        Assert.Contains("Bitte die Vorschau prüfen", preview.Text, StringComparison.Ordinal);
        Assert.Contains("4 neue Umsätze", preview.Text, StringComparison.Ordinal);
        Assert.Contains("Nicht gebuchter Umsatz übersprungen", preview.Text, StringComparison.Ordinal);
        Assert.Contains("Miete Februar 2026 Wohnung 1 Lindenweg 3", preview.Text, StringComparison.Ordinal);

        var transactions = await admin.SubmitAsync(preview, "run-import");
        Assert.Contains("4 Umsätze importiert.", transactions.Text, StringComparison.Ordinal);
        Assert.Contains("Stadtwerke Musterstadt", transactions.Text, StringComparison.Ordinal);
        Assert.Contains("4 Umsätze ohne Zuordnung", (await admin.GetAsync("/verwaltung")).TextWithStreaming, StringComparison.Ordinal);

        // Row button "Ignorieren" with a note; the selection (account) is preserved.
        var button = transactions.Form("status").QuerySelectorAll("button[name=Action]").First(b => b.GetAttribute("aria-label")!.Contains("05.02.2026", StringComparison.Ordinal));
        transactions = await admin.SubmitAsync(transactions, "status", new Dictionary<string, string> { ["Action"] = button.GetAttribute("value")!, ["Note"] = "über Belege" });
        Assert.Contains("Der Umsatz wird ignoriert.", transactions.Text, StringComparison.Ordinal);
        Assert.Contains("Notiz: über Belege", transactions.Text, StringComparison.Ordinal);
        Assert.Contains("konto=", transactions.Url.Query, StringComparison.Ordinal);
        Assert.Equal(3, (await admin.GetAsync(transactions.Url.PathAndQuery + "&status=Open")).Document.QuerySelectorAll("tbody tr").Length);

        // Same file again: nothing new, import blocked; discard.
        preview = await admin.SubmitWithFileAsync(await admin.GetAsync(accountPath), "import", "Upload", TestFile("camt053_v02.xml"), "februar.xml", "application/xml");
        Assert.Contains("0 neue Umsätze, 4 schon vorhanden", preview.Text, StringComparison.Ordinal);
        Assert.True(preview.Form("run-import").QuerySelector("button")!.HasAttribute("disabled"));
        account = await admin.SubmitAsync(preview, "discard");
        Assert.Contains("Der Import wurde verworfen.", account.Text, StringComparison.Ordinal);
        Assert.Contains("februar.xml", account.Text, StringComparison.Ordinal); // import log
    }

    [Fact]
    public async Task Unknown_file_is_reported_and_custom_CSV_profile_created()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var account = await admin.SubmitAsync(await admin.GetAsync("/verwaltung/bank/konten/neu"), "account", new Dictionary<string, string>
        {
            ["Data.Name"] = "Mietkonto",
            ["Data.Iban"] = "DE89370400440532013000",
        });

        var csv = "Date,Payee,Memo,Amount\n2026-02-02,Anna Schmidt,Miete,770.00\n"u8.ToArray();
        var errors = await admin.SubmitWithFileAsync(account, "import", "Upload", csv, "export.csv", "text/csv");
        Assert.Contains("Das Dateiformat wurde nicht erkannt.", errors.Text, StringComparison.Ordinal);

        var profile = await admin.SubmitAsync(await admin.GetAsync("/verwaltung/bank/csv-profile"), "profile", new Dictionary<string, string>
        {
            ["Data.Name"] = "Hausbank",
            ["Data.Delimiter"] = ",",
            ["Data.DecimalSeparator"] = ".",
            ["Data.DateFormat"] = "yyyy-MM-dd",
            ["Data.BookingDateColumn"] = "Date",
            ["Data.AmountColumn"] = "Amount",
            ["Data.NameColumn"] = "Payee",
            ["Data.PurposeColumn"] = "Memo",
        });
        Assert.Contains("Das Profil wurde angelegt.", profile.Text, StringComparison.Ordinal);

        var preview = await admin.SubmitWithFileAsync(await admin.GetAsync(account.Path), "import", "Upload", csv, "export.csv", "text/csv", new Dictionary<string, string> { ["Profile"] = "Hausbank" });
        Assert.Contains("1 neue Umsätze", preview.Text, StringComparison.Ordinal);
        Assert.Contains("CSV (Hausbank)", preview.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_finance_permission_no_bank_accounts()
    {
        var (manager, _) = await SignedInAsAsync(Roles.Manager, [AccessRight.MasterData]);
        using var _ = manager;

        Assert.Equal("/konto/zugriff-verweigert", (await manager.GetAsync("/verwaltung/bank/konten", followRedirects: false)).RedirectLocation!.AbsolutePath);
        Assert.DoesNotContain("/verwaltung/bank", (await manager.GetAsync("/verwaltung")).Html, StringComparison.Ordinal);
    }

    private static byte[] TestFile(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Bank", "TestFiles", name));
}
