using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Properties;
using MietHahn.Application.Tax;
using MietHahn.Domain.Tax;
using MietHahn.Integration.Tests.Infrastructure;

namespace MietHahn.Integration.Tests.Tax;

public class LoanServiceTests(PostgresFixture postgres) : IAsyncLifetime
{
    private InfrastructureEnvironment environment = null!;
    private Guid propertyId;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres);
        await using var scope = environment.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IPropertyService>()
            .CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct);
        propertyId = result.Id!.Value;
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    private LoanInput Input(decimal share = 100m) => new()
    {
        PropertyId = propertyId,
        Bank = "Sparkasse",
        LoanNumber = "123",
        Principal = 200_000m,
        DisbursementDate = new DateOnly(2024, 1, 1),
        FirstPaymentDate = new DateOnly(2024, 2, 1),
        InterestRatePercent = 3.6m,
        Instalment = 1_000m,
        SharePercent = share,
    };

    private async Task<T> WithAsync<T>(Func<ILoanService, Task<T>> action)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ILoanService>());
    }

    [Fact]
    public async Task Created_loan_has_a_plan_and_is_listed()
    {
        var id = (await WithAsync(s => s.CreateAsync(Input(), Ct))).Id!.Value;

        var detail = (await WithAsync(s => s.GetAsync(id, Ct)))!;
        Assert.Equal(600m, detail.Plan.Payments[0].Interest);
        Assert.Single(await WithAsync(s => s.ListAsync(Ct)));
        Assert.Null(await WithAsync(s => s.GetAsync(Guid.NewGuid(), Ct)));
    }

    [Fact]
    public async Task Instalment_can_be_derived_from_initial_repayment()
    {
        var input = Input();
        input.Instalment = null;
        input.InitialRepaymentPercent = 2m;

        var id = (await WithAsync(s => s.CreateAsync(input, Ct))).Id!.Value;

        Assert.Equal(933.33m, (await WithAsync(s => s.GetAsync(id, Ct)))!.Input.Instalment);
    }

    [Fact]
    public async Task Certificate_takes_precedence_over_the_plan_in_the_expenses()
    {
        var id = (await WithAsync(s => s.CreateAsync(Input(), Ct))).Id!.Value;
        var planInterest = (await WithAsync(s => s.GetAsync(id, Ct)))!.Years.Single(j => j.Year == 2024).PlanInterest;
        Assert.Equal(planInterest, (await WithAsync(s => s.ExpensesAsync(propertyId, 2024, Ct))).Interest);

        Assert.True((await WithAsync(s => s.SaveCertificateAsync(id, new CertificateInput { Year = 2024, Interest = 6_543.21m, ClosingBalance = 190_000m }, Ct))).Succeeded);
        Assert.True((await WithAsync(s => s.SaveCertificateAsync(id, new CertificateInput { Year = 2024, Interest = 6_543.21m }, Ct))).Succeeded);

        var detail = (await WithAsync(s => s.GetAsync(id, Ct)))!;
        Assert.Single(detail.Certificates);
        Assert.Equal(6_543.21m, detail.Years.Single(j => j.Year == 2024).Interest);
        Assert.Equal(6_543.21m, (await WithAsync(s => s.ExpensesAsync(propertyId, 2024, Ct))).Interest);
    }

    [Fact]
    public async Task Expenses_apply_the_share_and_add_one_off_costs()
    {
        var id = (await WithAsync(s => s.CreateAsync(Input(share: 50m), Ct))).Id!.Value;
        await WithAsync(s => s.SaveCertificateAsync(id, new CertificateInput { Year = 2024, Interest = 6_000m }, Ct));
        await WithAsync(s => s.AddFinancingCostAsync(id, new FinancingCostInput { Date = new DateOnly(2024, 1, 5), Amount = 2_000m, Kind = FinancingCostKind.Disagio, Description = "Disagio" }, Ct));

        var expenses = await WithAsync(s => s.ExpensesAsync(propertyId, 2024, Ct));

        Assert.Equal(3_000m, expenses.Interest);
        Assert.Equal(2_000m, expenses.OneOffCosts);
        Assert.Equal(5_000m, expenses.Total);
        Assert.Equal(0m, (await WithAsync(s => s.ExpensesAsync(propertyId, 2030, Ct))).OneOffCosts);
    }

    [Fact]
    public async Task Extra_payments_and_costs_can_be_removed_and_invalid_input_is_rejected()
    {
        var id = (await WithAsync(s => s.CreateAsync(Input(), Ct))).Id!.Value;
        var extra = (await WithAsync(s => s.AddExtraPaymentAsync(id, new ExtraPaymentInput { Date = new DateOnly(2024, 6, 15), Amount = 10_000m }, Ct))).Id!.Value;
        Assert.Single((await WithAsync(s => s.GetAsync(id, Ct)))!.ExtraPayments);

        Assert.True((await WithAsync(s => s.RemoveExtraPaymentAsync(id, extra, Ct))).Succeeded);
        Assert.Empty((await WithAsync(s => s.GetAsync(id, Ct)))!.ExtraPayments);

        var invalid = Input();
        invalid.FirstPaymentDate = invalid.DisbursementDate;
        Assert.False((await WithAsync(s => s.CreateAsync(invalid, Ct))).Succeeded);
        Assert.False((await WithAsync(s => s.RemoveExtraPaymentAsync(id, Guid.NewGuid(), Ct))).Succeeded);
    }
}
