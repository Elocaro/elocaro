namespace Elocaro.Application.People;

/// <summary>
/// Extension point for business modules: in which role a person appears (tenant, guarantor, ticket ...). Determines the
/// role display, prevents deletion and provides the end of the last relationship for the retention period.
/// </summary>
public interface IPersonReference
{
    Task<IReadOnlyList<PersonRelation>> ReferencesAsync(IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken = default);
}

/// <param name="Role">E.g. "Mieter", "Bürge".</param>
/// <param name="Description">E.g. "Wohnung 1, Lindenweg 3 (seit 01.03.2026)".</param>
/// <param name="Active">Is the relationship still ongoing (e.g. lease not ended)?</param>
/// <param name="EndsOn">End of the relationship; basis of the retention period.</param>
public sealed record PersonRelation(Guid PersonId, string Role, string Description, bool Active, DateOnly? EndsOn, string? Link = null);

/// <summary>Extension point for the GDPR data access request: each module provides the data stored about the person.</summary>
public interface IPersonalDataSource
{
    /// <summary>Section name in the response, e.g. "mietvertraege".</summary>
    string Section { get; }

    /// <returns>Serializable data, or <c>null</c> if the module stores nothing about the person.</returns>
    Task<object?> DataAsync(Guid personId, CancellationToken cancellationToken = default);
}
