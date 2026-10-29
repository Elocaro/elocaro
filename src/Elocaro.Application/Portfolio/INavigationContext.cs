using Elocaro.Domain.Properties;

namespace Elocaro.Application.Portfolio;

/// <summary>A step of the trail above the page, e.g. the property or lease the page belongs to. Without <paramref name="Href"/> it is not a link.</summary>
public sealed record TrailStep(string Label, string? Href);

/// <summary>What the header above the property tabs shows: name, address and the key facts.</summary>
public sealed record PropertySummary(string Name, string Address, int? YearBuilt, HeatingType HeatingType, EnergySource EnergySource, int UnitCount);

/// <summary>Where a page sits: trail with the names of the entities, the property whose workspace tabs apply and its summary.</summary>
public sealed record PageContext(IReadOnlyList<TrailStep> Trail, Guid? PropertyId, PropertySummary? Property = null);

/// <summary>Resolves the entity behind a path of the administration (property, unit, room, meter, statement, lease, person) to its names.</summary>
public interface INavigationContext
{
    /// <summary>The context of the page at <paramref name="path"/>, or null if it belongs to no single entity or the entity does not exist.</summary>
    Task<PageContext?> ResolveAsync(string path, CancellationToken cancellationToken = default);
}
