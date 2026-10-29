using Elocaro.Application.Documents;
using Elocaro.Application.Leases;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Domain.Documents;
using Elocaro.Domain.Leases;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Elocaro.Web.Tests.Browser;

/// <summary>Handover protocol on a phone: fill in, draw the signature on the canvas, file.</summary>
public class HandoverBrowserTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddScoped<IPdfGenerator, FakePdfGenerator>();

    [Fact]
    public async Task Protocol_is_created_filled_in_and_signed_on_the_phone()
    {
        var lease = await App.WithScopeAsync(async sp =>
        {
            var properties = sp.GetRequiredService<IPropertyService>();
            var house = (await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct)).Id!.Value;
            var unit = (await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 1", RoomCount = 1m }, Ct)).Id!.Value;
            var person = (await sp.GetRequiredService<IPersonService>().CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt" }, null, Ct)).Id!.Value;
            return (await sp.GetRequiredService<ILeaseService>().CreateAsync(new LeaseCreation { RentalUnitId = unit, Start = new DateOnly(2026, 4, 1), TenantIds = [person], BaseRent = 500m, People = 1 }, Ct)).Id!.Value;
        });
        var page = await SignedInAsAdminAsync();

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await GotoInteractiveAsync(page, $"/verwaltung/vertraege/{lease}/uebergabe/einzug");
            await page.GetByRole(AriaRole.Button, new() { Name = "Protokoll anlegen" }).ClickAsync();

            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Räume und Zustand" })).ToBeVisibleAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Mangel hinzufügen" }).ClickAsync();
            await page.GetByLabel("Mangel / Auffälligkeit").FillAsync("Kratzer im Parkett");
            await page.GetByRole(AriaRole.Button, new() { Name = "Speichern" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Das Protokoll wurde gespeichert.");

            // Signature with the pointer on the canvas.
            var canvas = page.GetByLabel("Unterschrift des Mieters");
            await canvas.EvaluateAsync("e => e.scrollIntoView({ block: 'center' })"); // keep it clear of the sticky header
            var box = (await canvas.BoundingBoxAsync())!;
            await page.Mouse.MoveAsync(box.X + 20, box.Y + 40);
            await page.Mouse.DownAsync();
            await page.Mouse.MoveAsync(box.X + 80, box.Y + 100, new() { Steps = 8 });
            await page.Mouse.MoveAsync(box.X + 140, box.Y + 50, new() { Steps = 8 });
            await page.Mouse.UpAsync();

            await page.GetByRole(AriaRole.Button, new() { Name = "Unterschreiben und ablegen" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Das Protokoll ist unterschrieben und abgelegt");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Unterschrieben am");
        });

        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();
        var protocol = await db.Set<HandoverProtocol>().SingleAsync(Ct);
        Assert.Equal((HandoverStatus.Completed, "Anna Schmidt"), (protocol.Status, protocol.SignedByName));
        Assert.Equal("Kratzer im Parkett", protocol.Content.Defects.Single().Description);
        Assert.NotNull(protocol.TenantSignatureFileId);
        Assert.Equal(DocumentCategory.Minutes, (await db.Set<Document>().SingleAsync(d => d.Id == protocol.DocumentId, Ct)).Category);
    }

    [Fact]
    public async Task Signing_without_a_drawn_signature_is_refused()
    {
        var lease = await App.WithScopeAsync(async sp =>
        {
            var properties = sp.GetRequiredService<IPropertyService>();
            var house = (await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct)).Id!.Value;
            var unit = (await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 1" }, Ct)).Id!.Value;
            var person = (await sp.GetRequiredService<IPersonService>().CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt" }, null, Ct)).Id!.Value;
            var id = (await sp.GetRequiredService<ILeaseService>().CreateAsync(new LeaseCreation { RentalUnitId = unit, Start = new DateOnly(2026, 4, 1), TenantIds = [person], BaseRent = 500m, People = 1 }, Ct)).Id!.Value;
            await sp.GetRequiredService<IHandovers>().CreateAsync(id, HandoverType.MoveIn, new DateOnly(2026, 4, 1), Ct);
            return id;
        });
        var page = await SignedInAsAdminAsync();

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await GotoInteractiveAsync(page, $"/verwaltung/vertraege/{lease}/uebergabe/einzug");
            await page.GetByRole(AriaRole.Button, new() { Name = "Unterschreiben und ablegen" }).ClickAsync();
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Unterschrift des Mieters fehlt");
        });

        await using var scope = App.Services.CreateAsyncScope();
        Assert.Equal(HandoverStatus.Draft, (await scope.ServiceProvider.GetRequiredService<ElocaroDbContext>().Set<HandoverProtocol>().SingleAsync(Ct)).Status);
    }
}
