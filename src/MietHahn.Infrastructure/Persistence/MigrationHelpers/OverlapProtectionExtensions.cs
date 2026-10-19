using Microsoft.EntityFrameworkCore.Migrations;

namespace MietHahn.Infrastructure.Persistence.MigrationHelpers;

/// <summary>
/// Migrations-Hilfe für überlappungsfreie Gültigkeitszeiträume (z. B. höchstens ein aktiver Vertrag je Mietobjekt).
/// Erzeugt einen PostgreSQL-Exclusion-Constraint über <c>daterange(von, bis, '[]')</c>, beide Grenzen inklusive,
/// <c>bis = NULL</c> bedeutet „unbefristet“. Logisch gelöschte Zeilen werden ignoriert.
/// Geprüft wird erst beim Commit (<c>DEFERRABLE INITIALLY DEFERRED</c>): Beim Fortschreiben einer Zeitreihe
/// (alten Stand beenden, neuen einfügen) ist die Reihenfolge der Befehle innerhalb von SaveChanges nicht festgelegt.
/// Fehler erkennt <see cref="DatabaseErrors.IsOverlap"/>.
/// Voraussetzung: Extension <c>btree_gist</c> (im Modell aktiviert).
/// </summary>
public static class OverlapProtectionExtensions
{
    public static void CreateOverlapProtection(
        this MigrationBuilder migrationBuilder,
        string name,
        string table,
        string fromColumn,
        string toColumn,
        params string[] groupColumns)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.Sql(CreateSql(name, table, fromColumn, toColumn, groupColumns));
    }

    public static void DropOverlapProtection(this MigrationBuilder migrationBuilder, string name, string table)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.Sql(DropSql(name, table));
    }

    public static string CreateSql(string name, string table, string fromColumn, string toColumn, params string[] groupColumns)
    {
        ArgumentNullException.ThrowIfNull(groupColumns);

        var groups = string.Concat(groupColumns.Select(s => $"{Identifier(s)} WITH =, "));
        return $"ALTER TABLE {Identifier(table)} ADD CONSTRAINT {Identifier(name)} EXCLUDE USING gist " +
            $"({groups}daterange({Identifier(fromColumn)}, {Identifier(toColumn)}, '[]') WITH &&) " +
            "WHERE (NOT deleted) DEFERRABLE INITIALLY DEFERRED;";
    }

    public static string DropSql(string name, string table) =>
        $"ALTER TABLE {Identifier(table)} DROP CONSTRAINT IF EXISTS {Identifier(name)};";

    private static string Identifier(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
