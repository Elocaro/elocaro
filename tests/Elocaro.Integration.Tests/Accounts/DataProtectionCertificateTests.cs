using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Elocaro.Infrastructure;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elocaro.Integration.Tests.Accounts;

public class DataProtectionCertificateTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Password = "Test-Zertifikat-2026";
    private const string Purpose = "test";
    private const string EncryptedMarker = "encryptedSecret";

    private readonly string directory = Path.Combine(Path.GetTempPath(), "elocaro-tests", Guid.NewGuid().ToString("N"));
    private InfrastructureEnvironment environment = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string CertificatePath => Path.Combine(directory, "keys.pfx");

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(directory);
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Elocaro Test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        await File.WriteAllBytesAsync(CertificatePath, certificate.Export(X509ContentType.Pkcs12, Password), Ct);

        environment = await InfrastructureEnvironment.CreateAsync(postgres);
    }

    public async ValueTask DisposeAsync()
    {
        await environment.DisposeAsync();
        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public async Task Values_protected_without_a_certificate_stay_readable_after_adding_one_and_new_keys_are_encrypted()
    {
        var value = environment.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose).Protect("IBAN");
        Assert.DoesNotContain(EncryptedMarker, Assert.Single(await KeysAsync()), StringComparison.Ordinal);

        await using var withCertificate = Start(CertificatePath);

        Assert.Equal("IBAN", withCertificate.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose).Unprotect(value));
        Assert.DoesNotContain(EncryptedMarker, Assert.Single(await KeysAsync()), StringComparison.Ordinal);

        withCertificate.GetRequiredService<IKeyManager>().CreateNewKey(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(90));

        var keys = await KeysAsync();
        Assert.Equal(2, keys.Count);
        Assert.Single(keys, k => k.Contains(EncryptedMarker, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Keys_created_with_a_certificate_cannot_be_used_without_it()
    {
        string value;
        await using (var first = Start(CertificatePath))
        {
            value = first.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose).Protect("IBAN");
        }

        Assert.Contains(EncryptedMarker, Assert.Single(await KeysAsync()), StringComparison.Ordinal);

        await using (var restarted = Start(CertificatePath))
        {
            Assert.Equal("IBAN", restarted.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose).Unprotect(value));
        }

        await using var withoutCertificate = Start("");
        Assert.ThrowsAny<CryptographicException>(() => withoutCertificate.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose).Unprotect(value));
    }

    [Fact]
    public void Missing_file_stops_the_start_with_a_clear_message()
    {
        var path = Path.Combine(directory, "fehlt.pfx");

        var error = Assert.Throws<InvalidOperationException>(() => Start(path));

        Assert.Contains(path, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Wrong_password_stops_the_start_with_a_clear_message()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Start(CertificatePath, "falsch"));

        Assert.Contains("Passwort", error.Message, StringComparison.Ordinal);
    }

    private async Task<List<string>> KeysAsync()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var keys = await scope.ServiceProvider.GetRequiredService<ElocaroDbContext>().DataProtectionKeys.AsNoTracking().ToListAsync(Ct);
        return keys.Select(k => k.Xml!).ToList();
    }

    /// <summary>A new start of the application on the same database.</summary>
    private ServiceProvider Start(string certificatePath, string password = Password)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(TimeProvider.System);
        services.AddElocaroInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Elocaro"] = environment.Connection,
            ["Jobs:Active"] = "false",
            ["FileStore:Path"] = environment.FilePath,
            ["DataProtection:CertificatePath"] = certificatePath,
            ["DataProtection:CertificatePassword"] = password,
        }).Build());
        return services.BuildServiceProvider();
    }
}
