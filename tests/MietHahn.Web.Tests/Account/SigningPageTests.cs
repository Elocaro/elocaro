using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Documents;
using MietHahn.Application.Leases;
using MietHahn.Application.People;
using MietHahn.Application.Properties;
using MietHahn.Domain.Admin;
using MietHahn.Domain.Leases;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Web.Tests.Account;

public class SigningPageTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    protected override void ConfigureServices(IServiceCollection services) => services.AddScoped<IPdfGenerator, FakePdfGenerator>();

    [Fact]
    public async Task An_unknown_signing_link_is_explained_without_login()
    {
        using var browser = App.Browser();

        var page = await browser.GetAsync("/unterschrift/gibt-es-nicht");

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains("Link ungültig", page.Text);
        Assert.Null(page.Document.QuerySelector("form"));
    }

    [Fact]
    public async Task The_document_of_an_unknown_link_is_not_found()
    {
        using var browser = App.Browser();

        var page = await browser.GetAsync("/unterschrift/gibt-es-nicht/dokument", followRedirects: false);

        Assert.Equal(HttpStatusCode.NotFound, page.Status);
    }

    [Fact]
    public async Task A_tenant_reads_the_contract_and_signs_with_the_personal_link()
    {
        var ct = TestContext.Current.CancellationToken;
        var (contract, token) = await App.WithScopeAsync(async sp =>
        {
            var properties = sp.GetRequiredService<IPropertyService>();
            var house = (await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, ct)).Id!.Value;
            var unit = (await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 1" }, ct)).Id!.Value;
            var anna = (await sp.GetRequiredService<IPersonService>().CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt", Email = "anna@example.de" }, null, ct)).Id!.Value;
            var lease = (await sp.GetRequiredService<ILeaseService>().CreateAsync(new LeaseCreation { RentalUnitId = unit, Start = new DateOnly(2026, 11, 1), TenantIds = [anna], BaseRent = 650m }, ct)).Id!.Value;
            var db = sp.GetRequiredService<MietHahnDbContext>();
            db.Add(new LandlordProfile("Andy Hahn", "Musterweg 1", "12345", "Musterstadt") { Iban = "DE02 1203 0000 0000 2020 51" });
            await db.SaveChangesAsync(ct);
            var templates = sp.GetRequiredService<IDocumentTemplates>();
            await templates.InstallDefaultsAsync(ct);
            var template = (await templates.ListAsync(ct)).Single(t => t.Name == "Mietvertrag Wohnraum").Id;
            var draft = (await sp.GetRequiredService<ILeaseContracts>().CreateDraftAsync(lease, template, ct)).Id!.Value;
            var links = await sp.GetRequiredService<IContractSignatures>().StartAsync(draft, false, ct);
            Assert.True(links.Succeeded, string.Join(" ", links.Errors));
            return (draft, links.Links[0].Url[(links.Links[0].Url.LastIndexOf('/') + 1)..]);
        });
        using var browser = App.Browser();

        var page = await browser.GetAsync($"/unterschrift/{token}");
        Assert.Contains("Anna Schmidt", page.Text);
        Assert.NotNull(page.Document.QuerySelector("iframe"));
        var pdf = await browser.Http.GetAsync(new Uri($"/unterschrift/{token}/dokument", UriKind.Relative), ct);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);

        var refused = await browser.SubmitAsync(page, "unterschrift", new Dictionary<string, string> { ["Input.TypedName"] = "Anna Schmidt" });
        Assert.Contains("gelesen", refused.Text);

        var done = await browser.SubmitAsync(page, "unterschrift", new Dictionary<string, string> { ["Input.TypedName"] = "Anna Schmidt", ["Input.Accepted"] = "true" });
        Assert.Contains("Ihre Unterschrift ist gespeichert", done.Text);
        var status = await App.WithScopeAsync(sp => sp.GetRequiredService<MietHahnDbContext>().Set<LeaseContract>().Where(c => c.Id == contract).Select(c => c.Status).SingleAsync(ct));
        Assert.Equal(ContractStatus.Concluded, status);
    }
}
