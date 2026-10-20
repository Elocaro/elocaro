using MietHahn.Domain.Tax;

namespace MietHahn.Web.Display;

internal static class AnlageVLinks
{
    /// <summary>Page showing the origin of a posting (receipt, tenant account, depreciation plan, loans).</summary>
    public static string For(AnlageVPosting posting, Guid propertyId) => posting.Source switch
    {
        AnlageVSource.Receipt when posting.SourceId is { } id => $"/verwaltung/belege/{id}",
        AnlageVSource.Payment when posting.LeaseId is { } lease => $"/verwaltung/vertraege/{lease}/konto",
        AnlageVSource.Depreciation => $"/verwaltung/objekte/{propertyId}/afa",
        AnlageVSource.Maintenance => $"/verwaltung/objekte/{propertyId}/erhaltungsaufwand",
        AnlageVSource.Loan => "/verwaltung/finanzen/darlehen",
        _ => $"/verwaltung/objekte/{propertyId}",
    };
}
