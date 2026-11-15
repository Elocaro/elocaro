using System.Reflection;
using Elocaro.Application.Audit;
using Elocaro.Application.Common;
using Elocaro.Domain.Common;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

namespace Elocaro.Infrastructure.Audit;

/// <summary>
/// Takes back an operation: a creation becomes a soft delete, a deletion is restored, a change gets its earlier values back
/// (the typed values in <see cref="AuditEntry.RestoreJson"/>, not the display texts of the audit log). Anything later touching the same
/// record, or still depending on a created one, stops it. Runs in one transaction and is an operation of its own.
/// </summary>
internal sealed class OperationUndo(ElocaroDbContext db, IOperationScope scope, IEnumerable<IUndoCompensation> compensations) : IOperationUndo
{
    private static readonly MethodInfo Loader = typeof(OperationUndo).GetMethod(nameof(LoadAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    public async Task<UndoCheck> CheckAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        var operation = await db.Set<Operation>().AsNoTracking().SingleOrDefaultAsync(o => o.Id == operationId, cancellationToken);
        if (operation is null)
        {
            return UndoCheck.No("Die Aktion gibt es nicht mehr.");
        }

        if (operation.UndoOf is not null)
        {
            return UndoCheck.No("Ein Rückgängig lässt sich nicht noch einmal rückgängig machen.");
        }

        if (await db.Set<Operation>().AnyAsync(o => o.UndoOf == operationId, cancellationToken))
        {
            return UndoCheck.No("Die Aktion wurde schon rückgängig gemacht.");
        }

        var entries = await db.Set<AuditEntry>().AsNoTracking()
            .Where(a => a.OperationId == operationId && a.Action != AuditAction.Read && a.Action != AuditAction.SecurityEvent)
            .ToListAsync(cancellationToken);
        if (entries.Count == 0)
        {
            return UndoCheck.No("Die Aktion hat keine Änderungen aufgezeichnet.");
        }

        var blocked = entries.Where(e => !CanBeTakenBack(e)).Select(e => e.EntityType).Distinct().Order(StringComparer.Ordinal).ToList();
        if (blocked.Count > 0)
        {
            var bookings = blocked.Any(t => UndoPolicy.All.GetValueOrDefault(t) == UndoClass.Compensating) ? " Buchungen werden nur durch eine Gegenbuchung ausgeglichen, und nur ihr Anlegen; Änderungen und Löschungen bitte als Korrekturbuchung erfassen." : "";
            return UndoCheck.No("Die Aktion enthält Änderungen, die sich nicht rückgängig machen lassen: " + string.Join(", ", blocked) + "." + bookings);
        }

        foreach (var entry in entries.Where(e => CompensationFor(e.EntityType) is not null))
        {
            if (await CompensationFor(entry.EntityType)!.CheckAsync(entry.EntityId, cancellationToken) is { } reason)
            {
                return UndoCheck.No(reason);
            }
        }

        var later = await LaterChangeAsync(operationId, entries, cancellationToken);
        if (later is not null)
        {
            return UndoCheck.No($"{later} wurde seitdem geändert. Rückgängig würde diese Änderung überschreiben.");
        }

        var dependent = await DependentAsync(entries, cancellationToken);
        if (dependent is null && entries.Any(e => e.Action == AuditAction.Created && CompensationFor(e.EntityType) is not null))
        {
            return UndoCheck.YesWith("Buchungen werden nicht gelöscht: Elocaro legt eine Gegenbuchung an, die den Betrag ausgleicht. Das Original bleibt sichtbar.");
        }

        return dependent is null ? UndoCheck.Yes : UndoCheck.No($"{dependent} gehört inzwischen dazu oder verweist darauf. Zuerst das entfernen.");
    }

    public async Task<Result> UndoAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        var check = await CheckAsync(operationId, cancellationToken);
        if (!check.CanUndo)
        {
            return Result.Failed(check.Reason ?? "Nicht möglich.");
        }

        var original = await db.Set<Operation>().AsNoTracking().SingleAsync(o => o.Id == operationId, cancellationToken);
        var entries = (await db.Set<AuditEntry>().AsNoTracking().Where(a => a.OperationId == operationId && a.Action != AuditAction.Read && a.Action != AuditAction.SecurityEvent).ToListAsync(cancellationToken))
            .OrderByDescending(e => e.Id.ToByteArray(bigEndian: true), ByteOrder.Instance)
            .ToList();

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            db.ChangeTracker.Clear();
            using var operation = scope.Begin(original.Channel, "Rückgängig: " + (original.Label ?? "Änderung"), operationId);
            foreach (var entry in entries)
            {
                var result = await ApplyAsync(entry, cancellationToken);
                if (result is not null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    db.ChangeTracker.Clear();
                    return Result.Failed(result);
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return Result.Ok();
        });
    }

