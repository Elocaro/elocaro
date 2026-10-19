using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Audit;
using MietHahn.Infrastructure.Audit;
using MietHahn.Infrastructure.Persistence;
using MietHahn.Integration.Tests.Infrastructure;

namespace MietHahn.Integration.Tests.Audit;

public class AccessLogTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Read_access_appears_with_purpose_in_the_history()
    {
        await using var environment = await InfrastructureEnvironment.CreateAsync(postgres);
        var tenantId = Guid.CreateVersion7();

        await using (var scope = environment.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAccessLog>().LogAsync("Person", tenantId, "Mieterakte geöffnet", Ct);
        }

        await using var read = environment.Services.CreateAsyncScope();
        var entry = Assert.Single(await read.ServiceProvider.GetRequiredService<IChangeHistory>().GetAsync("Person", tenantId, Ct));
        Assert.Equal(AuditAction.Read, entry.Action);
        Assert.Equal("Mieterakte geöffnet", entry.Purpose);
        Assert.Equal("system", entry.User);
    }

    [Fact]
    public async Task Repeated_access_is_logged_only_once_per_time_window()
    {
        await using var environment = await InfrastructureEnvironment.CreateAsync(postgres);
        var tenantId = Guid.CreateVersion7();

        async Task OpenAsync(string purpose = "Mieterakte geöffnet")
        {
            await using var scope = environment.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IAccessLog>().LogAsync("Person", tenantId, purpose, Ct);
        }

        await OpenAsync();
        await OpenAsync();
        environment.Clock.Advance(AccessLog.TimeWindow - TimeSpan.FromMinutes(1));
        await OpenAsync();
        await OpenAsync("DSGVO-Auskunft"); // anderer Zweck zählt eigens
        environment.Clock.Advance(TimeSpan.FromMinutes(2));
        await OpenAsync();

        await using var read = environment.Services.CreateAsyncScope();
        var entries = await read.ServiceProvider.GetRequiredService<IChangeHistory>().GetAsync("Person", tenantId, Ct);
        Assert.Equal(3, entries.Count);
    }

    [Fact]
    public async Task Access_log_can_be_disabled()
    {
        await using var environment = await InfrastructureEnvironment.CreateAsync(postgres, new Dictionary<string, string?> { ["Audit:LogAccess"] = "false" });

        await using (var scope = environment.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAccessLog>().LogAsync("Person", Guid.CreateVersion7(), "Test", Ct);
        }

        await using var check = environment.Services.CreateAsyncScope();
        Assert.False(await check.ServiceProvider.GetRequiredService<MietHahnDbContext>().Set<AuditEntry>().AnyAsync(Ct));
    }

    [Fact]
    public async Task Cleanup_respects_separate_retention_periods()
    {
        await using var environment = await InfrastructureEnvironment.CreateAsync(postgres, new Dictionary<string, string?>
        {
            ["Audit:RetentionYears"] = "10",
            ["Audit:AccessLogRetentionMonths"] = "12",
        });
        var now = InfrastructureEnvironment.StartTime;

        await using (var scope = environment.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
            db.AddRange(
                new AuditEntry(now.AddYears(-11), "x", "A", Guid.CreateVersion7(), AuditAction.Modified, "[]"),
                new AuditEntry(now.AddYears(-9), "x", "B", Guid.CreateVersion7(), AuditAction.Modified, "[]"),
                new AuditEntry(now.AddMonths(-13), "x", "C", Guid.CreateVersion7(), AuditAction.Read, "[]", "alt"),
                new AuditEntry(now.AddMonths(-11), "x", "D", Guid.CreateVersion7(), AuditAction.Read, "[]", "neu"));
            await db.SaveChangesAsync(Ct);
        }

        await using (var scope = environment.Services.CreateAsyncScope())
        {
            Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<AuditCleanup>().CleanUpAsync(Ct));
        }

        await using var check = environment.Services.CreateAsyncScope();
        var remaining = await check.ServiceProvider.GetRequiredService<MietHahnDbContext>().Set<AuditEntry>()
            .Select(a => a.EntityType).OrderBy(t => t).ToListAsync(Ct);
        Assert.Equal(["B", "D"], remaining);
    }
}
