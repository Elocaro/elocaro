using Elocaro.Application.Common;
using Elocaro.Application.Files;
using Elocaro.Application.Properties;
using Elocaro.Domain.Properties;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Properties;

public class UnitPhotoTests(PostgresFixture postgres) : IAsyncLifetime
{
    private InfrastructureEnvironment environment = null!;
    private Guid unit;
    private Guid room;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres);
        var property = Validate(await With<IPropertyService, Result>(s => s.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct)));
        unit = Validate(await With<IPropertyService, Result>(s => s.CreateUnitAsync(property, new UnitInput { Name = "WG 1" }, Ct)));
        room = Validate(await With<IPropertyService, Result>(s => s.CreateRoomAsync(unit, new RoomInput { Name = "Zimmer 1" }, Ct)));
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    private Task<Guid> ImageAsync() => With<IFileStore, Guid>(async s => (await s.SaveAsync(new MemoryStream(TestFiles.TwoColorImage(40, 30)), "foto.jpg", Ct)).Id);

    [Fact]
    public async Task Photos_of_a_unit_and_a_room_are_kept_apart_in_order_and_can_be_moved_captioned_and_removed()
    {
        var a = Validate(await With<IUnitPhotos, Result>(s => s.AddAsync(unit, ImageAsync().Result, "Küche", Ct)));
        var b = Validate(await With<IUnitPhotos, Result>(s => s.AddAsync(unit, ImageAsync().Result, null, Ct)));
        Validate(await With<IUnitPhotos, Result>(s => s.AddAsync(room, ImageAsync().Result, null, Ct)));

        Assert.Equal([a, b], (await With<IUnitPhotos, IReadOnlyList<UnitPhotoRow>>(s => s.ListAsync(unit, Ct))).Select(p => p.Id));
        Assert.Single(await With<IUnitPhotos, IReadOnlyList<UnitPhotoRow>>(s => s.ListAsync(room, Ct)));

        Validate(await With<IUnitPhotos, Result>(s => s.MoveAsync(b, true, Ct)));
        Validate(await With<IUnitPhotos, Result>(s => s.MoveAsync(b, true, Ct))); // already first: nothing happens
        Validate(await With<IUnitPhotos, Result>(s => s.SetCaptionAsync(b, "  Bad  ", Ct)));
        var rows = await With<IUnitPhotos, IReadOnlyList<UnitPhotoRow>>(s => s.ListAsync(unit, Ct));
        Assert.Equal([(b, "Bad"), (a, "Küche")], rows.Select(p => (p.Id, p.Caption)));

        Validate(await With<IUnitPhotos, Result>(s => s.RemoveAsync(b, Ct)));
        Assert.Equal([a], (await With<IUnitPhotos, IReadOnlyList<UnitPhotoRow>>(s => s.ListAsync(unit, Ct))).Select(p => p.Id));
    }

    [Fact]
    public async Task Only_images_for_known_units_and_at_most_twelve_photos_are_accepted()
    {
        var notImage = await With<IFileStore, Guid>(async s => (await s.SaveAsync(new MemoryStream("%PDF-1.4\n%%EOF"u8.ToArray()), "x.pdf", Ct)).Id);
        Assert.False((await With<IUnitPhotos, Result>(s => s.AddAsync(unit, notImage, null, Ct))).Succeeded);
        Assert.False((await With<IUnitPhotos, Result>(s => s.AddAsync(Guid.NewGuid(), ImageAsync().Result, null, Ct))).Succeeded);

        var image = await ImageAsync();
        for (var i = 0; i < UnitPhoto.MaxPerUnit; i++)
        {
            Validate(await With<IUnitPhotos, Result>(s => s.AddAsync(room, image, null, Ct)));
        }

        Assert.False((await With<IUnitPhotos, Result>(s => s.AddAsync(room, image, null, Ct))).Succeeded);
    }

    [Fact]
    public async Task Deleting_a_room_deletes_its_photos()
    {
        Validate(await With<IUnitPhotos, Result>(s => s.AddAsync(room, ImageAsync().Result, null, Ct)));

        Validate(await With<IPropertyService, Result>(s => s.DeleteRoomAsync(room, Ct)));

        Assert.Empty(await With<IUnitPhotos, IReadOnlyList<UnitPhotoRow>>(s => s.ListAsync(room, Ct)));
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
}
