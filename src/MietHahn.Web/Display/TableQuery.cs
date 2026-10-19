using System.Linq.Expressions;

namespace MietHahn.Web.Display;

/// <summary>Sortierung und Blättern über Query-Parameter (<c>?sortierung=name&amp;absteigend=true&amp;seite=2</c>).</summary>
public sealed record TableQuery(string? SortOrder, bool Descending, int Page, int PageSize = 25)
{
    public int Skip => (Math.Max(Page, 1) - 1) * PageSize;

    /// <summary>Sortiert nach der gewählten Spalte; unbekannte Spalten fallen auf den Standard zurück.</summary>
    public IQueryable<T> Sort<T>(IQueryable<T> query, IReadOnlyDictionary<string, Expression<Func<T, object?>>> columns, string standard)
    {
        ArgumentNullException.ThrowIfNull(columns);
        var key = SortOrder is not null && columns.ContainsKey(SortOrder) ? SortOrder : standard;
        var expression = columns[key];
        return Descending ? query.OrderByDescending(expression) : query.OrderBy(expression);
    }

    public IEnumerable<T> Sort<T>(IEnumerable<T> list, IReadOnlyDictionary<string, Func<T, object?>> columns, string standard)
    {
        ArgumentNullException.ThrowIfNull(columns);
        var key = SortOrder is not null && columns.ContainsKey(SortOrder) ? SortOrder : standard;
        return Descending ? list.OrderByDescending(columns[key]) : list.OrderBy(columns[key]);
    }

    public Paged<T> Paginate<T>(IReadOnlyCollection<T> sorted) =>
        new(sorted.Skip(Skip).Take(PageSize).ToList(), sorted.Count, Math.Max(Page, 1), PageSize);
}

public sealed record Paged<T>(IReadOnlyList<T> Entries, int Total, int Page, int PageSize)
{
    public int Pages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
}
