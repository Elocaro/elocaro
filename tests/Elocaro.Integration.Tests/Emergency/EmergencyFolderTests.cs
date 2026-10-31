using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Elocaro.Application.Accounts;
using Elocaro.Application.Admin;
using Elocaro.Application.Audit;
using Elocaro.Application.Common;
using Elocaro.Application.Emergency;
using Elocaro.Application.Finance;
using Elocaro.Application.Leases;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Application.Todos;
using Elocaro.Domain.Admin;
using Elocaro.Domain.Bank;
using Elocaro.Infrastructure.Audit;
using Elocaro.Infrastructure.Emergency;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Emergency;

public partial class EmergencyFolderTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Password = "Das-ist-ein-langes-Passwort-2026";

    private static readonly ClaimsPrincipal Admin = new(new ClaimsIdentity([new Claim(ClaimTypes.Role, Roles.Admin)], "test"));

    private InfrastructureEnvironment environment = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => environment = await InfrastructureEnvironment.CreateAsync(postgres, new Dictionary<string, string?> { ["Application:BaseUrl"] = "https://elocaro.example.de/" });

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public void Content_security_policy_hash_matches_the_script_as_the_browser_reads_it()
    {
        var page = UnlockPage.Template(new DateOnly(2026, 3, 20));

        var script = page[(page.LastIndexOf("<script>", StringComparison.Ordinal) + "<script>".Length)..page.LastIndexOf("</script>", StringComparison.Ordinal)];
        Assert.DoesNotContain('\r', script); // browsers normalise line breaks before hashing
        Assert.Contains($"'sha256-{Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(script)))}'", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Created_folder_decrypts_with_the_password_and_contains_the_business_data()
    {
        await SeedAsync();
        await SaveNotesAsync(new EmergencyFolderNotes { ImportantContacts = "Steuerberaterin Frau Lange, 0221 123456", DocumentLocation = "Ordner im Schrank im Büro" });
        environment.Clock.SetUtcNow(new DateTimeOffset(2026, 3, 20, 10, 0, 0, TimeSpan.Zero));
        await WithAsync<ITenantAccounts, int>(a => a.GenerateChargesAsync(null, Ct));

        var result = await CreateAsync(Password);

        Assert.True(result.Succeeded);
        Assert.Equal("elocaro-notfallmappe-2026-03-20.html", result.FileName);
        var content = Decrypt(result.Content!, Password);
        var html = content.GetProperty("html").GetString()!;
        Assert.Contains("Erste Schritte", html, StringComparison.Ordinal);
        Assert.Contains("keine Rechtsberatung", html, StringComparison.Ordinal);
        Assert.Contains("Steuerberaterin Frau Lange", html, StringComparison.Ordinal);
        Assert.Contains("Ordner im Schrank im Büro", html, StringComparison.Ordinal);
        Assert.Contains("Lindenweg 3", html, StringComparison.Ordinal);
        Assert.Contains("Wohnung 1", html, StringComparison.Ordinal);
        Assert.Contains("Anna Schmidt", html, StringComparison.Ordinal);
        Assert.Contains("anna@example.de", html, StringComparison.Ordinal);
        Assert.Contains("770,00 €", html, StringComparison.Ordinal); // monthly rent and open item
        Assert.Contains("DE02120300000000202051", html, StringComparison.Ordinal); // bank account
        Assert.Contains("https://elocaro.example.de/", html, StringComparison.Ordinal);
        Assert.Contains("Max Mustermann", html, StringComparison.Ordinal);
        Assert.Empty(content.GetProperty("files").EnumerateArray());
    }

    [Fact]
    public async Task The_file_states_its_parameters_in_plain_text_and_meets_the_minimum()
    {
        var result = await CreateAsync(Password);

        var parameters = Parameters(result.Content!);
        Assert.Equal("AES-256-GCM", parameters.GetProperty("algorithm").GetString());
        Assert.Equal("PBKDF2-SHA256", parameters.GetProperty("kdf").GetString());
        Assert.True(parameters.GetProperty("iterations").GetInt32() >= 600_000);
        Assert.Equal(16, Convert.FromBase64String(parameters.GetProperty("salt").GetString()!).Length);
        Assert.Equal(12, Convert.FromBase64String(parameters.GetProperty("nonce").GetString()!).Length);
        var text = Encoding.UTF8.GetString(result.Content!);
        Assert.DoesNotContain("<script src", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<link", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http://", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Content-Security-Policy", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Wrong_password_cannot_decrypt()
    {
        var result = await CreateAsync(Password);

        Assert.ThrowsAny<CryptographicException>(() => Decrypt(result.Content!, Password + "x"));
        Assert.ThrowsAny<CryptographicException>(() => Decrypt(result.Content!, "ganz-anderes-Passwort"));
    }

    [Fact]
    public async Task Every_folder_gets_a_fresh_salt_and_nonce()
    {
        var first = Parameters((await CreateAsync(Password)).Content!);
        var second = Parameters((await CreateAsync(Password)).Content!);

        Assert.NotEqual(first.GetProperty("salt").GetString(), second.GetProperty("salt").GetString());
        Assert.NotEqual(first.GetProperty("nonce").GetString(), second.GetProperty("nonce").GetString());
        Assert.NotEqual(first.GetProperty("ciphertext").GetString(), second.GetProperty("ciphertext").GetString());
    }

    [Fact]
    public async Task The_encrypted_file_shows_no_business_data()
    {
        await SeedAsync();

        var text = Encoding.UTF8.GetString((await CreateAsync(Password)).Content!);

        Assert.DoesNotContain("Schmidt", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Lindenweg", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Weak_or_different_passwords_are_refused_without_a_file()
    {
        var tooShort = await CreateAsync("elf-zeichen");
        var different = await CreateAsync(Password, Password + "!");

        Assert.Equal(EmergencyFolderProblem.PasswordTooShort, tooShort.Problem);
        Assert.Equal(EmergencyFolderProblem.PasswordsDiffer, different.Problem);
        Assert.Null(tooShort.Content);
        Assert.Null(different.Content);
        Assert.Empty(await AuditAsync());
    }

    [Fact]
    public async Task Creation_is_audited_without_password_and_remembers_the_date()
    {
        environment.Clock.SetUtcNow(new DateTimeOffset(2026, 4, 2, 8, 0, 0, TimeSpan.Zero));

        await CreateAsync(Password);

        var entry = Assert.Single(await AuditAsync());
        Assert.Equal(AuditAction.Read, entry.Action);
        Assert.Equal("Notfallmappe", entry.EntityType);
        Assert.Equal(new DateTimeOffset(2026, 4, 2, 8, 0, 0, TimeSpan.Zero), (await State()).LastCreatedAt);

        await using var scope = environment.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();
        var everything = JsonSerializer.Serialize(await db.Set<AuditEntry>().ToListAsync(Ct)) + JsonSerializer.Serialize(await db.Set<EmergencyFolder>().ToListAsync(Ct));
        Assert.DoesNotContain(Password, everything, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Secrets_never_reach_the_folder()
    {
        await SeedAsync();
        await using (var scope = environment.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAccountService>().CreateFirstAdminAsync("admin@example.de", "Admin", "Geheimes-Anmelde-Passwort-2026", Ct);
            var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();
            var account = new BankAccount("Online-Konto", "DE89370400440532013000");
            account.SetFinTs("https://fints.example.de/", "12345678", "FINTS-GEHEIME-KENNUNG");
            db.Add(account);
            await db.SaveChangesAsync(Ct);
        }

        var passwordHash = await ScalarAsync("SELECT password_hash FROM users LIMIT 1");
        var html = Decrypt((await CreateAsync(Password)).Content!, Password).GetProperty("html").GetString()!;

        Assert.False(string.IsNullOrEmpty(passwordHash));
        Assert.Contains("Online-Konto", html, StringComparison.Ordinal);
        Assert.DoesNotContain(passwordHash, html, StringComparison.Ordinal);
        Assert.DoesNotContain("Geheimes-Anmelde-Passwort", html, StringComparison.Ordinal);
        Assert.DoesNotContain("FINTS-GEHEIME-KENNUNG", html, StringComparison.Ordinal);
        Assert.DoesNotContain("fints.example.de", html, StringComparison.Ordinal);
        Assert.DoesNotContain("12345678", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Names_and_free_texts_are_html_encoded()
    {
        await using (var scope = environment.Services.CreateAsyncScope())
        {
            var properties = scope.ServiceProvider.GetRequiredService<IPropertyService>();
            var house = Validate(await properties.CreateAsync(new PropertyInput { Name = "<b onmouseover=alert(1)>Haus</b>", Street = "Weg 1", PostalCode = "50667", City = "Köln" }, Ct));
            var unit = Validate(await properties.CreateUnitAsync(house, new UnitInput { Name = "<script>alert('x')</script>" }, Ct));
            var tenant = Validate(await scope.ServiceProvider.GetRequiredService<IPersonService>().CreateAsync(
                new PersonInput { FirstName = "<img src=x onerror=alert(1)>", LastName = "O'Brien & \"Söhne\"", Salutation = Elocaro.Domain.People.Salutation.Mr }, null, Ct));
            Validate(await scope.ServiceProvider.GetRequiredService<ILeaseService>().CreateAsync(
                new LeaseCreation { RentalUnitId = unit, Start = new DateOnly(2026, 1, 1), TenantIds = [tenant], BaseRent = 500m }, Ct));
        }

        await SaveNotesAsync(new EmergencyFolderNotes { FurtherNotes = "</iframe><script>alert('Notiz')</script><a href=\"javascript:alert(1)\">x</a>" });

        var html = Decrypt((await CreateAsync(Password)).Content!, Password).GetProperty("html").GetString()!;

        // Only the markup of the folder itself is left: its own style element and no script, frame or injected element.
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<iframe", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<b ", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<a href", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;alert(&#x27;x&#x27;)&lt;/script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", html, StringComparison.Ordinal);
        Assert.Contains("O&#x27;Brien &amp; &quot;Söhne&quot;", html, StringComparison.Ordinal);
        Assert.Contains("&lt;/iframe&gt;&lt;script&gt;", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Notes_are_saved_trimmed_and_too_long_ones_are_refused()
    {
        var saved = await WithAsync<IEmergencyFolder, EmergencyFolderOutcome>(f => f.SaveNotesAsync(new EmergencyFolderNotes { ImportantContacts = "  Anna  ", FurtherNotes = "   " }, Ct));
        var state = await State();

        Assert.True(saved.Succeeded);
        Assert.Equal("Anna", state.Notes.ImportantContacts);
        Assert.Null(state.Notes.FurtherNotes);

        var refused = await WithAsync<IEmergencyFolder, EmergencyFolderOutcome>(f => f.SaveNotesAsync(new EmergencyFolderNotes { ImportantContacts = new string('x', 4001) }, Ct));
        Assert.Equal(EmergencyFolderProblem.NotesTooLong, refused.Problem);
        Assert.Equal("Anna", (await State()).Notes.ImportantContacts);
    }

    [Fact]
    public async Task Nothing_is_written_to_the_file_store()
    {
        await SeedAsync();
        var before = Directory.Exists(environment.FilePath) ? Directory.GetFiles(environment.FilePath, "*", SearchOption.AllDirectories).Length : 0;

        await CreateAsync(Password);

        var after = Directory.Exists(environment.FilePath) ? Directory.GetFiles(environment.FilePath, "*", SearchOption.AllDirectories).Length : 0;
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Reminder_appears_once_there_is_something_to_hand_over_and_after_twelve_months()
    {
        Assert.Empty(await TodosAsync(Admin)); // empty installation: nothing to hand over yet

        await SeedAsync();
        var never = Assert.Single(await TodosAsync(Admin));
        Assert.Equal("Notfallmappe erstellen", never.Title);
        Assert.Equal("/verwaltung/system/notfallmappe", never.Link);

        await CreateAsync(Password);
        Assert.Empty(await TodosAsync(Admin));

        environment.Clock.Advance(TimeSpan.FromDays(364));
        Assert.Empty(await TodosAsync(Admin));

        environment.Clock.Advance(TimeSpan.FromDays(3));
        var old = Assert.Single(await TodosAsync(Admin));
        Assert.Equal("Notfallmappe erneuern", old.Title);
        Assert.Equal(Urgency.Soon, old.Urgency);
    }

    [Fact]
    public async Task Reminder_is_only_for_accounts_with_the_system_permission()
    {
        await SeedAsync();
        var manager = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, Roles.Manager), new Claim(Permissions.ClaimType, nameof(AccessRight.Finance))], "test"));
        var system = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, Roles.Manager), new Claim(Permissions.ClaimType, nameof(AccessRight.System))], "test"));

        Assert.Empty(await TodosAsync(manager));
        Assert.Single(await TodosAsync(system));
    }

    [Fact]
    public async Task The_new_table_is_part_of_the_full_export()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();

        Assert.Contains("emergency_folders", Elocaro.Infrastructure.Operations.DataExport.ExportTables(db.Model).Select(t => t.Name));
    }

    // ---- Helpers --------------------------------------------------------------------------------------------------

    /// <summary>Independent of the production code: parses the file like the browser does and decrypts with the parameters from it.</summary>
    internal static JsonElement Decrypt(byte[] file, string password)
    {
        var parameters = Parameters(file);
        var salt = Convert.FromBase64String(parameters.GetProperty("salt").GetString()!);
        var nonce = Convert.FromBase64String(parameters.GetProperty("nonce").GetString()!);
        var sealedData = Convert.FromBase64String(parameters.GetProperty("ciphertext").GetString()!);
        var key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password.Normalize(NormalizationForm.FormC)), salt, parameters.GetProperty("iterations").GetInt32(), HashAlgorithmName.SHA256, 32);
        var plain = new byte[sealedData.Length - 16];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(nonce, sealedData.AsSpan(0, plain.Length), sealedData.AsSpan(plain.Length, 16), plain, Encoding.ASCII.GetBytes(parameters.GetProperty("aad").GetString()!));
        return JsonDocument.Parse(plain).RootElement.Clone();
    }

    internal static JsonElement Parameters(byte[] file)
    {
        var match = DataBlock().Match(Encoding.UTF8.GetString(file));
        Assert.True(match.Success, "Datenblock fehlt");
        return JsonDocument.Parse(match.Groups[1].Value).RootElement.Clone();
    }

    [GeneratedRegex("""<script type="application/json" id="mappe-daten">(.*?)</script>""", RegexOptions.Singleline)]
    private static partial Regex DataBlock();

    private async Task<EmergencyFolderResult> CreateAsync(string password, string? confirmation = null) =>
        await WithAsync<IEmergencyFolder, EmergencyFolderResult>(f => f.CreateAsync(new EmergencyFolderRequest(password, confirmation ?? password, false), Admin, Ct));

    private async Task SaveNotesAsync(EmergencyFolderNotes notes) =>
        Assert.True((await WithAsync<IEmergencyFolder, EmergencyFolderOutcome>(f => f.SaveNotesAsync(notes, Ct))).Succeeded);

    private async Task<EmergencyFolderState> State() => await WithAsync<IEmergencyFolder, EmergencyFolderState>(f => f.StateAsync(Ct));

    private async Task<List<AuditEntry>> AuditAsync()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ElocaroDbContext>().Set<AuditEntry>().Where(a => a.EntityType == "Notfallmappe").ToListAsync(Ct);
    }

    private async Task<IReadOnlyList<TodoItem>> TodosAsync(ClaimsPrincipal user)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var source = scope.ServiceProvider.GetServices<ITodoSource>().Single(s => s is EmergencyFolderTodos);
        return await source.TodosAsync(user, Ct);
    }

    private async Task<string?> ScalarAsync(string sql)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();
        await db.Database.OpenConnectionAsync(Ct);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        return (await command.ExecuteScalarAsync(Ct))?.ToString();
    }

    private async Task SeedAsync()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var properties = scope.ServiceProvider.GetRequiredService<IPropertyService>();
        var house = Validate(await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct));
        var apartment = Validate(await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 1" }, Ct));
        var anna = Validate(await scope.ServiceProvider.GetRequiredService<IPersonService>().CreateAsync(
            new PersonInput { FirstName = "Anna", LastName = "Schmidt", Salutation = Elocaro.Domain.People.Salutation.Mrs, Email = "anna@example.de" }, null, Ct));
        Validate(await scope.ServiceProvider.GetRequiredService<ILeaseService>().CreateAsync(new LeaseCreation
        {
            RentalUnitId = apartment,
            Start = new DateOnly(2026, 3, 1),
            TenantIds = [anna],
            BaseRent = 620m,
            OperatingCostPrepayment = 150m,
            Data = new LeaseInput { Deposit = 1860m },
        }, Ct));

        var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();
        db.Add(new LandlordProfile("Max Mustermann", "Musterweg 1", "12345", "Musterstadt") { Email = "vermieter@example.de" });
        db.Add(new BankAccount("Mietkonto", "DE02120300000000202051") { Bank = "Musterbank" });
        await db.SaveChangesAsync(Ct);
        await scope.ServiceProvider.GetRequiredService<IFinanceSettings>().SaveAsync(new FinanceSettingsInput { AccountingStart = new DateOnly(2026, 3, 1) }, Ct);
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
}