    private async Task<string?> ApplyAsync(AuditEntry audit, CancellationToken cancellationToken)
    {
        var type = db.Model.GetEntityTypes().FirstOrDefault(t => !t.IsOwned() && t.ClrType.Name == audit.EntityType)?.ClrType;
        if (type is null)
        {
            return $"Der Typ {audit.EntityType} ist unbekannt.";
        }

        var entity = await (Task<Entity?>)Loader.MakeGenericMethod(type).Invoke(null, [db, audit.EntityId, cancellationToken])!;
        if (entity is null)
        {
            return $"{audit.EntityType} gibt es nicht mehr.";
        }

        var entry = db.Entry(entity);
        switch (audit.Action)
        {
            case AuditAction.Created when CompensationFor(audit.EntityType) is { } compensation:
                return await compensation.CompensateAsync(audit.EntityId, cancellationToken);

            case AuditAction.Created:
                if (entity.Deleted)
                {
                    return $"{audit.EntityType} ist schon gelöscht.";
                }

                db.Remove(entity); // the interceptor turns it into a soft delete
                break;

            case AuditAction.Deleted:
                entry.Property(nameof(Entity.Deleted)).CurrentValue = false;
                SetValues(entry, audit);
                break;

            default:
                SetValues(entry, audit);
                break;
        }

        return null;
    }

    private IUndoCompensation? CompensationFor(string entityType) => compensations.FirstOrDefault(c => c.EntityType == entityType);

    /// <summary>Master data in any way; finance types only their creation, and only where a module can compensate it.</summary>
    private bool CanBeTakenBack(AuditEntry e) =>
        e.Action is AuditAction.Created or AuditAction.Modified or AuditAction.Deleted
        && (UndoPolicy.IsRestorable(e.EntityType) || (e.Action == AuditAction.Created && CompensationFor(e.EntityType) is not null));

    private static void SetValues(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, AuditEntry audit)
    {
        foreach (var (name, element) in AuditCapture.ReadRestore(audit.RestoreJson))
        {
            var dot = name.IndexOf('.', StringComparison.Ordinal);
            var property = dot < 0
                ? entry.Property(name)
                : entry.ComplexProperty(name[..dot]).Property(name[(dot + 1)..]);
            property.CurrentValue = AuditCapture.Value(element, property.Metadata.ClrType);
        }
    }

    /// <summary>Description of the first record of the operation that another operation touched afterwards.</summary>
    private async Task<string?> LaterChangeAsync(Guid operationId, List<AuditEntry> entries, CancellationToken cancellationToken)
    {
        var ids = entries.Select(e => e.EntityId).Distinct().ToList();
        var others = await db.Set<AuditEntry>().AsNoTracking()
            .Where(a => ids.Contains(a.EntityId) && a.OperationId != operationId && a.Action != AuditAction.Read && a.Action != AuditAction.SecurityEvent)
            .Select(a => new { a.Id, a.EntityId })
            .ToListAsync(cancellationToken);
        foreach (var entry in entries)
        {
            var key = entry.Id.ToByteArray(bigEndian: true);
            if (others.Any(o => o.EntityId == entry.EntityId && ByteOrder.Instance.Compare(o.Id.ToByteArray(bigEndian: true), key) > 0))
            {
                return entry.EntityType;
            }
        }

        return null;
    }

    /// <summary>A created record that other records (not part of the same operation) still point to.</summary>
    private async Task<string?> DependentAsync(List<AuditEntry> entries, CancellationToken cancellationToken)
    {
        var inOperation = entries.Select(e => e.EntityId).ToHashSet();
        foreach (var created in entries.Where(e => e.Action == AuditAction.Created))
        {
            var target = db.Model.GetEntityTypes().FirstOrDefault(t => !t.IsOwned() && t.ClrType.Name == created.EntityType);
            if (target is null)
            {
                continue;
            }

            foreach (var foreignKey in db.Model.GetEntityTypes().Where(t => !t.IsOwned() && t.ClrType.IsAssignableTo(typeof(Entity))).SelectMany(t => t.GetDeclaredForeignKeys()))
            {
                if (foreignKey.PrincipalEntityType != target || foreignKey.Properties.Count != 1)
                {
                    continue;
                }

                var dependent = foreignKey.DeclaringEntityType;
                var table = StoreObjectIdentifier.Create(dependent, StoreObjectType.Table);
                if (table is null)
                {
                    continue;
                }

                var column = foreignKey.Properties[0].GetColumnName(table.Value);
                var deleted = dependent.FindProperty(nameof(Entity.Deleted))!.GetColumnName(table.Value);
                var key = dependent.FindPrimaryKey()!.Properties[0].GetColumnName(table.Value);
                var sql = $"SELECT \"{key}\" AS \"Value\" FROM \"{table.Value.Name}\" WHERE \"{column}\" = @id AND \"{deleted}\" = false";
                var rows = await db.Database.SqlQueryRaw<Guid>(sql, new NpgsqlParameter("id", created.EntityId)).ToListAsync(cancellationToken);
                if (rows.Any(id => !inOperation.Contains(id)))
                {
                    return dependent.ClrType.Name;
                }
            }
        }

        return null;
    }

    private static async Task<Entity?> LoadAsync<T>(ElocaroDbContext db, Guid id, CancellationToken cancellationToken)
        where T : Entity =>
        await db.Set<T>().IgnoreQueryFilters().SingleOrDefaultAsync(e => e.Id == id, cancellationToken);

    /// <summary>Byte order of the time-sortable ids (UUID v7): newer ids are greater.</summary>
    private sealed class ByteOrder : IComparer<byte[]>
    {
        public static readonly ByteOrder Instance = new();

        public int Compare(byte[]? x, byte[]? y) => x.AsSpan().SequenceCompareTo(y);
    }
}
