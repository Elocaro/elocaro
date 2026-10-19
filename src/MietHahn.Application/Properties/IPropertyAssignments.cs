using MietHahn.Application.Common;

namespace MietHahn.Application.Properties;

/// <summary>Welche Objekte ein Hausmeister betreut (sieht er im Servicebereich).</summary>
public interface IPropertyAssignments
{
    Task<IReadOnlyList<Guid>> PropertiesOfAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> IsAssignedAsync(Guid userId, Guid propertyId, CancellationToken cancellationToken = default);

    /// <summary>Ersetzt die Zuordnungen des Kontos; nur für Hausmeister.</summary>
    Task<Result> SetAsync(Guid userId, IReadOnlyCollection<Guid> propertyIds, CancellationToken cancellationToken = default);
}
