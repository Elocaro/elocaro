using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Elocaro.Application.Accounts;
using Elocaro.Application.Admin;
using Elocaro.Application.Files;
using Elocaro.Application.Operations;
using Elocaro.Domain.Files;
using Elocaro.Infrastructure.Audit;
using Elocaro.Infrastructure.Identity;
using Elocaro.Infrastructure.Operations;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Operations;

public class DataExportTests(PostgresFixture postgres) : IAsyncLifetime
{
    private InfrastructureEnvironment environment = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => environment = await InfrastructureEnvironment.CreateAsync(postgres);

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Export_contains_every_table_as_Json_and_Csv_with_manifest()
    {
        await CreateLandlordAsync();

        using var zip = await ExportAsync();

        Assert.NotNull(zip.GetEntry("LIESMICH.txt"));
        var manifest = Json(zip, "manifest.json");
        Assert.Equal("Elocaro-Vollexport", manifest.GetProperty("format").GetString());
        var tables = manifest.GetProperty("tables").EnumerateArray().Select(t => t.GetProperty("name").GetString()!).ToList();
        Assert.Contains("landlord_profiles", tables);
        Assert.Contains("users", tables);
        Assert.Contains("audit_entries", tables);
        foreach (var table in tables)
        {
            Assert.NotNull(zip.GetEntry($"data/{table}.json"));
            Assert.NotNull(zip.GetEntry($"data/{table}.csv"));
        }

        var landlord = Json(zip, "data/landlord_profiles.json").EnumerateArray().Single();
        Assert.Equal("Familie Müller-Lüdenscheidt", landlord.GetProperty("name").GetString());
        Assert.Equal("DE02 1203 0000 0000 2020 51", landlord.GetProperty("iban").GetString());
        Assert.False(landlord.TryGetProperty("xmin", out _));
        Assert.Equal(1, manifest.GetProperty("tables").EnumerateArray().Single(t => t.GetProperty("name").GetString() == "landlord_profiles").GetProperty("rows").GetInt32());
    }

