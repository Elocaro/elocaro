using System.Security.Claims;
using System.Text.Json;
using Elocaro.Application.Accounts;
using Elocaro.Application.Common;
using Elocaro.Application.Files;
using Elocaro.Application.Leases;
using Elocaro.Application.OperatingCosts;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Application.Receipts;
using Elocaro.Application.Statements;
using Elocaro.Domain.Common;
using Elocaro.Domain.OperatingCosts;
using Elocaro.Domain.People;
using Elocaro.Domain.Statements;
using Elocaro.Infrastructure.Identity;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Statements;

/// <summary>The sent statement as the tenant sees it in the portal: data separation, receipts, previous year.</summary>
public class TenantStatementTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly ClaimsPrincipal Admin = new(new ClaimsIdentity([new Claim(ClaimTypes.Role, Roles.Admin)], "test"));

    private InfrastructureEnvironment environment = null!;
    private Guid house;
    private Guid leaseAnna;
    private Guid leaseBen;
    private ClaimsPrincipal anna = null!;
    private ClaimsPrincipal ben = null!;
    private Guid taxReceipt;
    private Guid insuranceReceipt;
    private Guid taxFile;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Period Year(int year) => Period.CalendarYear(year);

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres);
        environment.Clock.SetUtcNow(new DateTimeOffset(2027, 2, 10, 10, 0, 0, TimeSpan.Zero));
        await using var scope = environment.Services.CreateAsyncScope();
        var properties = scope.ServiceProvider.GetRequiredService<IPropertyService>();
        var people = scope.ServiceProvider.GetRequiredService<IPersonService>();
        var leases = scope.ServiceProvider.GetRequiredService<ILeaseService>();

        house = Validate(await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct));
        var unit1 = Validate(await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 1" }, Ct));
        var unit2 = Validate(await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 2" }, Ct));
        Validate(await properties.SetUnitAreaAsync(unit1, new AreaInput { From = new DateOnly(2020, 1, 1), Area = 60m }, Ct));
        Validate(await properties.SetUnitAreaAsync(unit2, new AreaInput { From = new DateOnly(2020, 1, 1), Area = 40m }, Ct));
        var annaId = Validate(await people.CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt", Email = "anna@example.de" }, null, Ct));
        var benId = Validate(await people.CreateAsync(new PersonInput { FirstName = "Ben", LastName = "Weber", Email = "ben@example.de" }, null, Ct));
        leaseAnna = Validate(await leases.CreateAsync(new LeaseCreation { RentalUnitId = unit1, Start = new DateOnly(2024, 1, 1), TenantIds = [annaId], BaseRent = 500m, OperatingCostPrepayment = 100m, People = 1 }, Ct));
        leaseBen = Validate(await leases.CreateAsync(new LeaseCreation { RentalUnitId = unit2, Start = new DateOnly(2024, 1, 1), TenantIds = [benId], BaseRent = 400m, OperatingCostPrepayment = 80m, People = 1 }, Ct));
        foreach (var type in new[] { "betrkv-01", "betrkv-13" })
        {
            Validate(await scope.ServiceProvider.GetRequiredService<ICostAllocation>().SetRuleAsync(house, new CostRuleInput { CostType = type, From = new DateOnly(2020, 1, 1), Key = AllocationKey.LivingArea }, Ct));
        }

        anna = await TenantAccountAsync(annaId, "anna@example.de");
        ben = await TenantAccountAsync(benId, "ben@example.de");
        (taxReceipt, taxFile) = await ReceiptAsync(2026, "betrkv-01", 1000m, "Stadt Köln", withFile: true);
        (insuranceReceipt, _) = await ReceiptAsync(2026, "betrkv-13", 500m, "Versicherung AG", withFile: true);
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Tenant_sees_only_statements_that_were_sent_to_their_own_lease()
    {
        var statementId = await ReleaseAsync(2026);
        var annaDispatch = await DispatchAsync(statementId, leaseAnna);

        var own = await WithAsync<ITenantStatements, IReadOnlyList<PortalStatementRow>>(s => s.ListAsync(anna, Ct));
        var row = Assert.Single(own);
        Assert.Equal((annaDispatch, leaseAnna, Year(2026), false), (row.DispatchId, row.LeaseId, row.BillingPeriod, row.Superseded));
        Assert.Empty(await WithAsync<ITenantStatements, IReadOnlyList<PortalStatementRow>>(s => s.ListAsync(ben, Ct))); // not sent to Ben's lease yet

        var benDispatch = await DispatchAsync(statementId, leaseBen);
        Assert.Null(await WithAsync<ITenantStatements, PortalStatement?>(s => s.GetAsync(anna, benDispatch, Ct)));
        Assert.Null(await WithAsync<ITenantStatements, PortalStatement?>(s => s.GetAsync(ben, annaDispatch, Ct)));
        Assert.NotNull(await WithAsync<ITenantStatements, PortalStatement?>(s => s.GetAsync(ben, benDispatch, Ct)));
        Assert.Null(await WithAsync<ITenantStatements, PortalStatement?>(s => s.GetAsync(anna, Guid.NewGuid(), Ct)));
    }

    [Fact]
    public async Task Other_accounts_and_roles_see_nothing()
    {
        var statementId = await ReleaseAsync(2026);
        var dispatch = await DispatchAsync(statementId, leaseAnna);
        var stranger = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, Roles.Tenant)], "test"));

        Assert.Empty(await WithAsync<ITenantStatements, IReadOnlyList<PortalStatementRow>>(s => s.ListAsync(Admin, Ct)));
        Assert.Empty(await WithAsync<ITenantStatements, IReadOnlyList<PortalStatementRow>>(s => s.ListAsync(stranger, Ct)));
        Assert.Null(await WithAsync<ITenantStatements, PortalStatement?>(s => s.GetAsync(Admin, dispatch, Ct)));
        Assert.Null(await WithAsync<ITenantStatements, PortalStatement?>(s => s.GetAsync(stranger, dispatch, Ct)));
    }

    [Fact]
    public async Task An_unsent_newer_revision_stays_hidden_and_a_sent_one_marks_the_old_as_superseded()
    {
        var first = await ReleaseAsync(2026);
        var firstDispatch = await DispatchAsync(first, leaseAnna);
        await ReceiptAsync(2026, "betrkv-01", 300m, "Nachtrag GmbH", withFile: false);
        var second = Validate(await WithAsync<IStatements, Result>(s => s.ReleaseAsync(house, Year(2026), true, "Nachträgliche Rechnung", Ct)));

        var hidden = Assert.Single(await WithAsync<ITenantStatements, IReadOnlyList<PortalStatementRow>>(s => s.ListAsync(anna, Ct)));
        Assert.Equal((firstDispatch, 1, false), (hidden.DispatchId, hidden.Revision, hidden.Superseded));
        var stillFirst = (await WithAsync<ITenantStatements, PortalStatement?>(s => s.GetAsync(anna, firstDispatch, Ct)))!;
        Assert.False(stillFirst.Header.Superseded);
        Assert.Null(stillFirst.Reason);

        var secondDispatch = await DispatchAsync(second, leaseAnna);
        var rows = await WithAsync<ITenantStatements, IReadOnlyList<PortalStatementRow>>(s => s.ListAsync(anna, Ct));
        Assert.Equal([(secondDispatch, 2, false), (firstDispatch, 1, true)], rows.Select(r => (r.DispatchId, r.Revision, r.Superseded)));
        var corrected = (await WithAsync<ITenantStatements, PortalStatement?>(s => s.GetAsync(anna, secondDispatch, Ct)))!;
        Assert.Equal("Nachträgliche Rechnung", corrected.Reason);
        Assert.True((await WithAsync<ITenantStatements, PortalStatement?>(s => s.GetAsync(anna, firstDispatch, Ct)))!.Header.Superseded);
    }

    [Fact]
    public async Task View_model_holds_the_own_lines_with_the_derivation_and_nothing_of_other_tenants()
    {
        var statementId = await ReleaseAsync(2026);
        var dispatch = await DispatchAsync(statementId, leaseAnna);
        await DispatchAsync(statementId, leaseBen);

        var statement = (await WithAsync<ITenantStatements, PortalStatement?>(s => s.GetAsync(anna, dispatch, Ct)))!;

        // 60 of 100 m²: 600 € of the 1,000 € property tax, 300 € of the 500 € insurance.
        var tax = statement.Lines.Single(l => l.CostType == "betrkv-01");
        Assert.Equal((1000m, 600m), (tax.CostTotal, tax.Share));
        Assert.StartsWith("1.000,00 € nach Wohnfläche: Ihr Anteil 600,00 €", tax.Summary, StringComparison.Ordinal);
        Assert.Equal(900m, statement.Lines.Sum(l => l.Share));
        Assert.Equal(900m, statement.Balance); // no prepayments paid: the whole share is an additional payment

        // Data separation: the snapshot knows the names of both tenants (labels of the leases), the view model must not.
        var stored = (await WithAsync<IStatements, StoredStatement?>(s => s.GetAsync(statementId, Ct)))!;
        var labels = stored.Result.Tenants.Select(t => t.Label).ToList();
        Assert.Equal(2, labels.Count);
        Assert.Contains(labels, l => l.Contains("Weber", StringComparison.Ordinal)); // the guard below is only meaningful if the snapshot holds names
        var json = JsonSerializer.Serialize(statement);
        Assert.Contains("Wohnung 1", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Wohnung 2", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Weber", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Schmidt", json, StringComparison.Ordinal);
        foreach (var label in labels)
        {
            Assert.DoesNotContain(label, json, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(leaseBen.ToString(), json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Receipts_are_hidden_by_default_and_shown_only_for_released_ones()
    {
        var statementId = await ReleaseAsync(2026);
        var annaDispatch = await DispatchAsync(statementId, leaseAnna);

        Assert.Empty((await WithAsync<ITenantStatements, PortalStatement?>(s => s.GetAsync(anna, annaDispatch, Ct)))!.Receipts);
        Assert.False(await CanOpenAsync(anna, annaDispatch, taxReceipt, taxFile)); // nothing released

        Validate(await WithAsync<IStatementReceiptShares, Result>(s => s.SetSharedAsync(statementId, [taxReceipt], Ct)));

        var group = Assert.Single((await WithAsync<ITenantStatements, PortalStatement?>(s => s.GetAsync(anna, annaDispatch, Ct)))!.Receipts);
        Assert.Equal("betrkv-01", group.CostType);
        var receipt = Assert.Single(group.Receipts);
        Assert.Equal(("Stadt Köln", 1000m), (receipt.Supplier, receipt.Amount));
        Assert.Equal(taxFile, Assert.Single(receipt.Files).FileId);
        Assert.True(await CanOpenAsync(anna, annaDispatch, taxReceipt, taxFile));
        Assert.False(await CanOpenAsync(anna, annaDispatch, insuranceReceipt, await FileOfAsync(insuranceReceipt))); // not released
        Assert.False(await CanOpenAsync(anna, annaDispatch, taxReceipt, await FileOfAsync(insuranceReceipt))); // file of another receipt
        Assert.False(await CanOpenAsync(anna, annaDispatch, Guid.NewGuid(), taxFile));
    }

    [Fact]
    public async Task A_released_receipt_is_reachable_only_through_a_dispatch_of_the_own_lease()
    {
        var statementId = await ReleaseAsync(2026);
        var annaDispatch = await DispatchAsync(statementId, leaseAnna);
        var benDispatch = await DispatchAsync(statementId, leaseBen);
        Validate(await WithAsync<IStatementReceiptShares, Result>(s => s.SetSharedAsync(statementId, [taxReceipt], Ct)));

        Assert.False(await CanOpenAsync(anna, benDispatch, taxReceipt, taxFile));
        Assert.False(await CanOpenAsync(ben, annaDispatch, taxReceipt, taxFile));
        Assert.False(await CanOpenAsync(Admin, annaDispatch, taxReceipt, taxFile));
        Assert.True(await CanOpenAsync(ben, benDispatch, taxReceipt, taxFile));
    }

    [Fact]
    public async Task A_release_is_not_valid_for_another_revision_and_an_unsent_statement_shows_nothing()
    {
        var first = await ReleaseAsync(2026);
        var firstDispatch = await DispatchAsync(first, leaseAnna);
        Validate(await WithAsync<IStatementReceiptShares, Result>(s => s.SetSharedAsync(first, [taxReceipt], Ct)));
        await ReceiptAsync(2026, "betrkv-01", 300m, "Nachtrag GmbH", withFile: false);
        var second = Validate(await WithAsync<IStatements, Result>(s => s.ReleaseAsync(house, Year(2026), true, "Nachtrag", Ct)));
        var secondDispatch = await DispatchAsync(second, leaseAnna);

        Assert.Empty((await WithAsync<ITenantStatements, PortalStatement?>(s => s.GetAsync(anna, secondDispatch, Ct)))!.Receipts);
        Assert.False(await CanOpenAsync(anna, secondDispatch, taxReceipt, taxFile));
        Assert.True(await CanOpenAsync(anna, firstDispatch, taxReceipt, taxFile));
    }

    [Fact]
    public async Task After_withdrawing_the_release_the_receipt_cannot_be_fetched_any_more()
    {
        var statementId = await ReleaseAsync(2026);
        var dispatch = await DispatchAsync(statementId, leaseAnna);
        Validate(await WithAsync<IStatementReceiptShares, Result>(s => s.SetSharedAsync(statementId, [taxReceipt, insuranceReceipt], Ct)));
        Assert.True(await CanOpenAsync(anna, dispatch, taxReceipt, taxFile));

        Validate(await WithAsync<IStatementReceiptShares, Result>(s => s.SetSharedAsync(statementId, [insuranceReceipt], Ct)));

        Assert.False(await CanOpenAsync(anna, dispatch, taxReceipt, taxFile));
        Assert.True(await CanOpenAsync(anna, dispatch, insuranceReceipt, await FileOfAsync(insuranceReceipt)));
        var overview = (await WithAsync<IStatementReceiptShares, ReceiptShareOverview?>(s => s.OverviewAsync(statementId, Ct)))!;
        Assert.Equal([false, true], overview.Receipts.OrderBy(r => r.Supplier, StringComparer.Ordinal).Select(r => r.Shared));

        // Releasing again after a withdrawal works.
        Validate(await WithAsync<IStatementReceiptShares, Result>(s => s.SetSharedAsync(statementId, [taxReceipt], Ct)));
        Assert.True(await CanOpenAsync(anna, dispatch, taxReceipt, taxFile));
    }

    [Fact]
    public async Task Only_receipts_of_the_statement_with_a_file_can_be_released()
    {
        var statementId = await ReleaseAsync(2026);
        var foreignReceipt = (await ReceiptAsync(2025, "betrkv-01", 70m, "Anderes Jahr", withFile: true)).Receipt;
        var withoutFile = (await ReceiptAsync(2026, "betrkv-01", 50m, "Ohne Datei", withFile: false)).Receipt;
        var later = Validate(await WithAsync<IStatements, Result>(s => s.ReleaseAsync(house, Year(2026), true, "mit Beleg ohne Datei", Ct)));

        var unknown = await WithAsync<IStatementReceiptShares, Result>(s => s.SetSharedAsync(statementId, [Guid.NewGuid()], Ct));
        var otherYear = await WithAsync<IStatementReceiptShares, Result>(s => s.SetSharedAsync(statementId, [foreignReceipt], Ct));
        var notInThisRevision = await WithAsync<IStatementReceiptShares, Result>(s => s.SetSharedAsync(statementId, [withoutFile], Ct));
        var noFile = await WithAsync<IStatementReceiptShares, Result>(s => s.SetSharedAsync(later, [withoutFile], Ct));

        Assert.All(new[] { unknown, otherYear, notInThisRevision }, r => Assert.Contains("gehört nicht zu dieser Abrechnung", r.Errors.Single(), StringComparison.Ordinal));
        Assert.Contains("keine Datei", noFile.Errors.Single(), StringComparison.Ordinal);
        Assert.Null(await WithAsync<IStatementReceiptShares, ReceiptShareOverview?>(s => s.OverviewAsync(Guid.NewGuid(), Ct)));
    }

    [Fact]
    public async Task Opening_a_released_receipt_is_written_to_the_access_log()
    {
        var statementId = await ReleaseAsync(2026);
        var dispatch = await DispatchAsync(statementId, leaseAnna);
        Validate(await WithAsync<IStatementReceiptShares, Result>(s => s.SetSharedAsync(statementId, [taxReceipt], Ct)));

        await using (var opened = await OpenAsync(anna, dispatch, taxReceipt, taxFile))
        {
            Assert.NotNull(opened);
        }

        await using var scope = environment.Services.CreateAsyncScope();
        var entries = await scope.ServiceProvider.GetRequiredService<ElocaroDbContext>().Set<Elocaro.Infrastructure.Audit.AuditEntry>().Where(a => a.EntityId == taxReceipt && a.Action == Elocaro.Application.Audit.AuditAction.Read).ToListAsync(Ct);
        var entry = Assert.Single(entries);
        Assert.Equal("Receipt", entry.EntityType);
        Assert.Contains("Mieterportal", entry.Purpose, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Previous_year_is_compared_only_with_the_statement_sent_to_the_same_lease()
    {
        await ReceiptAsync(2025, "betrkv-01", 800m, "Stadt Köln", withFile: false);
        var previousId = await ReleaseAsync(2025);
        var currentId = await ReleaseAsync(2026);
        var currentDispatch = await DispatchAsync(currentId, leaseAnna);

        // Released, but never sent to this lease: no comparison.
        Assert.Null((await WithAsync<ITenantStatements, PortalStatement?>(s => s.GetAsync(anna, currentDispatch, Ct)))!.Previous);

        await DispatchAsync(previousId, leaseAnna);
        var comparison = (await WithAsync<ITenantStatements, PortalStatement?>(s => s.GetAsync(anna, currentDispatch, Ct)))!.Previous!;

        Assert.Equal(Year(2025), comparison.PreviousPeriod);
        var tax = comparison.Lines.Single(l => l.CostType == "betrkv-01");
        Assert.Equal((480m, 600m, 120m), (tax.Previous, tax.Current, tax.Difference));
        Assert.Null(comparison.Lines.Single(l => l.CostType == "betrkv-13").Previous); // new cost type, no invented cause
        Assert.False(comparison.TermDiffers);

        // Ben never got the 2025 statement: no comparison for him.
        var benDispatch = await DispatchAsync(currentId, leaseBen);
        Assert.Null((await WithAsync<ITenantStatements, PortalStatement?>(s => s.GetAsync(ben, benDispatch, Ct)))!.Previous);
    }

    // ---- helpers ---------------------------------------------------------------------------------------------------

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }

    private Task<Guid> ReleaseAsync(int year) =>
        WithAsync<IStatements, Result>(s => s.ReleaseAsync(house, Year(year), true, null, Ct)).ContinueWith(t => Validate(t.Result), Ct, TaskContinuationOptions.None, TaskScheduler.Default);

    /// <summary>Records the dispatch of the statement to the lease (postal dispatch; no letter is rendered).</summary>
    private async Task<Guid> DispatchAsync(Guid statementId, Guid leaseId)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var statement = (await scope.ServiceProvider.GetRequiredService<IStatements>().GetAsync(statementId, Ct))!;
        var file = await scope.ServiceProvider.GetRequiredService<IFileStore>().SaveAsync(new MemoryStream(TestFiles.Pdf("Brief")), "abrechnung.pdf", Ct);
        var dispatch = new StatementDispatch(statementId, leaseId, DispatchChannel.Post, new DateOnly(2027, 2, 10), file.Id, null, null, statement.Result.Tenants.Single(t => t.LeaseId == leaseId).Balance, 0m, null, null);
        var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();
        db.Add(dispatch);
        await db.SaveChangesAsync(Ct);
        return dispatch.Id;
    }

    private async Task<(Guid Receipt, Guid File)> ReceiptAsync(int year, string costType, decimal amount, string supplier, bool withFile)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var files = new List<Guid>();
        if (withFile)
        {
            files.Add((await scope.ServiceProvider.GetRequiredService<IFileStore>().SaveAsync(new MemoryStream(TestFiles.Pdf(supplier)), $"{supplier}.pdf", Ct)).Id);
        }

        var receipt = Validate(await scope.ServiceProvider.GetRequiredService<IReceiptService>().CreateAsync(
            new ReceiptInput { SupplierName = supplier, InvoiceNumber = $"R-{year}-{amount:0}", InvoiceDate = new DateOnly(year, 12, 15), Gross = amount },
            files,
            new CostItemInput { CostType = costType, PropertyId = house, From = new DateOnly(year, 1, 1), To = new DateOnly(year, 12, 31) },
            Ct));
        return (receipt, files.FirstOrDefault());
    }

    private async Task<Guid> FileOfAsync(Guid receiptId)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ElocaroDbContext>().Set<Elocaro.Domain.Receipts.ReceiptFile>().Where(f => f.ReceiptId == receiptId).Select(f => f.FileId).SingleAsync(Ct);
    }

    private async Task<OpenedFile?> OpenAsync(ClaimsPrincipal user, Guid dispatchId, Guid receiptId, Guid fileId)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ITenantStatements>().OpenReceiptFileAsync(user, dispatchId, receiptId, fileId, Ct);
    }

    private async Task<bool> CanOpenAsync(ClaimsPrincipal user, Guid dispatchId, Guid receiptId, Guid fileId)
    {
        await using var opened = await OpenAsync(user, dispatchId, receiptId, fileId);
        return opened is not null;
    }

    private async Task<TResult> WithAsync<TService, TResult>(Func<TService, Task<TResult>> action)
        where TService : notnull
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private async Task<ClaimsPrincipal> TenantAccountAsync(Guid personId, string email)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = email, Email = email, DisplayName = email };
        Assert.True((await manager.CreateAsync(user)).Succeeded);
        Assert.True((await manager.AddToRoleAsync(user, Roles.Tenant)).Succeeded);
        var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();
        var person = await db.Set<Person>().SingleAsync(p => p.Id == personId, Ct);
        person.UserId = user.Id;
        await db.SaveChangesAsync(Ct);
        return new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Role, Roles.Tenant)], "test"));
    }
}
