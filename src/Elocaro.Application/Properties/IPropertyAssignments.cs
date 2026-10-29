using Elocaro.Application.Common;

namespace Elocaro.Application.Properties;

/// <summary>Which properties a caretaker looks after (sees in the service area).</summary>
public interface IPropertyAssignments
{
    Task<IReadOnlyList<Guid>> PropertiesOfAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> IsAssignedAsync(Guid userId, Guid propertyId, CancellationToken cancellationToken = default);

    /// <summary>Replaces the account's assignments; caretakers only.</summary>
    Task<Result> SetAsync(Guid userId, IReadOnlyCollection<Guid> propertyIds, CancellationToken cancellationToken = default);
}
