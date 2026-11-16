using System.Text;
using Elocaro.Application.Admin;
using Elocaro.Application.Common;
using Elocaro.Application.Documents;
using Elocaro.Application.Properties;
using Elocaro.Application.Prospects;
using Elocaro.Domain.Files;
using Elocaro.Domain.Properties;
using Elocaro.Domain.Prospects;
using Elocaro.Infrastructure.Pdf;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Prospects;

public class ExposeTests(PostgresFixture postgres) : IAsyncLifetime
{
    private InfrastructureEnvironment environment = null!;
    private Guid property;
    private Guid unit;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        // The real template engine, but the HTML instead of a PDF (no converter in the test).
        environment = await InfrastructureEnvironment.CreateAsync(postgres, configure: s => s.AddScoped<IPdfGenerator>(sp => new HtmlOnly(ActivatorUtilities.CreateInstance<PdfGenerator>(sp))));
        await With<ILandlordProfileService, bool>(async s =>
        {
            await s.SaveAsync(new LandlordProfileInput { Name = "Hans Müller", Street = "Musterweg 1", PostalCode = "50667", City = "Köln" }, Ct);
            return true;
        });
        property = Validate(await With<IPropertyService, Result>(s => s.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln", RentIndexPerSquareMetre = 10m, RentCapPercent = 15 }, Ct)));
        unit = Validate(await With<IPropertyService, Result>(s => s.CreateUnitAsync(property, new UnitInput { Name = "Wohnung 1", Location = "EG links", RoomCount = 2.5m, Balcony = true, Amenities = "Parkett\nKeller" }, Ct)));
        Validate(await With<IPropertyService, Result>(s => s.SetUnitAreaAsync(unit, new AreaInput { From = new DateOnly(2020, 1, 1), Area = 60m }, Ct)));
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task The_check_uses_the_comparative_rent_of_the_property_and_the_area_of_the_unit()
    {
        var over = await With<IExposes, NewLetCheck?>(s => s.CheckAsync(unit, new ExposeInput { Rent = 700m }, Ct));
        var fine = await With<IExposes, NewLetCheck?>(s => s.CheckAsync(unit, new ExposeInput { Rent = 650m }, Ct));

        Assert.Equal((660m, true, false), (over!.Limit, over.OverLimit, fine!.OverLimit));
        Assert.Null(await With<IExposes, NewLetCheck?>(s => s.CheckAsync(Guid.NewGuid(), new ExposeInput { Rent = 650m }, Ct)));
    }

    [Fact]
    public async Task The_expose_holds_the_data_of_the_unit_and_the_costs_but_none_of_the_landlord_hints()
    {
        var file = await With<IExposes, ExposeFile>(s => s.CreateAsync(unit, new ExposeInput { Rent = 650m, OperatingCosts = 120m, Deposit = 1950m, Description = "Ruhige Lage." }, Ct));

        Assert.True(file.Result.Succeeded, string.Join(", ", file.Result.Errors));
        Assert.Equal("Expose-Lindenweg3-Wohnung1.pdf", file.FileName);
        var html = Encoding.UTF8.GetString(file.Content!);
        Assert.Contains("Wohnung 1", html, StringComparison.Ordinal);
        Assert.Contains("Lindenweg 3", html, StringComparison.Ordinal);
        Assert.Contains("EG links", html, StringComparison.Ordinal);
        Assert.Contains("Parkett", html, StringComparison.Ordinal);
        Assert.Contains("Ruhige Lage.", html, StringComparison.Ordinal);
        Assert.Contains("770,00", html, StringComparison.Ordinal); // total rent
        Assert.DoesNotContain("Mietpreisbremse", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Vormieters", html, StringComparison.Ordinal);

        var withPrevious = await With<IExposes, ExposeFile>(s => s.CreateAsync(unit, new ExposeInput { Rent = 650m, PreviousRent = 640m }, Ct));
        Assert.Contains("Vormieters", Encoding.UTF8.GetString(withPrevious.Content!), StringComparison.Ordinal);
        Assert.False((await With<IExposes, ExposeFile>(s => s.CreateAsync(unit, new ExposeInput(), Ct))).Result.Succeeded);
    }

    [Fact]
    public async Task The_photos_are_in_the_expose_with_captions_and_a_room_shows_its_own_photos_before_those_of_the_flat()
    {
        async Task<Guid> Image() => await With<Elocaro.Application.Files.IFileStore, Guid>(async s => (await s.SaveAsync(new MemoryStream(TestFiles.TwoColorImage(40, 30)), "foto.jpg", Ct)).Id);
        Validate(await With<IUnitPhotos, Result>(s => s.AddAsync(unit, Image().Result, "Wohnzimmer mit Parkett", Ct)));
        var flat = Validate(await With<IPropertyService, Result>(s => s.CreateUnitAsync(property, new UnitInput { Name = "WG 1" }, Ct)));
        var room = Validate(await With<IPropertyService, Result>(s => s.CreateRoomAsync(flat, new RoomInput { Name = "Zimmer 1", Furnishing = Furnishing.Furnished }, Ct)));
        Validate(await With<IUnitPhotos, Result>(s => s.AddAsync(flat, Image().Result, "Gemeinschaftsküche", Ct)));
        Validate(await With<IUnitPhotos, Result>(s => s.AddAsync(room, Image().Result, "Das Zimmer", Ct)));

        var unitHtml = Encoding.UTF8.GetString((await With<IExposes, ExposeFile>(s => s.CreateAsync(unit, new ExposeInput { Rent = 650m }, Ct))).Content!);
        Assert.Contains("Wohnzimmer mit Parkett", unitHtml, StringComparison.Ordinal);
        Assert.Contains("data:image/", unitHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Gemeinschaftsküche", unitHtml, StringComparison.Ordinal);

        var roomHtml = Encoding.UTF8.GetString((await With<IExposes, ExposeFile>(s => s.CreateAsync(room, new ExposeInput { Rent = 400m }, Ct))).Content!);
        Assert.True(roomHtml.IndexOf("Das Zimmer", StringComparison.Ordinal) is var own and >= 0 && roomHtml.IndexOf("Gemeinschaftsküche", StringComparison.Ordinal) > own);
        Assert.Contains("Möbliert", roomHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Wohnzimmer mit Parkett", roomHtml, StringComparison.Ordinal);
    }

    private async Task<T> With<TService, T>(Func<TService, Task<T>> action)
        where TService : notnull
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }

    private sealed class HtmlOnly(IPdfGenerator inner) : IPdfGenerator
    {
        public async Task<byte[]> GenerateAsync(PdfRequest job, CancellationToken cancellationToken = default) => Encoding.UTF8.GetBytes(await inner.RenderHtmlAsync(job, cancellationToken));

        public Task<StoredFile> GenerateAndStoreAsync(PdfRequest job, string fileName, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<string> RenderHtmlAsync(PdfRequest job, CancellationToken cancellationToken = default) => inner.RenderHtmlAsync(job, cancellationToken);

        public Task<byte[]> MergeAsync(IReadOnlyList<byte[]> documents, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
