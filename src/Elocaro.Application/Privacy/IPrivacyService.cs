using Elocaro.Application.Common;

namespace Elocaro.Application.Privacy;

/// <summary>GDPR: data access request (Art. 15) and deletion/anonymization after the retention period (Art. 17).</summary>
public interface IPrivacyService
{
    /// <summary>All data stored about the person as JSON (UTF-8). The retrieval is logged.</summary>
    Task<byte[]?> AccessReportAsync(Guid personId, CancellationToken cancellationToken = default);

    /// <summary>Persons whose data is no longer needed (suggestion only, nothing happens automatically).</summary>
    Task<IReadOnlyList<AnonymizationSuggestion>> SuggestionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently removes name, contact data, addresses, IBANs and the values in the change log.
    /// Only possible without active references (e.g. running leases).
    /// </summary>
    Task<Result> AnonymizeAsync(Guid personId, CancellationToken cancellationToken = default);
}

public sealed record AnonymizationSuggestion(Guid PersonId, string DisplayName, string Reason, DateOnly? LastRelation);
