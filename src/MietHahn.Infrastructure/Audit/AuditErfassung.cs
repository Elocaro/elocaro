using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using MietHahn.Application.Audit;
using MietHahn.Domain.Gemeinsam;

namespace MietHahn.Infrastructure.Audit;

/// <summary>Leitet aus dem Change-Tracker die Audit-Einträge einer Speicherung ab.</summary>
internal static class AuditErfassung
{
    public const string Maskiert = "***";

    private static readonly HashSet<string> Technisch = new(StringComparer.Ordinal)
    {
        nameof(Entitaet.Id), nameof(Entitaet.ErstelltAm), nameof(Entitaet.ErstelltVon),
        nameof(Entitaet.GeaendertAm), nameof(Entitaet.GeaendertVon), nameof(Entitaet.Version), nameof(Entitaet.Geloescht),
    };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<AuditEintrag> Erfassen(DbContext context, DateTimeOffset zeitpunkt, string benutzer)
    {
        var ergebnis = new List<AuditEintrag>();
        foreach (var eintrag in context.ChangeTracker.Entries<Entitaet>())
        {
            if (eintrag.State is not (EntityState.Added or EntityState.Modified)
                || eintrag.Metadata.ClrType.GetCustomAttribute<OhneAuditAttribute>() is not null)
            {
                continue;
            }

            var geloescht = eintrag.State == EntityState.Modified
                && eintrag.Property(e => e.Geloescht) is { IsModified: true, CurrentValue: true, OriginalValue: false };
            var aktion = eintrag.State == EntityState.Added ? AuditAktion.Angelegt
                : geloescht ? AuditAktion.Geloescht
                : AuditAktion.Geaendert;

            var aenderungen = aktion == AuditAktion.Geloescht ? [] : Feldaenderungen(eintrag, aktion);
            if (aktion == AuditAktion.Geaendert && aenderungen.Count == 0)
            {
                continue;
            }

            ergebnis.Add(new AuditEintrag(
                zeitpunkt, benutzer, eintrag.Metadata.ClrType.Name, eintrag.Entity.Id, aktion, JsonSerializer.Serialize(aenderungen, Json)));
        }

        return ergebnis;
    }

    public static IReadOnlyList<Feldaenderung> Lesen(string json) =>
        JsonSerializer.Deserialize<List<Feldaenderung>>(json, Json) ?? [];

    private static List<Feldaenderung> Feldaenderungen(EntityEntry<Entitaet> eintrag, AuditAktion aktion)
    {
        var liste = new List<Feldaenderung>();
        foreach (var property in eintrag.Properties)
        {
            Hinzufuegen(liste, property.Metadata, property.Metadata.Name, property.OriginalValue, property.CurrentValue, property.IsModified, aktion);
        }

        foreach (var komplex in eintrag.ComplexProperties)
        {
            foreach (var property in komplex.Properties)
            {
                var name = komplex.Metadata.Name + "." + property.Metadata.Name;
                Hinzufuegen(liste, property.Metadata, name, property.OriginalValue, property.CurrentValue, property.IsModified, aktion);
            }
        }

        return liste;
    }

    private static void Hinzufuegen(List<Feldaenderung> liste, IProperty property, string name, object? alt, object? neu, bool geaendert, AuditAktion aktion)
    {
        if (Technisch.Contains(name) || property.IsShadowProperty())
        {
            return;
        }

        if (aktion == AuditAktion.Angelegt)
        {
            if (neu is not null)
            {
                liste.Add(new Feldaenderung(name, null, Text(property, neu)));
            }

            return;
        }

        if (geaendert && !Equals(alt, neu) && !(alt is IEnumerable a && neu is IEnumerable n && a is not string && Enumerable.SequenceEqual(a.Cast<object>(), n.Cast<object>())))
        {
            liste.Add(new Feldaenderung(name, Text(property, alt), Text(property, neu)));
        }
    }

    private static string? Text(IProperty property, object? wert)
    {
        if (wert is null)
        {
            return null;
        }

        if (property.PropertyInfo?.GetCustomAttribute<VertraulichAttribute>() is not null)
        {
            return Maskiert;
        }

        return wert switch
        {
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
            decimal m => m.ToString("0.############################", CultureInfo.InvariantCulture), // ohne Nachkomma-Nullen: 650.00 == 650
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            string s => s,
            IEnumerable e => string.Join(", ", e.Cast<object>()),
            _ => wert.ToString(),
        };
    }
}
