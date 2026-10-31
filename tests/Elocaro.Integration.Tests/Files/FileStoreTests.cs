using Elocaro.Application.Files;
using Elocaro.Domain.Files;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace Elocaro.Integration.Tests.Files;

public class FileStoreTests(PostgresFixture postgres) : IAsyncLifetime
{
    private InfrastructureEnvironment environment = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() =>
        environment = await InfrastructureEnvironment.CreateAsync(postgres, new Dictionary<string, string?>
        {
            ["FileStore:MaxSizeMb"] = "1",
            ["FileStore:MaxImageEdge"] = "100",
        });

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Pdf_is_saved_and_can_be_read()
    {
        var content = TestFiles.Pdf();

        var file = await SaveAsync(content, "Rechnung Stadtwerke.pdf");

        Assert.Equal("application/pdf", file.MimeType);
        Assert.Equal("Rechnung Stadtwerke.pdf", file.Name);
        Assert.Equal(content.Length, file.Size);
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(content)), file.Sha256);
        Assert.True(File.Exists(Path.Combine(environment.FilePath, file.Sha256[..2], file.Sha256)));

        Assert.Equal(content, await ReadAsync(file.Id));
    }

    [Fact]
    public async Task Unknown_file_returns_null()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var fileStore = scope.ServiceProvider.GetRequiredService<IFileStore>();

        Assert.Null(await fileStore.OpenAsync(Guid.CreateVersion7(), Ct));
    }

    [Theory]
    [InlineData(new byte[] { 0x4D, 0x5A, 0x90, 0x00 }, "programm.pdf")] // Windows EXE disguised as PDF
    [InlineData(new byte[] { 0x3C, 0x68, 0x74, 0x6D, 0x6C, 0x3E }, "page.html")]
    public async Task Disallowed_type_is_detected_by_content_and_rejected(byte[] content, string name)
    {
        var errors = await Assert.ThrowsAsync<FileRejectedException>(() => SaveAsync(content, name));

        Assert.Contains("nicht erlaubt", errors.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(environment.FilePath) && Directory.EnumerateFiles(environment.FilePath, "*", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task Empty_file_is_rejected()
    {
        await Assert.ThrowsAsync<FileRejectedException>(() => SaveAsync([], "empty.pdf"));
    }

    [Fact]
    public async Task Too_large_file_is_rejected()
    {
        var large = new byte[(1024 * 1024) + 1];
        TestFiles.Pdf().CopyTo(large, 0);

        var errors = await Assert.ThrowsAsync<FileRejectedException>(() => SaveAsync(large, "large.pdf"));
        Assert.Contains("1 MB", errors.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Identical_content_is_stored_only_once()
    {
        var content = TestFiles.Pdf("doppelt");

        var first = await SaveAsync(content, "a.pdf");
        var second = await SaveAsync(content, "b.pdf");

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(first.Sha256, second.Sha256);
        Assert.Single(Directory.EnumerateFiles(environment.FilePath, "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("../../etc/passwd.pdf", "passwd.pdf")]
    [InlineData("C:\\Users\\x\\Beleg.pdf", "Beleg.pdf")]
    [InlineData("", "datei.pdf")]
    [InlineData("rechnung.exe", "rechnung.pdf")]
    public async Task File_name_is_sanitized_and_extension_follows_content(string name, string expected)
    {
        var file = await SaveAsync(TestFiles.Pdf(name), name);

        Assert.Equal(expected, file.Name);
    }

    [Fact]
    public async Task Large_photo_is_downsized_and_stored_without_metadata()
    {
        var jpeg = TestFiles.WithExifOrientation(TestFiles.TwoColorImage(400, 200), 1);

        var file = await SaveAsync(jpeg, "photo.png");
        var saved = await ReadAsync(file.Id);

        Assert.Equal("image/jpeg", file.MimeType);
        Assert.Equal("photo.jpg", file.Name);
        Assert.Equal((100, 50), TestFiles.Size(saved));
        Assert.DoesNotContain("Exif", System.Text.Encoding.ASCII.GetString(saved), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Image_with_huge_dimensions_is_rejected_before_decoding()
    {
        var bomb = TestFiles.PngWithDimensions(12000, 12000);

        var error = await Assert.ThrowsAsync<FileRejectedException>(() => SaveAsync(bomb, "bombe.png"));

        Assert.True(bomb.Length < 100);
        Assert.Contains("höchstens 50 Megapixel", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Megapixel_limit_is_configurable_and_includes_the_limit_itself()
    {
        await using var limited = await InfrastructureEnvironment.CreateAsync(postgres, new Dictionary<string, string?> { ["FileStore:MaxImageMegapixels"] = "1" });
        await using var scope = limited.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IFileStore>();

        var allowed = TestFiles.TwoColorImage(1000, 1000, SKEncodedImageFormat.Png);
        await using (var content = new MemoryStream(allowed))
        {
            Assert.Equal("image/png", (await store.SaveAsync(content, "erlaubt.png", Ct)).MimeType);
        }

        await using var tooLarge = new MemoryStream(TestFiles.TwoColorImage(1001, 1000, SKEncodedImageFormat.Png));
        await Assert.ThrowsAsync<FileRejectedException>(() => store.SaveAsync(tooLarge, "zu-gross.png", Ct));
    }

    [Fact]
    public async Task Png_stays_Png()
    {
        var png = TestFiles.TwoColorImage(40, 20, SKEncodedImageFormat.Png);

        var file = await SaveAsync(png, "screenshot.png");

        Assert.Equal("image/png", file.MimeType);
        Assert.Equal((40, 20), TestFiles.Size(await ReadAsync(file.Id)));
    }

    /// <summary>
    /// Source image 80×40: red on the left, blue on the right. Expected is what an image viewer shows after applying the
    /// EXIF orientation (checked via the color at the top left or top/bottom).
    /// </summary>
    [Theory]
    [InlineData(1, 80, 40, "rot-links")]
    [InlineData(2, 80, 40, "blau-links")]  // mirrored
    [InlineData(3, 80, 40, "blau-links")]  // 180°
    [InlineData(4, 80, 40, "rot-links")]   // flipped vertically: left stays red
    [InlineData(5, 40, 80, "rot-oben")]    // transposed
    [InlineData(6, 40, 80, "rot-oben")]    // 90° clockwise: left edge becomes top
    [InlineData(7, 40, 80, "blau-oben")]   // transversed
    [InlineData(8, 40, 80, "blau-oben")]   // 90° counterclockwise: left edge becomes bottom
    public async Task Exif_orientation_is_applied(ushort orientation, int width, int height, string expected)
    {
        var jpeg = TestFiles.WithExifOrientation(TestFiles.TwoColorImage(80, 40), orientation);

        var file = await SaveAsync(jpeg, "photo.jpg");
        var image = await ReadAsync(file.Id);

        Assert.Equal((width, height), TestFiles.Size(image));
        var (x, y) = expected.EndsWith("links", StringComparison.Ordinal) ? (0.1f, 0.5f) : (0.5f, 0.1f);
        var color = TestFiles.Color(image, x, y);
        Assert.True(expected.StartsWith("rot", StringComparison.Ordinal) ? TestFiles.IsRed(color) : TestFiles.IsBlue(color), $"Farbe {color} passt nicht zu '{expected}'.");
    }

    [Fact]
    public async Task Corrupt_image_is_rejected()
    {
        byte[] corrupt = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x02];

        await Assert.ThrowsAsync<FileRejectedException>(() => SaveAsync(corrupt, "corrupt.jpg"));
    }

    [Fact]
    public async Task Metadata_is_stored_in_the_database()
    {
        var file = await SaveAsync(TestFiles.Pdf(), "receipt.pdf");

        await using var scope = environment.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();
        var read = await db.Set<StoredFile>().SingleAsync(d => d.Id == file.Id, Ct);
        Assert.Equal(file.Sha256, read.Sha256);
        Assert.Equal(InfrastructureEnvironment.StartTime, read.CreatedAt);
    }

    private async Task<StoredFile> SaveAsync(byte[] content, string name)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var fileStore = scope.ServiceProvider.GetRequiredService<IFileStore>();
        using var stream = new MemoryStream(content);
        return await fileStore.SaveAsync(stream, name, Ct);
    }

    private async Task<byte[]> ReadAsync(Guid id)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var fileStore = scope.ServiceProvider.GetRequiredService<IFileStore>();
        await using var opened = await fileStore.OpenAsync(id, Ct) ?? throw new InvalidOperationException("Datei fehlt.");
        using var buffer = new MemoryStream();
        await opened.Content.CopyToAsync(buffer, Ct);
        return buffer.ToArray();
    }
}
