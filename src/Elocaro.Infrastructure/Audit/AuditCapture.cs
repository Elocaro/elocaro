using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Elocaro.Application.Audit;
using Elocaro.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Elocaro.Infrastructure.Audit;

/// <summary>Derives the audit entries of a save from the change tracker.</summary>
internal static class AuditCapture
{
    public const string Masked = "***";

    private static readonly HashSet<string> Technical = new(StringComparer.Ordinal)
    {
        nameof(Entity.Id), nameof(Entity.CreatedAt), nameof(Entity.CreatedBy),
        nameof(Entity.ModifiedAt), nameof(Entity.ModifiedBy), nameof(Entity.Version), nameof(Entity.Deleted),
    };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<AuditEntry> Capture(DbContext context, DateTimeOffset timestamp, string user)
    {
        var result = new List<AuditEntry>();
        foreach (var entry in context.ChangeTracker.Entries<Entity>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified)
                || entry.Metadata.ClrType.GetCustomAttribute<NoAuditAttribute>() is not null)
            {
                continue;
            }

            var deleted = entry.State == EntityState.Modified
                && entry.Property(e => e.Deleted) is { IsModified: true, CurrentValue: true, OriginalValue: false };
            var action = entry.State == EntityState.Added ? AuditAction.Created
                : deleted ? AuditAction.Deleted
                : AuditAction.Modified;

            var changes = action == AuditAction.Deleted ? [] : FieldChanges(entry, action);
            if (action == AuditAction.Modified && changes.Count == 0)
            {
                continue;
            }

            var audit = new AuditEntry(
                timestamp, user, entry.Metadata.ClrType.Name, entry.Entity.Id, action, JsonSerializer.Serialize(changes, Json));
            if (entry.State == EntityState.Modified && UndoPolicy.IsRestorable(entry.Metadata.ClrType))
            {
                audit.SetRestore(Snapshot(entry));
            }

            result.Add(audit);
        }

        return result;
    }

    /// <summary>The typed values (all columns, also foreign keys, nothing masked) of the changed properties before the save, as JSON by property name.</summary>
    private static string? Snapshot(EntityEntry<Entity> entry)
    {
        var before = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in entry.Properties)
        {
            if (property.IsModified && !Technical.Contains(property.Metadata.Name))
            {
                before[property.Metadata.Name] = JsonSerializer.SerializeToElement(property.OriginalValue, property.Metadata.ClrType, Json);
            }
        }

        foreach (var complexProperty in entry.ComplexProperties)
        {
            foreach (var property in complexProperty.Properties)
            {
                if (property.IsModified)
                {
                    before[complexProperty.Metadata.Name + "." + property.Metadata.Name] = JsonSerializer.SerializeToElement(property.OriginalValue, property.Metadata.ClrType, Json);
                }
            }
        }

        return before.Count == 0 ? null : JsonSerializer.Serialize(before, Json);
    }

    /// <summary>Property name → typed value from <see cref="Snapshot"/>.</summary>
    public static IReadOnlyDictionary<string, JsonElement> ReadRestore(string? json) =>
        string.IsNullOrEmpty(json) ? new Dictionary<string, JsonElement>() : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, Json) ?? [];

    public static object? Value(JsonElement element, Type type) => element.Deserialize(type, Json);

    public static IReadOnlyList<FieldChange> Read(string json) =>
        JsonSerializer.Deserialize<List<FieldChange>>(json, Json) ?? [];

    private static List<FieldChange> FieldChanges(EntityEntry<Entity> entry, AuditAction action)
    {
        var list = new List<FieldChange>();
        foreach (var property in entry.Properties)
        {
            Add(list, property.Metadata, property.Metadata.Name, property.OriginalValue, property.CurrentValue, property.IsModified, action);
        }

        foreach (var complexProperty in entry.ComplexProperties)
        {
            foreach (var property in complexProperty.Properties)
            {
                var name = complexProperty.Metadata.Name + "." + property.Metadata.Name;
                Add(list, property.Metadata, name, property.OriginalValue, property.CurrentValue, property.IsModified, action);
            }
        }

        return list;
    }

    private static void Add(List<FieldChange> list, IProperty property, string name, object? old, object? newItem, bool changed, AuditAction action)
    {
        if (Technical.Contains(name) || property.IsShadowProperty())
        {
            return;
        }

        if (action == AuditAction.Created)
        {
            if (newItem is not null)
            {
                list.Add(new FieldChange(name, null, Text(property, newItem)));
            }

            return;
        }

        if (changed && !Equals(old, newItem) && !(old is IEnumerable a && newItem is IEnumerable n && a is not string && Enumerable.SequenceEqual(a.Cast<object>(), n.Cast<object>())))
        {
            list.Add(new FieldChange(name, Text(property, old), Text(property, newItem)));
        }
    }

    private static string? Text(IProperty property, object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (property.PropertyInfo?.GetCustomAttribute<ConfidentialAttribute>() is not null)
        {
            return Masked;
        }

        return value switch
        {
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
            decimal m => m.ToString("0.############################", CultureInfo.InvariantCulture), // without trailing decimal zeros: 650.00 == 650
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            string s => s,
            IEnumerable e => string.Join(", ", e.Cast<object>()),
            _ => value.ToString(),
        };
    }
}
