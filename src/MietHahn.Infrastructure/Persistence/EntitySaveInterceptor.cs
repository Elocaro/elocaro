using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MietHahn.Application.Common;
using MietHahn.Domain.Common;
using MietHahn.Infrastructure.Audit;

namespace MietHahn.Infrastructure.Persistence;

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
    }
}
