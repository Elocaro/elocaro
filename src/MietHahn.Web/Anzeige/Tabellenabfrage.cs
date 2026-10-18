using System.Linq.Expressions;

namespace MietHahn.Web.Anzeige;

/// <summary>Sortierung und Blättern über Query-Parameter (<c>?sortierung=name&amp;absteigend=true&amp;seite=2</c>).</summary>
public sealed record Tabellenabfrage(string? Sortierung, bool Absteigend, int Seite, int SeitenGroesse = 25)
{
    public int Ueberspringen => (Math.Max(Seite, 1) - 1) * SeitenGroesse;

    /// <summary>Sortiert nach der gewählten Spalte; unbekannte Spalten fallen auf den Standard zurück.</summary>
    public IQueryable<T> Sortieren<T>(IQueryable<T> abfrage, IReadOnlyDictionary<string, Expression<Func<T, object?>>> spalten, string standard)
    {
        ArgumentNullException.ThrowIfNull(spalten);
        var schluessel = Sortierung is not null && spalten.ContainsKey(Sortierung) ? Sortierung : standard;
        var ausdruck = spalten[schluessel];
        return Absteigend ? abfrage.OrderByDescending(ausdruck) : abfrage.OrderBy(ausdruck);
    }

    public IEnumerable<T> Sortieren<T>(IEnumerable<T> liste, IReadOnlyDictionary<string, Func<T, object?>> spalten, string standard)
    {
        ArgumentNullException.ThrowIfNull(spalten);
        var schluessel = Sortierung is not null && spalten.ContainsKey(Sortierung) ? Sortierung : standard;
        return Absteigend ? liste.OrderByDescending(spalten[schluessel]) : liste.OrderBy(spalten[schluessel]);
    }

    public Seitenweise<T> Blaettern<T>(IReadOnlyCollection<T> sortiert) =>
        new(sortiert.Skip(Ueberspringen).Take(SeitenGroesse).ToList(), sortiert.Count, Math.Max(Seite, 1), SeitenGroesse);
}

public sealed record Seitenweise<T>(IReadOnlyList<T> Eintraege, int Gesamt, int Seite, int SeitenGroesse)
{
    public int Seiten => Math.Max(1, (int)Math.Ceiling(Gesamt / (double)SeitenGroesse));
}
