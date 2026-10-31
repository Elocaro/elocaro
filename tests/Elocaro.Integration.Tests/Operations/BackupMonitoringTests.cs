using System.Security.Claims;
using Elocaro.Application.Accounts;
using Elocaro.Application.Operations;
using Elocaro.Application.Todos;
using Elocaro.Domain.Communication;
using Elocaro.Infrastructure.Operations;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Elocaro.Integration.Tests.Operations;

public class BackupMonitoringTests(PostgresFixture postgres) : IAsyncLifetime
{
    private InfrastructureEnvironment environment = null!;
    private string statusFile = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        statusFile = Path.Combine(Path.GetTempPath(), "elocaro-tests", Guid.NewGuid().ToString("N"), "backup.json");
        Directory.CreateDirectory(Path.GetDirectoryName(statusFile)!);
        environment = await InfrastructureEnvironment.CreateAsync(postgres, new Dictionary<string, string?>
        {
            ["Backup:StatusFile"] = statusFile,
            ["Application:BaseUrl"] = "https://elocaro.example.de/",
        });
    }

    public async ValueTask DisposeAsync()
    {
        await environment.DisposeAsync();
        Directory.Delete(Path.GetDirectoryName(statusFile)!, recursive: true);
    }

    [Fact]
    public async Task Without_status_file_there_is_no_backup_yet()
    {
        var state = await StateAsync();

        Assert.Equal(BackupState.Unknown, state.State);
        Assert.True(state.NeedsAttention);
    }

    [Fact]
    public async Task Recent_successful_backup_is_ok()
    {
        // Format as written by deploy/backup/elocaro-backup.sh
        await File.WriteAllTextAsync(statusFile,
            """{"timestamp":"2026-03-01T01:30:05Z","succeeded":true,"message":"Sicherung erfolgreich","snapshot":"49ccbcd74357a447","durationSeconds":5}""", Ct);

        var state = await StateAsync();

        Assert.Equal(BackupState.Ok, state.State);
        Assert.Equal("Letzte Sicherung am 01.03.2026 02:30 erfolgreich.", state.Description);
        Assert.Equal("49ccbcd74357a447", state.Snapshot);
        Assert.Equal(5, state.DurationSeconds);
        Assert.False(state.NeedsAttention);
    }

    [Fact]
    public async Task Too_old_backup_is_stale()
    {
        await WriteStatusAsync(succeeded: true, InfrastructureEnvironment.StartTime.AddHours(-27));

        Assert.Equal(BackupState.Outdated, (await StateAsync()).State);
    }

    [Fact]
    public async Task Failed_backup_is_reported_with_message()
    {
        await WriteStatusAsync(succeeded: false, InfrastructureEnvironment.StartTime.AddHours(-7), "Fatal: unable to open repository");

        var state = await StateAsync();

        Assert.Equal(BackupState.Failed, state.State);
        Assert.Equal("Fatal: unable to open repository", state.Message);
    }

    [Fact]
    public async Task Unreadable_status_file_is_unknown()
    {
        await File.WriteAllTextAsync(statusFile, "{kaputt", Ct);

        Assert.Equal(BackupState.Unknown, (await StateAsync()).State);
    }

    [Fact]
    public async Task Without_configuration_monitoring_is_off()
    {
        await using var without = await InfrastructureEnvironment.CreateAsync(postgres);
        await using var scope = without.Services.CreateAsyncScope();

        var state = await scope.ServiceProvider.GetRequiredService<IBackupStatus>().QueryAsync(Ct);

        Assert.Equal(BackupState.NotConfigured, state.State);
        Assert.False(state.NeedsAttention);
    }

    [Fact]
    public async Task Health_check_reports_problems_as_degraded()
    {
        await WriteStatusAsync(succeeded: false, InfrastructureEnvironment.StartTime);
        await using var scope = environment.Services.CreateAsyncScope();
        var report = await scope.ServiceProvider.GetRequiredService<HealthCheckService>().CheckHealthAsync(c => c.Name == "backup", Ct);

        Assert.Equal(HealthStatus.Degraded, report.Entries["backup"].Status);
    }

    [Fact]
    public async Task Dashboard_shows_problem_only_with_system_permission()
    {
        await WriteStatusAsync(succeeded: false, InfrastructureEnvironment.StartTime);
        await using var scope = environment.Services.CreateAsyncScope();
        var source = scope.ServiceProvider.GetServices<ITodoSource>().OfType<BackupTodos>().Single();

        var admin = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, Roles.Admin)], "test"));
        var manager = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, Roles.Manager), new Claim(Permissions.ClaimType, nameof(AccessRight.Finance))], "test"));

        var todo = Assert.Single(await source.TodosAsync(admin, Ct));
        Assert.Equal(Urgency.Urgent, todo.Urgency);
        Assert.Equal("/verwaltung/system/datensicherung", todo.Link);
        Assert.Empty(await source.TodosAsync(manager, Ct));
    }

    [Fact]
    public async Task Warning_goes_to_all_active_admins()
    {
        await using (var scope = environment.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
            await accounts.CreateFirstAdminAsync("admin@example.de", "Admin", "Langes-Passwort-2026", Ct);
            var second = await accounts.InviteAsync("admin2@example.de", "Zweiter Admin", Roles.Admin, Ct);
            var locked = await accounts.InviteAsync("gesperrt@example.de", "Gesperrt", Roles.Admin, Ct);
            await accounts.InviteAsync("verwalter@example.de", "Verwalter", Roles.Manager, Ct);
            Assert.True(second.Succeeded && locked.Succeeded);
            var actor = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, second.UserId!.Value.ToString())], "Test"));
            await scope.ServiceProvider.GetRequiredService<IUserManagementService>().LockAsync(actor, locked.UserId!.Value, Ct);
        }

        await WriteStatusAsync(succeeded: false, InfrastructureEnvironment.StartTime.AddHours(-7), "Fatal: unable to open repository");

        Assert.Equal(2, await WarnAsync());

        var mail = Assert.Single(await WarningMailsAsync());
        Assert.Equal(["admin2@example.de", "admin@example.de"], mail.To.Order(StringComparer.Ordinal));
        Assert.Equal("Elocaro: Datensicherung prüfen", mail.Subject);
        Assert.Contains("fehlgeschlagen", mail.Html, StringComparison.Ordinal);
        Assert.Contains("Fatal: unable to open repository", mail.Html, StringComparison.Ordinal);
        Assert.Contains("https://elocaro.example.de/verwaltung/system/datensicherung", mail.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_warning_without_a_problem()
    {
        await using (var scope = environment.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAccountService>().CreateFirstAdminAsync("admin@example.de", "Admin", "Langes-Passwort-2026", Ct);
        }

        await WriteStatusAsync(succeeded: true, InfrastructureEnvironment.StartTime.AddHours(-7));

        Assert.Equal(0, await WarnAsync());
        Assert.Empty(await WarningMailsAsync());
    }

    private async Task WriteStatusAsync(bool succeeded, DateTimeOffset timestamp, string message = "") =>
        await File.WriteAllTextAsync(statusFile,
            $$"""{"timestamp":"{{timestamp.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}}","succeeded":{{(succeeded ? "true" : "false")}},"message":"{{message}}","snapshot":"","durationSeconds":3}""", Ct);

    private async Task<BackupSnapshot> StateAsync()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IBackupStatus>().QueryAsync(Ct);
    }

    private async Task<int> WarnAsync()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<BackupWarning>().ValidateAsync(Ct);
    }

    private async Task<List<OutgoingMail>> WarningMailsAsync()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ElocaroDbContext>().Set<OutgoingMail>().Where(m => m.Template == "backup-warning").ToListAsync(Ct);
    }
}
