using Microsoft.EntityFrameworkCore.Migrations;

namespace MietHahn.Infrastructure.Persistence.MigrationHelpers;

/// <summary>
/// Migration helper for non-overlapping validity periods (e.g. at most one active lease per rental object).
/// Creates a PostgreSQL exclusion constraint over <c>daterange(from, to, '[]')</c>, both bounds inclusive,
/// <c>to = NULL</c> means "open-ended". Soft-deleted rows are ignored.
/// The check happens only on commit (<c>DEFERRABLE INITIALLY DEFERRED</c>): when advancing a time series
/// (ending the old state, inserting the new one), the order of commands within SaveChanges is not defined.
/// Errors are detected by <see cref="DatabaseErrors.IsOverlap"/>.
/// Prerequisite: extension <c>btree_gist</c> (enabled in the model).
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
