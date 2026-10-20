using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.Properties;
using MietHahn.Web.Tests.Account;

namespace MietHahn.Web.Tests.Tax;

public class LoanPageTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Loan_can_be_created_and_certificate_replaces_plan_interest()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var property = await App.WithScopeAsync(async sp =>
            (await sp.GetRequiredService<IPropertyService>().CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct)).Id!.Value);

        var page = await admin.SubmitAsync(await admin.GetAsync("/verwaltung/finanzen/darlehen"), "loan", new Dictionary<string, string>
        {
            ["Data.PropertyId"] = property.ToString(),
            ["Data.Bank"] = "Sparkasse",
            ["Data.Principal"] = "200000",
            ["Data.DisbursementDate"] = "2024-01-01",
            ["Data.FirstPaymentDate"] = "2024-02-01",
            ["Data.InterestRatePercent"] = "3.6",
            ["Data.Instalment"] = "1000",
            ["Data.MonthsPerPeriod"] = "1",
            ["Data.SharePercent"] = "100",
        });
        Assert.Contains("Das Darlehen wurde angelegt.", page.Text, StringComparison.Ordinal);
        Assert.Contains("Zinsen und Restschuld je Jahr", page.Text, StringComparison.Ordinal);

        page = await admin.SubmitAsync(await admin.GetAsync(page.Path), "certificate", new Dictionary<string, string>
        {
            ["Certificate.Year"] = "2024",
            ["Certificate.Interest"] = "6543.21",
        });
        Assert.Contains("Die Bescheinigung wurde gespeichert.", page.Text, StringComparison.Ordinal);
        Assert.Contains("6.543,21", page.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_loan_shows_a_hint()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        Assert.Contains("Darlehen nicht gefunden", (await admin.GetAsync($"/verwaltung/finanzen/darlehen/{Guid.NewGuid()}")).Text, StringComparison.Ordinal);
    }
}
