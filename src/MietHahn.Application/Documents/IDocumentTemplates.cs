using System.ComponentModel.DataAnnotations;
using MietHahn.Application.Common;
using MietHahn.Domain.Documents;

namespace MietHahn.Application.Documents;

/// <summary>Management of user-maintained document templates (editor, versions, preview).</summary>
public interface IDocumentTemplates
{
    Task<IReadOnlyList<TemplateRow>> ListAsync(CancellationToken cancellationToken = default);

    Task<TemplateInput?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Creates (<paramref name="id"/> is <c>null</c>) or changes a template. Syntax and placeholders are checked first.</summary>
    Task<Result> SaveAsync(Guid? id, TemplateInput data, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TemplateRevisionRow>> RevisionsAsync(Guid id, CancellationToken cancellationToken = default);

    Task<TemplateRevisionText?> RevisionAsync(Guid revisionId, CancellationToken cancellationToken = default);

    /// <summary>Renders the (even unsaved) text with the sample data of the catalog as PDF; stores nothing.</summary>
    Task<TemplatePreview> PreviewAsync(TemplateKind kind, string subject, string body, CancellationToken cancellationToken = default);

    /// <summary>Creates the bundled starting templates that do not exist yet (by name); existing ones stay untouched.</summary>
    Task<DefaultsInstalled> InstallDefaultsAsync(CancellationToken cancellationToken = default);

    /// <summary>The template as a file for backup or exchange.</summary>
    Task<TemplateExport?> ExportAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Creates a template from an exported file; an existing name gets a suffix.</summary>
    Task<Result> ImportAsync(byte[] content, CancellationToken cancellationToken = default);

    /// <summary>Names of the snippets that can be included in letters.</summary>
    Task<IReadOnlyList<string>> SnippetNamesAsync(CancellationToken cancellationToken = default);
}

public sealed class TemplateInput
{
    [Required(ErrorMessage = "Bitte einen Namen angeben.")]
    [StringLength(100)]
    public string Name { get; set; } = "";

    public TemplateKind Kind { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }

    /// <summary>Required for letters; snippets have none.</summary>
    [StringLength(200)]
    public string Subject { get; set; } = "";

    [Required(ErrorMessage = "Der Vorlagentext darf nicht leer sein.")]
    [StringLength(DocumentTemplate.MaxBodyLength)]
    public string Body { get; set; } = "";

    /// <summary>Number of the current revision (read only).</summary>
    public int Revision { get; set; }
}

public sealed record DefaultsInstalled(int Created, int Existing);

public sealed record TemplateExport(string FileName, byte[] Content);

public sealed record TemplateRow(Guid Id, string Name, TemplateKind Kind, string? Description, int Revision, DateTimeOffset? ChangedAt);

public sealed record TemplateRevisionRow(Guid Id, int Number, DateTimeOffset Timestamp, string? CreatedBy, bool IsCurrent);

public sealed record TemplateRevisionText(Guid Id, Guid TemplateId, int Number, string Subject, string Body);

/// <summary>Either the PDF or the errors (phrased for users, e.g. syntax error or unknown placeholder).</summary>
public sealed record TemplatePreview(byte[]? Pdf, IReadOnlyList<string> Errors);

/// <summary>Placeholder of a template kind; <see cref="Example"/> is what the preview shows.</summary>
public sealed record Placeholder(string Name, string Description, string Example);

/// <summary>Data of a letter: names are the placeholders (snake_case) in the templates.</summary>
public sealed record LetterModel(
    string Salutation,
    string TenantName,
    string RentalUnit,
    string PropertyName,
    string PropertyStreet,
    string PropertyPostalCode,
    string PropertyCity,
    DateOnly LeaseStart,
    DateOnly? LeaseEnd,
    string SenderName,
    DateOnly Today,
    string? SenderBank = null,
    string? SenderIban = null,
    string? SenderBic = null);

/// <summary>Documented placeholders and sample data per template kind.</summary>
public static class TemplateCatalog
{
    public static IReadOnlyList<Placeholder> Placeholders(TemplateKind kind) => kind switch
    {
        TemplateKind.Letter => LetterPlaceholders,
        TemplateKind.Contract => ContractPlaceholders,
        TemplateKind.Snippet => [.. LetterPlaceholders, .. ContractPlaceholders],
        _ => [],
    };

