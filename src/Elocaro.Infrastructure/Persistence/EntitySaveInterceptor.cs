using Elocaro.Application.Audit;
using Elocaro.Application.Common;
using Elocaro.Domain.Common;
using Elocaro.Infrastructure.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Elocaro.Infrastructure.Persistence;

/// <summary>
/// Sets the audit fields on save, converts physical deletion into soft deletion and writes
/// the changes to the audit log in the same transaction (<see cref="AuditEntry"/>).
/// </summary>
internal sealed class EntitySaveInterceptor(TimeProvider clock, ICurrentUser user) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Prepare(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Prepare(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Prepare(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        context.ChangeTracker.DetectChanges();
        var now = clock.GetUtcNow();
        var who = user.Identifier;

        foreach (var entry in context.ChangeTracker.Entries<Entity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Property(e => e.CreatedAt).CurrentValue = now;
                    entry.Property(e => e.CreatedBy).CurrentValue = who;
                    break;

                case EntityState.Modified:
                    entry.Property(e => e.ModifiedAt).CurrentValue = now;
                    entry.Property(e => e.ModifiedBy).CurrentValue = who;
                    break;

                case EntityState.Deleted:
                    entry.State = EntityState.Modified;
                    entry.Property(e => e.Deleted).CurrentValue = true;
                    entry.Property(e => e.ModifiedAt).CurrentValue = now;
                    entry.Property(e => e.ModifiedBy).CurrentValue = who;
                    break;

                case EntityState.Detached:
                case EntityState.Unchanged:
                default:
                    break;
            }
        }

        context.AddRange(AuditCapture.Capture(context, now, who));
        AssignOperation(context, now, who);
    }

    /// <summary>Links the new audit entries to their operation: the ambient scope, or a new one of its own for this save.</summary>
    private void AssignOperation(DbContext context, DateTimeOffset now, string who)
    {
        var entries = context.ChangeTracker.Entries<AuditEntry>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList();
        if (entries.Count == 0)
        {
            return;
        }

        var scope = OperationScope.Active;
        Guid id;
        if (scope is null)
        {
            id = Guid.CreateVersion7();
            context.Add(new Operation(id, now, who, who == ICurrentUser.System ? ChangeChannel.Job : ChangeChannel.Ui, null));
        }
        else
        {
            id = scope.Id;
            if (!scope.Recorded)
            {
                context.Add(new Operation(id, now, who, scope.Channel, scope.Label, scope.UndoOf));
                scope.Recorded = true;
                claimed = scope;
            }
        }

        foreach (var entry in entries)
        {
            entry.AssignOperation(id);
        }
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        Release();
        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Release();
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        claimed = null;
        return base.SavedChanges(eventData, result);
    }

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        claimed = null;
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>A failed save wrote no operation row: the next save of the scope has to write it.</summary>
    private void Release()
    {
        claimed?.Recorded = false;
        claimed = null;
    }

    private OperationScope.State? claimed;
}
