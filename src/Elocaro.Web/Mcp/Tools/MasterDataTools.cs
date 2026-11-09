using System.ComponentModel;
using Elocaro.Application.Accounts;
using Elocaro.Application.Documents;
using Elocaro.Application.Leases;
using Elocaro.Application.Metering;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace Elocaro.Web.Mcp.Tools;

/// <summary>Master data: properties, leases, meters, documents, people. Requires the permission "Stammdaten" (scope read:masterdata).</summary>
[McpServerToolType]
[Authorize(Policy = Policies.MasterData)]
internal sealed class MasterDataTools(
    IPropertyService properties,
    ILeaseService leases,
    IMeterService meters,
    IDocumentLibrary documents,
    IPersonService people,
    IOptions<McpOptions> options)
{
    [McpServerTool(Name = "list_properties", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Listet alle Objekte (Häuser) mit Adresse, Anzahl der Einheiten, Vermietungsstand und Wohnfläche.")]
    public async Task<McpList<PropertyOverview>> ListPropertiesAsync(CancellationToken cancellationToken) =>
        (await properties.ListAsync(null, cancellationToken)).Limit(options);

    [McpServerTool(Name = "get_property", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Zeigt ein Objekt mit seinen Einheiten, Flächen und Eigentümern. Die Id stammt aus list_properties.")]
    public async Task<PropertyDetails?> GetPropertyAsync([Description("Id des Objekts")] Guid propertyId, CancellationToken cancellationToken) =>
        await properties.GetAsync(propertyId, cancellationToken);

    [McpServerTool(Name = "list_leases", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Listet Mietverträge mit Einheit, Objekt, Mieter, Laufzeit, Status und Monatsmiete.")]
    public async Task<McpList<LeaseOverview>> ListLeasesAsync(
        [Description("Nur laufende Verträge (Standard). false zeigt auch beendete.")] bool activeOnly = true,
        [Description("Filter auf Mieter-, Einheiten- oder Objektname (Teilwort, optional)")] string? text = null,
        CancellationToken cancellationToken = default)
    {
        var all = await leases.ListAsync(activeOnly, cancellationToken);
        return (string.IsNullOrWhiteSpace(text)
            ? all
            : all.Where(l => Contains(l.Tenant, text) || Contains(l.RentalUnit, text) || Contains(l.Property, text)).ToList()).Limit(options);
    }

    [McpServerTool(Name = "get_lease", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Zeigt einen Mietvertrag mit Parteien, Mietbestandteilen, Personenzahl und Mietmodell. Die Id stammt aus list_leases.")]
    public async Task<LeaseDetails?> GetLeaseAsync([Description("Id des Mietvertrags")] Guid leaseId, CancellationToken cancellationToken) =>
        await leases.GetAsync(leaseId, cancellationToken);

    [McpServerTool(Name = "list_meters", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Listet die Zähler eines Objekts mit letztem Stand.")]
    public async Task<McpList<MeterRow>> ListMetersAsync([Description("Id des Objekts")] Guid propertyId, CancellationToken cancellationToken) =>
        (await meters.ListAsync(propertyId, false, cancellationToken)).Limit(options);

    [McpServerTool(Name = "search_documents", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Sucht Dokumente der Ablage nach Titel und Bezug. Liefert nur Metadaten, keine Dateiinhalte.")]
    public async Task<McpList<DocumentRow>> SearchDocumentsAsync(
        [Description("Suchtext (optional)")] string? text = null,
        [Description("Nur Dokumente dieses Objekts (optional)")] Guid? propertyId = null,
        [Description("Nur Dokumente dieses Mietvertrags (optional)")] Guid? leaseId = null,
        CancellationToken cancellationToken = default) =>
        (await documents.SearchAsync(new DocumentQuery(text, null, propertyId, null, leaseId, null), cancellationToken)).Limit(options);

    [McpServerTool(Name = "search_people", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Sucht Personen (Mieter, Eigentümer, Lieferanten …) nach Namen. Liefert Kontaktdaten, keine Bankverbindungen.")]
    public async Task<McpList<PersonOverview>> SearchPeopleAsync([Description("Suchtext (optional)")] string? text = null, CancellationToken cancellationToken = default) =>
        (await people.SearchAsync(new PersonQuery(text), cancellationToken)).Limit(options);

    private static bool Contains(string value, string text) => value.Contains(text, StringComparison.OrdinalIgnoreCase);
}