    public static IReadOnlyList<Placeholder> Functions { get; } =
    [
        new("money amount", "Betrag als Währung", "{{ money 770 }} → 770,00 €"),
        new("date value", "Datum im Format TT.MM.JJJJ", "{{ date today }} → 20.10.2026"),
        new("number value 2", "Zahl mit Nachkommastellen", "{{ number 1234.5 2 }} → 1.234,50"),
        new("text_to_html text", "Text mit Zeilenumbrüchen", "{{ text_to_html note }}"),
        new("include 'Name'", "Fügt einen Baustein ein (Name wie in der Liste der Vorlagen)", "{{ include 'Grußformel' }}"),
    ];

    /// <summary>The first sample is the one the preview shows.</summary>
    public static IReadOnlyList<object> Samples(TemplateKind kind) => kind switch
    {
        TemplateKind.Letter => [LetterSample],
        TemplateKind.Contract => ContractSamples,
        TemplateKind.Snippet => [LetterSample, .. ContractSamples],
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static object Sample(TemplateKind kind) => Samples(kind)[0];

    private static readonly IReadOnlyList<Placeholder> LetterPlaceholders =
    [
        new("salutation", "Anrede, z. B. „Sehr geehrte Frau Muster,“", "Sehr geehrte Frau Muster,"),
        new("tenant_name", "Name des Mieters (bei mehreren: alle)", "Erika Muster"),
        new("rental_unit", "Mietsache: Einheit und Objekt", "Wohnung 1, Lindenweg 3"),
        new("property_name", "Name des Objekts", "Lindenweg 3"),
        new("property_street", "Straße des Objekts", "Lindenweg 3"),
        new("property_postal_code", "Postleitzahl des Objekts", "50667"),
        new("property_city", "Ort des Objekts", "Köln"),
        new("lease_start", "Mietbeginn (mit „date“ formatieren)", "01.04.2024"),
        new("lease_end", "Mietende, leer bei unbefristetem Vertrag", "(leer)"),
        new("sender_name", "Name des Vermieters", "Hans Vermieter"),
        new("today", "Heutiges Datum (mit „date“ formatieren)", "20.10.2026"),
        new("sender_bank", "Bank des Vermieters (leer, wenn nicht hinterlegt)", "Sparkasse KölnBonn"),
        new("sender_iban", "IBAN des Vermieters (leer, wenn nicht hinterlegt)", "DE89 3704 0044 0532 0130 00"),
        new("sender_bic", "BIC des Vermieters (leer, wenn nicht hinterlegt)", "COLSDE33XXX"),
    ];

    private static readonly IReadOnlyList<Placeholder> ContractPlaceholders =
    [
        new("today", "Datum der Erstellung", "20.10.2026"),
        new("sender_name / sender_address", "Vermieter und seine Anschrift", "Hans Vermieter / Musterweg 1, 12345 Musterstadt"),
        new("sender_iban / sender_bank", "Bankverbindung des Vermieters (leer, wenn nicht hinterlegt)", "DE02 1203 …"),
        new("tenants", "Mieter (Liste): name, birth_date, address", "{{ for t in tenants }}{{ t.name }}{{ end }}"),
        new("guarantors", "Bürgen (Liste, wie tenants)", ""),
        new("tenant_names", "Alle Mieter in einem Satz", "Erika Muster und Max Muster"),
        new("property_name, property_street, property_postal_code, property_city, year_built, heating_type", "Das Mietobjekt", "Lindenweg 3, 50667 Köln, 1965, Zentralheizung"),
        new("unit_name, unit_location, room_count, living_area, bathroom, balcony, fitted_kitchen, amenities, energy_standard", "Die Wohnung (Fläche in m² zum Mietbeginn)", "Wohnung 1, 2. OG links, 3, 60,00"),
        new("is_shared_flat_room", "Wahr beim Zimmervertrag einer WG", "nein"),
        new("room_name, room_area, common_area_share, furnishing", "Zimmer: Name, Fläche, anteilige Gemeinschaftsfläche (m²), Möblierung", "Zimmer 1, 14,00, 11,50, teilmöbliert"),
        new("furnishing_list", "Möblierungsliste (Liste von Gegenständen)", "Bett, Schrank, Schreibtisch"),
        new("other_rooms", "Die übrigen Zimmer der WG (Liste von Namen)", "Zimmer 2, Zimmer 3"),
        new("start, fixed_term_until, is_fixed_term, notice_period_months", "Mietzeit und Kündigungsfrist", "01.04.2024, unbefristet, 3"),
        new("is_prepayment, is_flat_rate, is_inclusive", "Abrechnungsart: Vorauszahlung mit Abrechnung, Pauschale, Inklusivmiete", ""),
        new("heating_consumption_based", "Heizkosten nach Verbrauch abgerechnet", "ja"),
        new("is_graduated, is_index, rent_steps, index_base_month, index_base_value", "Mietmodell: Staffelmiete (Liste mit from, base_rent) oder Indexmiete mit Ausgangsindex", ""),
        new("base_rent, operating_cost_prepayment, heating_cost_prepayment, operating_cost_flat_rate, parking_space, furnishing_surcharge, total_rent", "Monatliche Beträge zum Mietbeginn (mit „money“ formatieren)", "500,00 €"),
        new("other_components", "Weitere Beträge (Liste: name, amount)", ""),
        new("due_on_working_day, direct_debit", "Fälligkeit (Werktag des Monats) und Lastschrift", "3, nein"),
        new("deposit, deposit_note, max_deposit", "Kaution (leer, wenn keine) und gesetzliche Obergrenze (drei Monatskaltmieten, § 551 BGB)", "1.500,00 €"),
        new("cost_types / excluded_cost_types", "Vereinbarte bzw. ausgeschlossene Betriebskostenarten (Liste: number, name; § 2 BetrKV)", "Wasserversorgung"),
        new("person_count, notes", "Personenzahl zum Mietbeginn, Anmerkungen aus dem Vertrag", "2"),
    ];

    private static readonly LetterModel LetterSample = new("Sehr geehrte Frau Muster,", "Erika Muster", "Wohnung 1, Lindenweg 3", "Lindenweg 3", "Lindenweg 3", "50667", "Köln",
        new DateOnly(2024, 4, 1), null, "Hans Vermieter", new DateOnly(2026, 10, 20), "Sparkasse KölnBonn", "DE89 3704 0044 0532 0130 00", "COLSDE33XXX");

    // Two samples so that both sides of the conditions (apartment/room, open/fixed, standard/graduated/index) get exercised when checking a text.
    private static readonly IReadOnlyList<ContractModel> ContractSamples =
    [
        new(new DateOnly(2026, 10, 20), "Hans Vermieter", "Musterweg 1, 12345 Musterstadt",
            [new("Erika Muster", new DateOnly(1985, 3, 14), "Alte Straße 5, 50667 Köln")], [], "Erika Muster",
            "Lindenweg 3", "Lindenweg 3", "50667", "Köln", 1965, "Zentralheizung",
            "Wohnung 1", "2. OG links", 3m, 60m, "Wannenbad", true, true, "Kellerabteil", "B",
            false, null, null, null, null, [], [],
            new DateOnly(2026, 11, 1), null, false, 3,
            true, false, false, false, false, false, [], null, null,
            500m, 100m, 60m, 0m, 0m, 0m, [], 660m,
            3, false, 1500m, null, 1500m,
            [new(1, "Laufende öffentliche Lasten (Grundsteuer)"), new(2, "Wasserversorgung")], [new(15, "Antenne / Breitbandnetz")], 2, null,
            "DE89 3704 0044 0532 0130 00", "Sparkasse KölnBonn"),
        new(new DateOnly(2026, 10, 20), "Hans Vermieter", "Musterweg 1, 12345 Musterstadt",
            [new("Max Muster", null, null)], [new("Anna Muster", null, "Alte Straße 5, 50667 Köln")], "Max Muster",
            "Lindenweg 3", "Lindenweg 3", "50667", "Köln", null, "Fernwärme",
            "Wohnung 2", null, null, 80m, null, false, false, null, null,
            true, "Zimmer 1", 14m, 11.5m, "teilmöbliert", ["Bett", "Schrank"], ["Zimmer 2", "Zimmer 3"],
            new DateOnly(2026, 11, 1), new DateOnly(2027, 10, 31), true, 3,
            false, true, false, true, true, true, [new(new DateOnly(2027, 11, 1), 520m)], new DateOnly(2026, 9, 1), 123.4m,
            400m, 0m, 0m, 80m, 25m, 30m, [new("Sonstiges", 5m)], 540m,
            5, true, null, "Ratenzahlung", 1200m,
            [new(null, "Sonstige Kosten")], [], null, "Anmerkung"),
    ];
}
