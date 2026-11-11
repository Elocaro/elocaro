using Elocaro.Application.Accounts;
using Elocaro.Application.Audit;
using Elocaro.Infrastructure.Audit;
using Elocaro.Infrastructure.Identity;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Audit;

public class SecurityAuditTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Failed_sign_in_of_a_known_account_is_linked_to_the_account()
    {
        await using var environment = await InfrastructureEnvironment.CreateAsync(postgres);
        await using var scope = environment.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
        await accounts.CreateFirstAdminAsync(scope.ServiceProvider.GetRequiredService<ISetupCode>().Value, "admin@example.de", "Admin", "Langes-Passwort-2026!", Ct);

        await scope.ServiceProvider.GetRequiredService<ISecurityAudit>().LogAsync(SecurityEvent.SignInFailed, "admin@example.de", null, Ct);

        var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();
        var entry = await db.Set<AuditEntry>().SingleAsync(e => e.Action == AuditAction.SecurityEvent, Ct);
        Assert.Equal("admin@example.de", entry.User);
        Assert.NotEqual(Guid.Empty, entry.EntityId);
        Assert.Contains("Anmeldung fehlgeschlagen", entry.ChangesJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_account_is_logged_without_an_entity_and_without_control_characters()
    {
        await using var environment = await InfrastructureEnvironment.CreateAsync(postgres);
        await using var scope = environment.Services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<ISecurityAudit>().LogAsync(SecurityEvent.LockedOut, "x@example.de\r\nFälschung", null, Ct);

        var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();
        var entry = await db.Set<AuditEntry>().SingleAsync(e => e.Action == AuditAction.SecurityEvent, Ct);
        Assert.Equal(Guid.Empty, entry.EntityId);
        Assert.Equal("x@example.deFälschung", entry.User);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    [InlineData(" a@example.de ", "a@example.de")]
    public void Clean_trims_and_drops_empty_values(string? value, string? expected) => Assert.Equal(expected, SecurityAudit.Clean(value));

    [Fact]
    public void Clean_shortens_long_input() => Assert.Equal(200, SecurityAudit.Clean(new string('a', 5000))!.Length);
}
