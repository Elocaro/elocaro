namespace MietHahn.Application.Documents;

/// <summary>
/// What the document library needs to know about leases (which documents belong to a unit, a person or a tenant, and the texts for the
/// reference pickers). The leases implement it, so the library does not depend on them.
/// </summary>
public interface IDocumentLeaseSource
{
    /// <summary>Ids of the leases of the given units (as a sub-query, for filters).</summary>
    IQueryable<Guid> LeaseIdsOfUnits(IQueryable<Guid> unitIds);

    /// <summary>Ids of the leases of a unit (as a sub-query, for filters).</summary>
    IQueryable<Guid> LeaseIdsOfUnit(Guid unitId);

    /// <summary>Ids of the leases a person takes part in, in any role (as a sub-query, for filters).</summary>
    IQueryable<Guid> LeaseIdsOfPerson(Guid personId);

    /// <summary>The leases the given people rent as tenants and the units of those leases that run today (as sub-queries, for filters).</summary>
    TenantLeaseScope TenantScopeOf(IReadOnlyCollection<Guid> personIds, DateOnly today);

    Task<bool> LeaseExistsAsync(Guid leaseId, CancellationToken cancellationToken = default);

    /// <summary>The rentable units for the reference picker.</summary>
    Task<IReadOnlyList<ReferenceOption>> UnitOptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>The leases for the reference picker, newest first.</summary>
    Task<IReadOnlyList<ReferenceOption>> LeaseOptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Names of the rentable units and the unit per lease, to describe the references of documents.</summary>
    Task<DocumentReferenceNames> NamesAsync(IReadOnlyCollection<Guid> leaseIds, CancellationToken cancellationToken = default);
}

/// <summary>Leases of tenants and the units of their running leases.</summary>
public sealed record TenantLeaseScope(IQueryable<Guid> LeaseIds, IQueryable<Guid> CurrentUnitIds);

/// <summary>Names of the rentable units by id and the rentable unit of each requested lease.</summary>
public sealed record DocumentReferenceNames(IReadOnlyDictionary<Guid, string> UnitNames, IReadOnlyDictionary<Guid, Guid> UnitOfLease);
