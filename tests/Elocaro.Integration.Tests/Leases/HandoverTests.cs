using Elocaro.Application.Common;
using Elocaro.Application.Files;
using Elocaro.Application.Leases;
using Elocaro.Application.Metering;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Domain.Admin;
using Elocaro.Domain.Common;
using Elocaro.Domain.Documents;
using Elocaro.Domain.Leases;
using Elocaro.Domain.Metering;
using Elocaro.Domain.People;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Leases;

/// <summary>Handover protocols with real PDFs (Gotenberg): prefill, photos, signature, meter readings, comparison.</summary>
public class HandoverTests(PostgresFixture postgres, GotenbergFixture gotenberg) : IClassFixture<GotenbergFixture>, IAsyncLifetime
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private InfrastructureEnvironment environment = null!;
    private Guid property;
    private Guid unit;
    private Guid lease;
    private Guid person;
    private Guid meter;
    private Guid roomLease;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres, new Dictionary<string, string?> { ["Pdf:GotenbergUrl"] = gotenberg.Url });
        environment.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 10, 10, 0, 0, TimeSpan.Zero));
        await using var scope = environment.Services.CreateAsyncScope();
        var properties = scope.ServiceProvider.GetRequiredService<IPropertyService>();
        var people = scope.ServiceProvider.GetRequiredService<IPersonService>();
        var leases = scope.ServiceProvider.GetRequiredService<ILeaseService>();

        property = Validate(await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct));
        unit = Validate(await properties.CreateUnitAsync(property, new UnitInput { Name = "Wohnung 1", RoomCount = 2.5m, Balcony = true, Bathroom = "Wannenbad" }, Ct));
        person = Validate(await people.CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt", Salutation = Salutation.Mrs }, null, Ct));
        lease = Validate(await leases.CreateAsync(new LeaseCreation { RentalUnitId = unit, Start = new DateOnly(2026, 4, 1), TenantIds = [person], BaseRent = 500m, OperatingCostPrepayment = 100m, People = 1 }, Ct));

        var flat = Validate(await properties.CreateUnitAsync(property, new UnitInput { Name = "WG Nord" }, Ct));
        var room = Validate(await properties.CreateRoomAsync(flat, new RoomInput { Name = "Zimmer 1" }, Ct));
        var ben = Validate(await people.CreateAsync(new PersonInput { FirstName = "Ben", LastName = "Weber" }, null, Ct));
        roomLease = Validate(await leases.CreateAsync(new LeaseCreation { RentalUnitId = room, Start = new DateOnly(2026, 4, 1), TenantIds = [ben], BillingType = BillingType.FlatRate, BaseRent = 400m, OperatingCostFlatRate = 80m, People = 1 }, Ct));

        meter = Validate(await scope.ServiceProvider.GetRequiredService<IMeterService>().CreateAsync(property, new MeterCreation
        {
            Data = new MeterInput { Type = MeterType.ColdWater, Name = "Kaltwasser", Number = "KW-1", AssignmentId = unit },
            InstalledOn = new DateOnly(2026, 1, 1),
            InitialReading = 0m,
        }, Ct));

        var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();
        db.Add(new LandlordProfile("Max Mustermann", "Musterweg 1", "12345", "Musterstadt"));
        await db.SaveChangesAsync(Ct);
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task New_protocol_is_prefilled_from_the_master_data()
    {
        var apartment = Validate(await WithAsync<IHandovers, Result>(h => h.CreateAsync(lease, HandoverType.MoveIn, new DateOnly(2026, 4, 1), Ct)));
        var wg = Validate(await WithAsync<IHandovers, Result>(h => h.CreateAsync(roomLease, HandoverType.MoveIn, new DateOnly(2026, 4, 1), Ct)));

        var view = (await WithAsync<IHandovers, HandoverView?>(h => h.GetAsync(lease, HandoverType.MoveIn, Ct)))!;
        var roomView = (await WithAsync<IHandovers, HandoverView?>(h => h.GetAsync(roomLease, HandoverType.MoveIn, Ct)))!;

        Assert.Equal(apartment, view.Id);
        Assert.Equal(["Flur", "Küche", "Bad (Wannenbad)", "Zimmer 1", "Zimmer 2", "Zimmer 3", "Balkon / Terrasse"], view.Content.Rooms.Select(r => r.Name));
        Assert.Contains("Sanitär (WC, Waschbecken, Wanne/Dusche)", view.Content.Rooms[2].Items.Select(i => i.Component));
        Assert.Equal(["Haustür", "Wohnung", "Briefkasten", "Keller"], view.Content.Keys.Select(k => k.Kind));
        Assert.Equal(("Kaltwasser", "KW-1", "m³"), (view.Content.Meters.Single().Name, view.Content.Meters.Single().Number, view.Content.Meters.Single().Unit));
        Assert.Equal(["Zimmer 1", "Küche (Gemeinschaft)", "Bad (Gemeinschaft)", "Flur (Gemeinschaft)"], roomView.Content.Rooms.Select(r => r.Name));
        Assert.NotEqual(apartment, wg);

        var again = await WithAsync<IHandovers, Result>(h => h.CreateAsync(lease, HandoverType.MoveIn, new DateOnly(2026, 4, 1), Ct));
        Assert.False(again.Succeeded);
    }

    [Fact]
    public async Task Signing_files_the_pdf_enters_the_readings_as_interim_readings_and_the_consumption_uses_them()
    {
        var moveIn = await FillAndSignMoveInAsync(10m);
        Assert.True(moveIn.Succeeded, string.Join(", ", moveIn.Errors));

        // Move-out: starts from the move-in protocol; two new findings, key missing, meter reading, new address.
        var id = Validate(await WithAsync<IHandovers, Result>(h => h.CreateAsync(lease, HandoverType.MoveOut, new DateOnly(2026, 9, 30), Ct)));
        var view = (await WithAsync<IHandovers, HandoverView?>(h => h.GetAsync(lease, HandoverType.MoveOut, Ct)))!;
        var content = view.Content;
        Assert.Equal("Fliese gesprungen", content.Defects.Single().Description); // taken over from move-in
        Assert.NotNull(content.Defects.Single().SourceId);
        content.Rooms[1].Items[1].Condition = ItemCondition.Defect; // kitchen floor
        content.Rooms[1].Items[1].Note = "Brandfleck";
        content.Defects.Add(new HandoverDefect { Room = "Flur", Description = "Loch in der Wand" });
        content.Keys.First(k => k.Kind == "Wohnung").Count = 1;
        content.Meters.Single().Value = 40m;
        content.NewStreet = "Neue Straße 7";
        content.NewPostalCode = "10115";
        content.NewCity = "Berlin";
        Validate(await WithAsync<IHandovers, Result>(h => h.SaveAsync(id, new DateOnly(2026, 9, 30), content, Ct)));

        var compared = (await WithAsync<IHandovers, HandoverView?>(h => h.GetAsync(lease, HandoverType.MoveOut, Ct)))!.Differences;
        Assert.Contains(compared, d => d.Kind == DifferenceKind.Worse && d.Text.Contains("Brandfleck", StringComparison.Ordinal));
        Assert.Contains(compared, d => d.Kind == DifferenceKind.NewDefect && d.Text == "Loch in der Wand");
        Assert.Contains(compared, d => d.Kind == DifferenceKind.KnownDefect);
        Assert.Contains(compared, d => d.Kind == DifferenceKind.MissingKeys && d.Text.Contains("1 von 2", StringComparison.Ordinal));

        var signed = await WithAsync<IHandovers, HandoverSignResult>(h => h.SignAsync(id, new HandoverSignature { SignedByName = "Anna Schmidt", TenantSignature = Png, LandlordSignature = Png }, false, Ct));
        Assert.True(signed.Succeeded, string.Join(", ", signed.Errors));

        await using var scope = environment.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();
        var document = await db.Set<Document>().SingleAsync(d => d.Id == signed.DocumentId, Ct);
        Assert.Equal((DocumentCategory.Minutes, lease, true), (document.Category, document.LeaseId!.Value, document.SharedWithTenants));
        var text = await PdfTextAsync(document.FileId);
        Assert.Contains("Übergabeprotokoll Auszug", text, StringComparison.Ordinal);
        Assert.Contains("Brandfleck", text, StringComparison.Ordinal);
        Assert.Contains("Loch in der Wand", text, StringComparison.Ordinal);
        Assert.Contains("Vergleich mit dem Einzugsprotokoll", text, StringComparison.Ordinal);
        Assert.Contains("40,000 m³", text, StringComparison.Ordinal);
        Assert.Contains("Neue Straße 7", text, StringComparison.Ordinal);
        Assert.Contains("Kaution", text, StringComparison.Ordinal);
        Assert.Contains("Nebenkostenabrechnung", text, StringComparison.Ordinal);

        // The readings are interim readings of the meter and feed the consumption of the stay.
        var readings = (await scope.ServiceProvider.GetRequiredService<IMeterService>().GetAsync(meter, Ct))!.Readings;
        Assert.Contains(readings, r => r is { Kind: ReadingType.InterimReading, Value: 40m } && r.Date == new DateOnly(2026, 9, 30));
        var consumption = (await scope.ServiceProvider.GetRequiredService<IMeterService>().ConsumptionAsync(meter, new Period(new DateOnly(2026, 4, 2), new DateOnly(2026, 9, 30)), Ct))!;
        Assert.Equal(30m, consumption.Quantity); // 40 at move-out − 10 at move-in

        var address = (await db.Set<Person>().Include(p => p.Addresses).SingleAsync(p => p.Id == person, Ct)).AddressAt(new DateOnly(2026, 10, 1));
        Assert.Equal(("Neue Straße 7", "Berlin"), (address!.Street, address.City));

        var changed = await WithAsync<IHandovers, Result>(h => h.SaveAsync(id, new DateOnly(2026, 9, 30), content, Ct));
        var deleted = await WithAsync<IHandovers, Result>(h => h.DeleteAsync(id, Ct));
        Assert.Contains("unterschrieben", changed.Errors.Single(), StringComparison.Ordinal);
        Assert.False(deleted.Succeeded);
    }

    [Fact]
    public async Task A_reading_below_the_last_one_needs_confirmation_and_nothing_is_filed_before()
    {
        Assert.True((await FillAndSignMoveInAsync(10m)).Succeeded);
        var id = Validate(await WithAsync<IHandovers, Result>(h => h.CreateAsync(lease, HandoverType.MoveOut, new DateOnly(2026, 9, 30), Ct)));
        var content = (await WithAsync<IHandovers, HandoverView?>(h => h.GetAsync(lease, HandoverType.MoveOut, Ct)))!.Content;
        content.Meters.Single().Value = 4m; // lower than 10 at move-in
        Validate(await WithAsync<IHandovers, Result>(h => h.SaveAsync(id, new DateOnly(2026, 9, 30), content, Ct)));
        var signature = new HandoverSignature { SignedByName = "Anna Schmidt", TenantSignature = Png };

        var first = await WithAsync<IHandovers, HandoverSignResult>(h => h.SignAsync(id, signature, false, Ct));

        Assert.True(first.NeedsConfirmation);
        Assert.NotEmpty(first.Warnings);
        Assert.Equal(HandoverStatus.Draft, (await WithAsync<IHandovers, HandoverView?>(h => h.GetAsync(lease, HandoverType.MoveOut, Ct)))!.Status);
        await using (var scope = environment.Services.CreateAsyncScope())
        {
            Assert.DoesNotContain((await scope.ServiceProvider.GetRequiredService<IMeterService>().GetAsync(meter, Ct))!.Readings, r => r.Value == 4m);
            Assert.Single(await scope.ServiceProvider.GetRequiredService<ElocaroDbContext>().Set<Document>().ToListAsync(Ct)); // only the move-in protocol
        }

        var confirmed = await WithAsync<IHandovers, HandoverSignResult>(h => h.SignAsync(id, signature, true, Ct));
        Assert.True(confirmed.Succeeded, string.Join(", ", confirmed.Errors));
    }

    [Fact]
    public async Task A_signed_printout_can_be_filed_instead_of_a_signature_on_the_device()
    {
        var id = Validate(await WithAsync<IHandovers, Result>(h => h.CreateAsync(roomLease, HandoverType.MoveIn, new DateOnly(2026, 4, 1), Ct)));
        var preview = await WithAsync<IHandovers, Elocaro.Application.Documents.TemplatePreview>(h => h.PreviewAsync(id, Ct));
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(preview.Pdf!, 0, 4), StringComparison.Ordinal);

        using var scan = new MemoryStream(Png);
        var result = await WithAsync<IHandovers, HandoverSignResult>(h => h.SignWithScanAsync(id, "Ben Weber", "scan.png", scan, false, Ct));

        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        var view = (await WithAsync<IHandovers, HandoverView?>(h => h.GetAsync(roomLease, HandoverType.MoveIn, Ct)))!;
        Assert.Equal((HandoverStatus.Completed, "Ben Weber"), (view.Status, view.SignedByName));
        Assert.NotNull(view.DocumentFileId);
    }

    private async Task<HandoverSignResult> FillAndSignMoveInAsync(decimal reading)
    {
        var id = Validate(await WithAsync<IHandovers, Result>(h => h.CreateAsync(lease, HandoverType.MoveIn, new DateOnly(2026, 4, 1), Ct)));
        var content = (await WithAsync<IHandovers, HandoverView?>(h => h.GetAsync(lease, HandoverType.MoveIn, Ct)))!.Content;
        content.Rooms[1].Items[1].Condition = ItemCondition.Wear;
        var photo = Validate(await WithAsync<IHandovers, Result>(h => h.AddPhotoAsync(id, "bad.png", new MemoryStream(Png), Ct)));
        content.Defects.Add(new HandoverDefect { Room = "Bad", Description = "Fliese gesprungen", PhotoFileIds = [photo] });
        foreach (var key in content.Keys)
        {
            key.Count = 2;
        }

        content.Meters.Single().Value = reading;
        Validate(await WithAsync<IHandovers, Result>(h => h.SaveAsync(id, new DateOnly(2026, 4, 1), content, Ct)));
        return await WithAsync<IHandovers, HandoverSignResult>(h => h.SignAsync(id, new HandoverSignature { SignedByName = "Anna Schmidt", TenantSignature = Png }, false, Ct));
    }

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }

    private async Task<T> WithAsync<TService, T>(Func<TService, Task<T>> action)
        where TService : notnull
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private async Task<string> PdfTextAsync(Guid fileId)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        await using var opened = (await scope.ServiceProvider.GetRequiredService<IFileStore>().OpenAsync(fileId, Ct))!;
        using var buffer = new MemoryStream();
        await opened.Content.CopyToAsync(buffer, Ct);
        using var document = UglyToad.PdfPig.PdfDocument.Open(buffer.ToArray());
        return string.Join(' ', document.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text)).Replace("ﬁ", "fi", StringComparison.Ordinal).Replace("ﬂ", "fl", StringComparison.Ordinal).Replace("ﬀ", "ff", StringComparison.Ordinal);
    }
}
