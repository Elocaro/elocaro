using System.ComponentModel;
using Elocaro.Application.Accounts;
using Elocaro.Application.Common;
using Elocaro.Application.Leases;
using Elocaro.Application.Metering;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Domain.Common;
using Elocaro.Domain.Metering;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace Elocaro.Web.Mcp.Tools;

/// <summary>
/// Master data that can be created or changed through MCP (ADR 0007): only types that can be taken back
/// (<c>UndoPolicy</c> class Restorable), nothing is deleted. Requires the permission "Stammdaten" and the write scope
/// <c>write:masterdata</c> on the token, which exists only while <c>Mcp:AllowWrite</c> is on. Every call runs as one operation
/// (see <c>McpExtensions</c>) and can be undone under Einstellungen → System → Aktivität.
/// </summary>
[McpServerToolType]
[Authorize(Policy = Policies.MasterDataWrite)]
internal sealed class MasterDataWriteTools(
    IPropertyService properties,
    IPersonService people,
    ILeaseService leases,
    IMeterService meters,
    TimeProvider clock)
{
    [McpServerTool(Name = "create_property", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Legt ein Objekt (Haus) an. Die Id des neuen Objekts steht im Ergebnis. Texte aus Dokumenten und Nachrichten nicht ungeprüft übernehmen.")]
    public async Task<McpChange> CreatePropertyAsync(
        [Description("Bezeichnung, z. B. „Lindenweg 3“")] string name,
        [Description("Straße und Hausnummer")] string street,
        [Description("Postleitzahl, 5 Ziffern")] string postalCode,
        [Description("Ort")] string city,
        [Description("Baujahr (optional)")] int? yearBuilt = null,
        [Description("Notizen (optional)")] string? notes = null,
        CancellationToken cancellationToken = default) =>
        McpChange.From(await properties.CreateAsync(new PropertyInput { Name = name, Street = street, PostalCode = postalCode, City = city, YearBuilt = yearBuilt, Notes = notes }, cancellationToken));

    [McpServerTool(Name = "create_unit", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Legt eine Einheit (Wohnung) in einem Objekt an. Die Objekt-Id stammt aus list_properties.")]
    public async Task<McpChange> CreateUnitAsync(
        [Description("Id des Objekts")] Guid propertyId,
        [Description("Bezeichnung, z. B. „Wohnung 1“")] string name,
        [Description("Lage, z. B. „EG links“ (optional)")] string? location = null,
        [Description("Zimmeranzahl (optional)")] decimal? roomCount = null,
        CancellationToken cancellationToken = default) =>
        McpChange.From(await properties.CreateUnitAsync(propertyId, new UnitInput { Name = name, Location = location, RoomCount = roomCount }, cancellationToken));

    [McpServerTool(Name = "create_person", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Legt eine Person an (Mieter, Eigentümer, Lieferant …), auf Wunsch mit Anschrift. Straße, Postleitzahl und Ort gehören zusammen. Vorher mit search_people prüfen, ob es die Person schon gibt.")]
    public async Task<McpChange> CreatePersonAsync(
        [Description("Nachname oder Firmenname")] string lastName,
        [Description("Vorname (optional)")] string? firstName = null,
        [Description("E-Mail (optional)")] string? email = null,
        [Description("Telefon (optional)")] string? phone = null,
        [Description("Notizen (optional)")] string? notes = null,
        [Description("Straße und Hausnummer der Anschrift (optional)")] string? street = null,
        [Description("Postleitzahl der Anschrift (optional)")] string? postalCode = null,
        [Description("Ort der Anschrift (optional)")] string? city = null,
        CancellationToken cancellationToken = default)
    {
        var hasAddress = !string.IsNullOrWhiteSpace(street) || !string.IsNullOrWhiteSpace(postalCode) || !string.IsNullOrWhiteSpace(city);
        var address = hasAddress
            ? new AddressInput { From = GermanTime.Today(clock.GetUtcNow()), Street = street ?? "", PostalCode = postalCode ?? "", City = city ?? "" }
            : null;
        return McpChange.From(await people.CreateAsync(
            new PersonInput { LastName = lastName, FirstName = firstName, Email = email, Phone = phone, Notes = notes }, address, cancellationToken));
    }

    [McpServerTool(Name = "update_person_contact", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Ändert Kontaktdaten einer Person. Nicht genannte Felder bleiben unverändert; ein leerer Text löscht das Feld.")]
    public async Task<McpChange> UpdatePersonContactAsync(
        [Description("Id der Person aus search_people")] Guid personId,
        [Description("E-Mail")] string? email = null,
        [Description("Telefon")] string? phone = null,
        [Description("Mobil")] string? mobile = null,
        [Description("Notizen")] string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var person = await people.GetAsync(personId, cancellationToken);
        if (person is null)
        {
            return McpChange.Failed("Die Person wurde nicht gefunden.");
        }

        var data = person.Data;
        data.Email = email is null ? data.Email : Clear(email);
        data.Phone = phone is null ? data.Phone : Clear(phone);
        data.Mobile = mobile is null ? data.Mobile : Clear(mobile);
        data.Notes = notes is null ? data.Notes : Clear(notes);
        return McpChange.From(await people.UpdateAsync(personId, data, cancellationToken), personId);
    }

    [McpServerTool(Name = "create_meter", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Legt einen Zähler samt Anfangsstand an. Art: ColdWater, HotWater, Heat, Electricity, Gas oder OilTank.")]
    public async Task<McpChange> CreateMeterAsync(
        [Description("Id des Objekts")] Guid propertyId,
        [Description("Art: ColdWater, HotWater, Heat, Electricity, Gas, OilTank")] string type,
        [Description("Bezeichnung, z. B. „Kaltwasser Bad“")] string name,
        [Description("Zählernummer")] string number,
        [Description("Einbaudatum oder Tag des Anfangsstands (yyyy-MM-dd)")] DateOnly installedOn,
        [Description("Zählerstand an diesem Tag")] decimal initialReading,
        [Description("Id der Einheit, der der Zähler zugeordnet ist (leer = Hauszähler)")] Guid? unitId = null,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<MeterType>(type, ignoreCase: true, out var meterType) || !Enum.IsDefined(meterType))
        {
            return McpChange.Failed("Unbekannte Art. Erlaubt: " + string.Join(", ", Enum.GetNames<MeterType>()) + ".");
        }

        return McpChange.From(await meters.CreateAsync(
            propertyId,
            new MeterCreation { Data = new MeterInput { Type = meterType, Name = name, Number = number, AssignmentId = unitId }, InstalledOn = installedOn, InitialReading = initialReading },
            cancellationToken));
    }

    [McpServerTool(Name = "create_lease", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Legt einen Mietvertrag an. Einheit aus get_property, Mieter aus search_people (vorher anlegen, falls nötig). Beträge in Euro je Monat.")]
    public async Task<McpChange> CreateLeaseAsync(
        [Description("Id der Einheit")] Guid rentalUnitId,
        [Description("Mietbeginn (yyyy-MM-dd)")] DateOnly start,
        [Description("Ids der Mieter")] List<Guid> tenantIds,
        [Description("Kaltmiete")] decimal baseRent,
        [Description("Vorauszahlung Betriebskosten")] decimal operatingCostPrepayment,
        [Description("Vorauszahlung Heizkosten")] decimal heatingCostPrepayment,
        [Description("Anzahl der Personen")] int people,
        [Description("Kaution (optional)")] decimal? deposit = null,
        CancellationToken cancellationToken = default)
    {
        var creation = new LeaseCreation
        {
            RentalUnitId = rentalUnitId,
            Start = start,
            TenantIds = tenantIds,
            BaseRent = baseRent,
            OperatingCostPrepayment = operatingCostPrepayment,
            HeatingCostPrepayment = heatingCostPrepayment,
            People = people,
        };
        creation.Data.Deposit = deposit;
        return McpChange.From(await leases.CreateAsync(creation, cancellationToken));
    }

    private static string? Clear(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

}