    [Fact]
    public async Task Csv_is_readable_by_German_Excel()
    {
        await CreateLandlordAsync();

        using var zip = await ExportAsync();

        var bytes = Bytes(zip, "data/landlord_profiles.csv");
        Assert.Equal(Encoding.UTF8.Preamble.ToArray(), bytes[..3]);
        var lines = Encoding.UTF8.GetString(bytes[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        var header = lines[0].Split(';');
        var values = lines[1].Split(';');
        Assert.Equal("Familie Müller-Lüdenscheidt", values[Array.IndexOf(header, "name")]);
        Assert.Equal("nein", values[Array.IndexOf(header, "deleted")]);
        Assert.Matches(@"^2026-03-01 10:30:00$", values[Array.IndexOf(header, "created_at")]);
    }

    [Fact]
    public async Task Secrets_are_not_included()
    {
        await using (var scope = environment.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAccountService>().CreateFirstAdminAsync(scope.ServiceProvider.GetRequiredService<ISetupCode>().Value, "admin@example.de", "Admin", "Langes-Passwort-2026", Ct);
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var admin = await userManager.FindByEmailAsync("admin@example.de");
            await userManager.ResetAuthenticatorKeyAsync(admin!);
            var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();
            Assert.True(await db.Set<IdentityUserToken<Guid>>().AnyAsync(Ct));
        }

        using var zip = await ExportAsync();

        var user = Json(zip, "data/users.json").EnumerateArray().Single();
        Assert.Equal("admin@example.de", user.GetProperty("email").GetString());
        Assert.False(user.TryGetProperty("password_hash", out _));
        Assert.False(user.TryGetProperty("security_stamp", out _));
        Assert.False(user.TryGetProperty("concurrency_stamp", out _));
        Assert.Null(zip.GetEntry("data/user_tokens.json"));
        Assert.Null(zip.GetEntry("data/data_protection_keys.json"));
        Assert.Null(zip.GetEntry("data/magic_links.json"));
        Assert.DoesNotContain(zip.Entries, e => Encoding.UTF8.GetString(Bytes(zip, e.FullName)).Contains("AQAAAA", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Mail_links_are_not_in_the_export_while_the_proof_of_delivery_is()
    {
        await using (var scope = environment.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
            await accounts.InviteAsync("verwalter@example.de", "Verwalter", Roles.Manager, Ct);
            await accounts.InviteAsync("mieter@example.de", "Mieter", Roles.Tenant, Ct);
        }

        using var zip = await ExportAsync();

        var mails = Json(zip, "data/outgoing_mails.json").EnumerateArray().ToList();
        Assert.Equal(2, mails.Count);
        Assert.All(mails, m =>
        {
            Assert.False(m.TryGetProperty("html", out _));
            Assert.False(m.TryGetProperty("text", out _));
            Assert.True(m.TryGetProperty("subject", out _));
            Assert.True(m.TryGetProperty("to", out _));
            Assert.True(m.TryGetProperty("status", out _));
        });
        foreach (var entry in new[] { "data/outgoing_mails.json", "data/outgoing_mails.csv" })
        {
            var content = Encoding.UTF8.GetString(Bytes(zip, entry));
            Assert.DoesNotContain("konto/einladung", content, StringComparison.Ordinal);
            Assert.DoesNotContain("konto/link", content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Every_table_is_exported_or_deliberately_excluded()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();

        var all = db.Model.GetRelationalModel().Tables.Select(t => t.Name).ToHashSet();
        var exported = DataExport.ExportTables(db.Model).Select(t => t.Name).ToHashSet();

        // New tables are included in the export automatically. Anyone who excludes one ([NoExport]) must list it here.
        Assert.Equal(["data_protection_keys", "magic_links", "user_tokens"], all.Except(exported).Order());
    }

    [Fact]
    public async Task Files_are_in_the_export_with_original_name_including_deleted_ones()
    {
        var content = TestFiles.Pdf();
        Guid id, deletedId;
        await using (var scope = environment.Services.CreateAsyncScope())
        {
            var fileStore = scope.ServiceProvider.GetRequiredService<IFileStore>();
            id = (await fileStore.SaveAsync(new MemoryStream(content), "Rechnung Stadtwerke März.pdf", Ct)).Id;
            var deleted = await fileStore.SaveAsync(new MemoryStream(TestFiles.Pdf("zweite")), "photo.pdf", Ct);
            deletedId = deleted.Id;
            var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();
            db.Remove(deleted);
            await db.SaveChangesAsync(Ct);
        }

        using var zip = await ExportAsync();

        Assert.Equal(content, Bytes(zip, $"files/{id}/Rechnung Stadtwerke März.pdf"));
        Assert.NotNull(zip.GetEntry($"files/{deletedId}/photo.pdf"));
        var line = Json(zip, "data/stored_files.json").EnumerateArray().Single(d => d.GetProperty("id").GetGuid() == deletedId);
        Assert.True(line.GetProperty("deleted").GetBoolean());
        Assert.Equal(2, Json(zip, "manifest.json").GetProperty("stored_files").GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Missing_file_contents_are_listed_in_the_manifest()
    {
        StoredFile file;
        await using (var scope = environment.Services.CreateAsyncScope())
        {
            file = await scope.ServiceProvider.GetRequiredService<IFileStore>().SaveAsync(new MemoryStream(TestFiles.Pdf()), "weg.pdf", Ct);
        }

        File.Delete(Path.Combine(environment.FilePath, file.Sha256[..2], file.Sha256));
        using var zip = await ExportAsync();

        var files = Json(zip, "manifest.json").GetProperty("stored_files");
        Assert.Equal(0, files.GetProperty("count").GetInt32());
        Assert.Equal(file.Id, files.GetProperty("missing").EnumerateArray().Single().GetGuid());
    }

    [Fact]
    public async Task Export_is_recorded_in_the_audit_log()
    {
        await ExportAsync();

        await using var scope = environment.Services.CreateAsyncScope();
        var entry = await scope.ServiceProvider.GetRequiredService<ElocaroDbContext>().Set<AuditEntry>().SingleAsync(a => a.EntityType == "Datenexport", Ct);
        Assert.Equal(Application.Audit.AuditAction.Read, entry.Action);
    }

    [Fact]
    public async Task File_name_contains_the_German_date()
    {
        environment.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 18, 23, 30, 0, TimeSpan.Zero));
        await using var scope = environment.Services.CreateAsyncScope();

        Assert.Equal("elocaro-export-2026-10-19.zip", scope.ServiceProvider.GetRequiredService<IDataExport>().FileName());
    }

    [Theory]
    [InlineData("einfach", "einfach")]
    [InlineData("a;b", "\"a;b\"")]
    [InlineData("Sie sagte \"Hallo\"", "\"Sie sagte \"\"Hallo\"\"\"")]
    [InlineData("zwei\nZeilen", "\"zwei\nZeilen\"")]
    public void Csv_fields_are_quoted_when_needed(string value, string expected) => Assert.Equal(expected, DataExport.CsvField(value));

    [Theory]
    [InlineData("=HYPERLINK(\"http://x\")", "'=HYPERLINK(\"http://x\")")]
    [InlineData("+49 123", "'+49 123")]
    [InlineData("@SUMME(A1)", "'@SUMME(A1)")]
    [InlineData("Müller", "Müller")]
    public void Csv_prevents_formula_injection(string value, string expected) => Assert.Equal(expected, DataExport.CsvValue(value, "text"));

    [Fact]
    public void Csv_formats_amounts_with_decimal_comma() => Assert.Equal("650,50", DataExport.CsvValue(650.50m, "numeric(12,2)"));

    private async Task CreateLandlordAsync()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ILandlordProfileService>().SaveAsync(
            new LandlordProfileInput { Name = "Familie Müller-Lüdenscheidt", Street = "Hauptstraße 1", PostalCode = "12345", City = "Köln", Iban = "DE02120300000000202051" }, Ct);
    }

    private async Task<ZipArchive> ExportAsync()
    {
        var buffer = new MemoryStream();
        await using (var scope = environment.Services.CreateAsyncScope())
        {
            // Like the response body: not seekable, only asynchronously writable.
            await using var destination = new AsyncWriteOnlyStream(buffer);
            await scope.ServiceProvider.GetRequiredService<IDataExport>().ExportAsync(destination, Ct);
        }

        buffer.Position = 0;
        return new ZipArchive(buffer, ZipArchiveMode.Read);
    }

    private static JsonElement Json(ZipArchive zip, string name) => JsonDocument.Parse(Bytes(zip, name)).RootElement;

    private static byte[] Bytes(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name) ?? throw new InvalidOperationException($"{name} fehlt im Export.");
        using var stream = entry.Open();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private sealed class AsyncWriteOnlyStream(Stream inner) : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() => throw new InvalidOperationException("Synchrones Flush ist wie in Kestrel nicht erlaubt.");

        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Synchrones Schreiben ist wie in Kestrel nicht erlaubt.");

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => inner.WriteAsync(buffer, cancellationToken);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => inner.WriteAsync(buffer, offset, count, cancellationToken);
    }
}
