using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.Properties;
using MietHahn.Application.Tax;
using MietHahn.Web.Tests.Account;

namespace MietHahn.Web.Tests.Tax;

public class LoanPageTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Loan_page_shows_certificate_replaces_plan_interest()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var property = await App.WithScopeAsync(async sp =>
            (await sp.GetRequiredService<IPropertyService>().CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct)).Id!.Value);

        var loan = await App.WithScopeAsync(async sp =>
        {
            var loans = sp.GetRequiredService<ILoanService>();
            var created = await loans.CreateAsync(new LoanInput
            {
                PropertyId = property,
                Bank = "Sparkasse",
                Principal = 200_000m,
                DisbursementDate = new DateOnly(2024, 1, 1),
                FirstPaymentDate = new DateOnly(2024, 2, 1),
                InterestRatePercent = 3.6m,
                Instalment = 1000m,
                MonthsPerPeriod = 1,
                SharePercent = 100m,
            }, Ct);
            Assert.True(created.Succeeded);
            var id = (await loans.ListAsync(Ct)).Single().Id;
            Assert.True((await loans.SaveCertificateAsync(id, new CertificateInput { Year = 2024, Interest = 6543.21m }, Ct)).Succeeded);
            return id;
        });

        var page = await admin.GetAsync($"/verwaltung/finanzen/darlehen/{loan}");
        Assert.Contains("Zinsen und Restschuld je Jahr", page.Text, StringComparison.Ordinal);
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
