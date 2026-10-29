using Elocaro.Application.Accounts;
using Elocaro.Web.Tests.Account;

namespace Elocaro.Web.Tests.Bank;

public class BankImportTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
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
