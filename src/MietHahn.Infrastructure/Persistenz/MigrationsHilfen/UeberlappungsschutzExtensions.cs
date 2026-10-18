using Microsoft.EntityFrameworkCore.Migrations;

namespace MietHahn.Infrastructure.Persistenz.MigrationsHilfen;

/// <summary>
/// Migrations-Hilfe für überlappungsfreie Gültigkeitszeiträume (z. B. höchstens ein aktiver Vertrag je Mietobjekt).
/// Erzeugt einen PostgreSQL-Exclusion-Constraint über <c>daterange(von, bis, '[]')</c>, beide Grenzen inklusive,
/// <c>bis = NULL</c> bedeutet „unbefristet“. Logisch gelöschte Zeilen werden ignoriert.
/// Voraussetzung: Extension <c>btree_gist</c> (im Modell aktiviert).
/// </summary>
public static class UeberlappungsschutzExtensions
{
    public static void ErstelleUeberlappungsschutz(
        this MigrationBuilder migrationBuilder,
        string name,
        string tabelle,
        string vonSpalte,
        string bisSpalte,
        params string[] gruppenSpalten)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.Sql(ErstellenSql(name, tabelle, vonSpalte, bisSpalte, gruppenSpalten));
    }

    public static void EntferneUeberlappungsschutz(this MigrationBuilder migrationBuilder, string name, string tabelle)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.Sql(EntfernenSql(name, tabelle));
    }

    public static string ErstellenSql(string name, string tabelle, string vonSpalte, string bisSpalte, params string[] gruppenSpalten)
    {
        ArgumentNullException.ThrowIfNull(gruppenSpalten);

        var gruppen = string.Concat(gruppenSpalten.Select(s => $"{Bezeichner(s)} WITH =, "));
        return $"ALTER TABLE {Bezeichner(tabelle)} ADD CONSTRAINT {Bezeichner(name)} EXCLUDE USING gist " +
            $"({gruppen}daterange({Bezeichner(vonSpalte)}, {Bezeichner(bisSpalte)}, '[]') WITH &&) " +
            "WHERE (NOT geloescht);";
    }

    public static string EntfernenSql(string name, string tabelle) =>
        $"ALTER TABLE {Bezeichner(tabelle)} DROP CONSTRAINT IF EXISTS {Bezeichner(name)};";

    private static string Bezeichner(string wert)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wert);
        return "\"" + wert.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
