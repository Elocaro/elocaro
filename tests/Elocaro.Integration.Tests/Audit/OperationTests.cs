using Elocaro.Application.Audit;
using Elocaro.Domain.Files;
using Elocaro.Infrastructure.Audit;
using Elocaro.Integration.Tests.Infrastructure;
using Elocaro.Integration.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Integration.Tests.Audit;

public class OperationTests(PostgresFixture postgres) : PersistenceTestBase(postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly OperationScope Scopes = new();

    [Fact]
    public async Task Every_save_without_a_scope_is_its_own_operation()
    {
        await SaveAsync("eins");
        await SaveAsync("zwei");

        await using var db = NewContext();
        var operations = await db.Set<Operation>().ToListAsync(Ct);
        Assert.Equal(2, operations.Count);
        Assert.All(operations, o => Assert.Equal(ChangeChannel.Ui, o.Channel));
        Assert.Equal(2, await db.Set<AuditEntry>().Select(a => a.OperationId).Distinct().CountAsync(Ct));
    }

    [Fact]
    public async Task A_background_job_without_user_is_channel_job()
    {
        User.Identifier = "system";
        await SaveAsync("nachts");

        await using var db = NewContext();
        Assert.Equal(ChangeChannel.Job, (await db.Set<Operation>().SingleAsync(Ct)).Channel);
    }

    [Fact]
    public async Task Saves_inside_a_scope_share_one_operation()
    {
        using (Scopes.Begin(ChangeChannel.Mcp, "MCP create_property"))
        {
            await SaveAsync("eins");
            await SaveAsync("zwei");
        }

        await SaveAsync("danach");

        await using var db = NewContext();
        var operations = await db.Set<Operation>().OrderBy(o => o.Timestamp).ThenBy(o => o.Id).ToListAsync(Ct);
        Assert.Equal(2, operations.Count);
        var scoped = Assert.Single(operations, o => o.Channel == ChangeChannel.Mcp);
        Assert.Equal("MCP create_property", scoped.Label);
        Assert.Equal(2, await db.Set<AuditEntry>().CountAsync(a => a.OperationId == scoped.Id, Ct));
    }

    [Fact]
    public async Task Activity_log_lists_the_operation_with_what_it_touched()
    {
        using (Scopes.Begin(ChangeChannel.Import, "Datenimport"))
        {
            await SaveAsync("eins");
            await SaveAsync("zwei");
        }

        await using var db = NewContext();
        var rows = await new ActivityLog(db).RecentAsync(ChangeChannel.Import, 10, Ct);

        var row = Assert.Single(rows);
        Assert.Equal("Datenimport", row.Label);
        Assert.Equal(new ActivityTouch(nameof(StoredFile), AuditAction.Created, 2), Assert.Single(row.Touched));
        Assert.Empty(await new ActivityLog(db).RecentAsync(ChangeChannel.Mcp, 10, Ct));
    }

    [Fact]
    public async Task Without_audit_entries_no_operation_is_written()
    {
        using (Scopes.Begin(ChangeChannel.Mcp, "MCP list_properties"))
        {
            await using var db = NewContext();
            await db.SaveChangesAsync(Ct);
        }

        await using var check = NewContext();
        Assert.False(await check.Set<Operation>().AnyAsync(Ct));
    }

    private async Task SaveAsync(string name)
    {
        await using var db = NewContext();
        db.Set<StoredFile>().Add(new StoredFile(name, "text/plain", 1, new string('a', 64)));
        await db.SaveChangesAsync(Ct);
    }
}
