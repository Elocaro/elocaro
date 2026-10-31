using Elocaro.Application.Accounts;
using Elocaro.Application.Common;
using Elocaro.Application.Files;
using Elocaro.Application.Leases;
using Elocaro.Application.OperatingCosts;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Application.Receipts;
using Elocaro.Application.Statements;
using Elocaro.Domain.Admin;
using Elocaro.Domain.Common;
using Elocaro.Domain.OperatingCosts;
using Elocaro.Domain.People;
using Elocaro.Domain.Statements;
using Elocaro.Infrastructure.Identity;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Infrastructure.Statements;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Statements;

/// <summary>QR code and link to the tenant portal in the statement letter: only with a public address and a tenant account.</summary>
public class StatementPortalLinkTests(PostgresFixture postgres, GotenbergFixture gotenberg) : IClassFixture<GotenbergFixture>, IAsyncLifetime
{
    private InfrastructureEnvironment? environment;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        if (environment is not null)
        {
            await environment.DisposeAsync();
        }
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Letter_points_to_the_portal_when_a_public_address_is_configured_and_the_tenant_has_an_account()
    {
        var text = await LetterTextAsync(baseUrl: "https://elocaro.example.de/", tenantHasAccount: true);

        Assert.Contains("Mieterportal", text, StringComparison.Ordinal);
        Assert.Contains("https://elocaro.example.de/portal/abrechnungen", text, StringComparison.Ordinal);
        Assert.Contains("keine förmliche Einwendung", text, StringComparison.Ordinal);
        Assert.DoesNotContain("token", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Letter_has_no_portal_hint_without_a_tenant_account()
    {
        var text = await LetterTextAsync(baseUrl: "https://elocaro.example.de/", tenantHasAccount: false);

        Assert.DoesNotContain("Mieterportal", text, StringComparison.Ordinal);
        Assert.Contains("Belegeinsicht", text, StringComparison.Ordinal); // the rest of the letter is unchanged
    }

    [Fact]
    public async Task Letter_has_no_portal_hint_with_the_development_address()
    {
        var text = await LetterTextAsync(baseUrl: null, tenantHasAccount: true);

        Assert.DoesNotContain("Mieterportal", text, StringComparison.Ordinal);
        Assert.Contains("Belegeinsicht", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Qr_code_is_an_svg_data_uri_for_the_plain_link()
    {
        var uri = PortalLinkQr.DataUri(new Uri("https://elocaro.example.de/portal/abrechnungen"));

        Assert.StartsWith("data:image/svg+xml;base64,", uri, StringComparison.Ordinal);
        var svg = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(uri["data:image/svg+xml;base64,".Length..]));
        Assert.Contains("<svg", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", svg, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> LetterTextAsync(string? baseUrl, bool tenantHasAccount)
    {
        var configuration = new Dictionary<string, string?> { ["Pdf:GotenbergUrl"] = gotenberg.Url };
        if (baseUrl is not null)
        {
            configuration["Application:BaseUrl"] = baseUrl;
        }

        environment = await InfrastructureEnvironment.CreateAsync(postgres, configuration);
        environment.Clock.SetUtcNow(new DateTimeOffset(2027, 2, 10, 10, 0, 0, TimeSpan.Zero));
        await using var scope = environment.Services.CreateAsyncScope();
        var properties = scope.ServiceProvider.GetRequiredService<IPropertyService>();
        var house = Validate(await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct));
        var unit = Validate(await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 1" }, Ct));
        Validate(await properties.SetUnitAreaAsync(unit, new AreaInput { From = new DateOnly(2020, 1, 1), Area = 60m }, Ct));
        var anna = Validate(await scope.ServiceProvider.GetRequiredService<IPersonService>().CreateAsync(
            new PersonInput { FirstName = "Anna", LastName = "Schmidt", Salutation = Salutation.Mrs, Email = "anna@example.de" }, null, Ct));
        var lease = Validate(await scope.ServiceProvider.GetRequiredService<ILeaseService>().CreateAsync(
            new LeaseCreation { RentalUnitId = unit, Start = new DateOnly(2025, 1, 1), TenantIds = [anna], BaseRent = 500m, OperatingCostPrepayment = 100m, People = 1 }, Ct));
        Validate(await scope.ServiceProvider.GetRequiredService<ICostAllocation>().SetRuleAsync(house, new CostRuleInput { CostType = "betrkv-01", From = new DateOnly(2020, 1, 1), Key = AllocationKey.LivingArea }, Ct));
        Validate(await scope.ServiceProvider.GetRequiredService<IReceiptService>().CreateAsync(
            new ReceiptInput { SupplierName = "Stadt Köln", InvoiceDate = new DateOnly(2026, 12, 15), Gross = 1200m },
            [],
            new CostItemInput { CostType = "betrkv-01", PropertyId = house, From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 12, 31) },
            Ct));

        var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();
        db.Add(new LandlordProfile("Max Mustermann", "Musterweg 1", "12345", "Musterstadt") { Email = "vermieter@example.de", Iban = "DE02 1203 0000 0000 2020 51", Bank = "Musterbank" });
        if (tenantHasAccount)
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = "anna@example.de", Email = "anna@example.de", DisplayName = "Anna" };
            Assert.True((await manager.CreateAsync(user)).Succeeded);
            Assert.True((await manager.AddToRoleAsync(user, Roles.Tenant)).Succeeded);
            (await db.Set<Person>().SingleAsync(p => p.Id == anna, Ct)).UserId = user.Id;
        }

        await db.SaveChangesAsync(Ct);

        var statementId = Validate(await scope.ServiceProvider.GetRequiredService<IStatements>().ReleaseAsync(house, Period.CalendarYear(2026), true, null, Ct));
        var dispatching = scope.ServiceProvider.GetRequiredService<IStatementDispatch>();
        Validate(await dispatching.DispatchAsync(statementId, lease, new DispatchInput { Channel = DispatchChannel.Post, ProposePrepayments = false }, Ct));
        var fileId = (await dispatching.OverviewAsync(statementId, Ct))!.Tenancies.Single().Dispatch!.FileId;

        await using var opened = (await scope.ServiceProvider.GetRequiredService<IFileStore>().OpenAsync(fileId, Ct))!;
        using var buffer = new MemoryStream();
        await opened.Content.CopyToAsync(buffer, Ct);
        using var document = UglyToad.PdfPig.PdfDocument.Open(buffer.ToArray());
        return string.Join(' ', document.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text));
    }

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }
}
