using Elocaro.Domain.Leases;

namespace Elocaro.Application.Admin;

/// <summary>Optional functional areas that can be switched off. Properties, people, leases, tenant accounts, users and settings are always on.</summary>
public enum AppModule
{
    Bank,
    Dunning,
    Receipts,
    OperatingCosts,
    Metering,
    RentIncreases,
    Tax,
    Tickets,
    Notices,
    Documents,
    Deadlines,
}

/// <summary>
/// Choices within the always-on core or a module: which rent models and kinds of utility cost billing the landlord uses.
/// An option that is off stays visible where existing data still uses it.
/// </summary>
public enum AppOption
{
    RentModelGraduated,
    RentModelIndex,
    BillingPrepayment,
    BillingFlatRate,
    BillingInclusive,
    TaxLoans,
    TaxDepreciation,
    TaxDatev,
    TaxOwnerPortal,
    RoomRental,
    UnitParking,
    UnitCommercial,
    LeaseDeposit,
    LeaseGuarantor,
    DunningFees,
    DunningInterest,
}

/// <param name="Group">Options of one group are asked together in the wizard.</param>
/// <param name="Prefixes">URL prefixes that only exist while the option is on.</param>
/// <param name="EntityPages">Sub-pages of an entity, see <see cref="ModuleInfo"/>.</param>
/// <param name="Jobs">Quartz job keys (names) that only make sense with the option.</param>
/// <param name="Module">The module the option belongs to; null for the core.</param>
public sealed record OptionInfo(AppOption Option, string Group, string Title, string Description, string[] Prefixes, (string Root, string Segment)[] EntityPages, string[] Jobs, AppModule? Module)
{
    public bool Owns(string path) => PathRules.Owns(Prefixes, EntityPages, path);
}

internal static class PathRules
{
    /// <summary>Whether the path (without query) is one of the prefixes or a sub-page of an entity.</summary>
    public static bool Owns(string[] prefixes, (string Root, string Segment)[] entityPages, string path)
    {
        path = path.TrimEnd('/');
        if (prefixes.Any(prefix => path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        foreach (var (root, segment) in entityPages)
        {
            if (!path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = path[(root.Length + 1)..].Split('/');
            if (parts.Length >= 2 && Guid.TryParse(parts[0], out _) && parts[1].Equals(segment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>Description of a module for the settings page and the setup wizard.</summary>
/// <param name="Summary">One sentence: what the module does.</param>
/// <param name="Purpose">When you need it (and when you do not).</param>
/// <param name="Includes">The pages and functions that disappear when it is off.</param>
/// <param name="Requires">Modules that must be on for this one.</param>
/// <param name="SetupLink">First page to look at after switching on.</param>
/// <param name="Prefixes">URL prefixes that belong to the module.</param>
/// <param name="EntityPages">Sub-pages of an entity, as (root, segment): <c>/verwaltung/objekte/{id}/heizung</c> = ("/verwaltung/objekte", "heizung").</param>
/// <param name="Jobs">Quartz job keys (names) of the background jobs that only make sense with the module.</param>
public sealed record ModuleInfo(
    AppModule Module,
    string Title,
    string Summary,
    string Purpose,
    string[] Includes,
    AppModule[] Requires,
    string SetupLink,
    string[] Prefixes,
    (string Root, string Segment)[] EntityPages,
    string[] Jobs)
{
    /// <summary>Whether the path (without query) belongs to this module.</summary>
    public bool Owns(string path) => PathRules.Owns(Prefixes, EntityPages, path);
}

public static class ModuleCatalog
{
    private const string Admin = "/verwaltung";

    public static readonly IReadOnlyList<ModuleInfo> All =
    [
        new(AppModule.Bank, "Bank & Zahlungsabgleich",
            "Bankkonten anbinden oder Kontoauszüge importieren und Zahlungen den Mietern zuordnen.",
            "Sinnvoll, wenn Mieten auf einem Konto eingehen und Sie nicht jede Zahlung von Hand verbuchen möchten. Wer Zahlungen selbst im Mieterkonto erfasst, braucht es nicht.",
            ["Bankkonten (FinTS und CSV-Import)", "Umsätze zuordnen", "Zuordnungsregeln"],
            [], $"{Admin}/bank/konten", [$"{Admin}/bank"], [], []),

        new(AppModule.Dunning, "Mahnwesen",
            "Erkennt Rückstände und bereitet Zahlungserinnerungen und Mahnungen zur Freigabe vor.",
            "Sinnvoll, wenn Mieter regelmäßig zu spät zahlen oder Sie Rückstände rechtssicher anmahnen wollen. Es wird nichts ohne Ihre Freigabe versendet.",
            ["Mahnvorschläge und Mahnungen", "Mahngebühren und Verzugszinsen", "täglicher Rückstands-Check"],
            [], $"{Admin}/finanzen/mahnungen", [$"{Admin}/finanzen/mahnungen"], [], ["dunning"]),

        new(AppModule.Receipts, "Belege",
            "Rechnungen und Belege ablegen, Kostenarten zuweisen und optional automatisch auslesen.",
            "Nötig für Nebenkostenabrechnung und Steuer, wenn Sie Kosten über Belege nachweisen. Ohne Kosten und Abrechnungen entbehrlich.",
            ["Belegablage", "Kostenpositionen", "Belegauslesung", "Belege per E-Mail"],
            [], $"{Admin}/belege", [$"{Admin}/belege", $"{Admin}/system/belegauslesung"], [($"{Admin}/objekte", "belegimport")], ["receipt-inbox"]),

        new(AppModule.OperatingCosts, "Nebenkostenabrechnung",
            "Betriebs- und Heizkosten auf die Mieter umlegen und die jährliche Abrechnung erstellen.",
            "Sinnvoll, wenn Sie Vorauszahlungen für Betriebskosten erheben und jährlich abrechnen müssen. Bei Pauschalen ohne Abrechnung nicht nötig.",
            ["Versorgungsverträge (Tarife)", "Umlageschlüssel", "Heizkosten", "Vorschau und Abrechnungs-Assistent", "Vereinbarte Kostenarten je Vertrag", "Abrechnung im Mieterportal"],
            [AppModule.Receipts], $"{Admin}/objekte", [$"{Admin}/abrechnungen", "/portal/abrechnungen"],
            [($"{Admin}/objekte", "versorgung"), ($"{Admin}/objekte", "umlageschluessel"), ($"{Admin}/objekte", "heizung"), ($"{Admin}/objekte", "nebenkosten"),
             ($"{Admin}/objekte", "abrechnung"), ($"{Admin}/objekte", "abrechnungen"), ($"{Admin}/vertraege", "kostenarten")], ["degree-days"]),

        new(AppModule.Metering, "Zähler & Verbrauch",
            "Zähler und Zählerstände erfassen, importieren und Mietern ihren Verbrauch im Portal zeigen.",
            "Sinnvoll bei verbrauchsabhängiger Abrechnung (Wasser, Heizung, Strom) oder wenn Mieter ihren Verbrauch einsehen sollen.",
            ["Zähler und Ablesungen", "CSV-Import von Funkablesesystemen", "Monatliche Verbrauchsinformation", "Verbrauch im Mieterportal", "Zählerstände durch Mieter melden"],
            [], $"{Admin}/objekte", [$"{Admin}/zaehler", "/portal/verbrauch", "/portal/zaehler"],
            [($"{Admin}/objekte", "zaehler"), ($"{Admin}/objekte", "ablesung"), ($"{Admin}/objekte", "zaehlerimport")], ["consumption-information", "meter-reading-reminder"]),

        new(AppModule.RentIncreases, "Mieterhöhung & Indexmiete",
            "Mieterhöhungen vorbereiten, Staffelmieten übernehmen und Indexmieten mit dem Verbraucherpreisindex (VPI) anpassen.",
            "Sinnvoll, wenn Sie Staffel- oder Indexmietverträge haben oder Mieten regelmäßig anheben. Mit reinen Festmieten nicht nötig.",
            ["Verbraucherpreisindex", "Mieterhöhungs-Schreiben", "automatische Übernahme fälliger Staffeln"],
            [], $"{Admin}/vertraege", [$"{Admin}/vpi"], [($"{Admin}/vertraege", "mieterhoehung")], ["graduated-rent"]),

        new(AppModule.Tax, "Steuer & Eigentümer",
            "Anlage V, Abschreibung, Erhaltungsaufwand, Darlehen und DATEV-Export; Einsicht für Eigentümer.",
            "Sinnvoll, wenn Sie Ihre Einkünfte aus Vermietung mit Elocaro vorbereiten oder Eigentümer informieren. Wer die Steuer anderweitig erledigt, braucht es nicht.",
            ["Anlage V", "AfA und Erhaltungsaufwand", "Darlehen", "DATEV-Export", "Eigentümer-Portal"],
            [AppModule.Receipts], $"{Admin}/finanzen/darlehen",
            [$"{Admin}/finanzen/darlehen", $"{Admin}/finanzen/anlage-v-zeilen", $"{Admin}/finanzen/datev", "/eigentuemer"],
            [($"{Admin}/objekte", "afa"), ($"{Admin}/objekte", "erhaltungsaufwand"), ($"{Admin}/objekte", "anlage-v")], []),

        new(AppModule.Tickets, "Tickets & Hausmeister",
            "Schäden und Anliegen als Tickets erfassen, an Handwerker vergeben und Routineaufgaben des Hausmeisters planen.",
            "Sinnvoll, wenn Mieter Anliegen melden sollen oder Hausmeister und Handwerker eingebunden sind. Sonst nicht nötig.",
            ["Tickets", "Hausmeister-Aufgaben", "Hausmeister-Bereich", "Handwerker-Links", "Tickets im Mieterportal"],
            [], $"{Admin}/tickets", [$"{Admin}/tickets", "/service", "/auftrag", "/portal/tickets"], [], []),

        new(AppModule.Notices, "Mitteilungen & Aushänge",
            "Nachrichten mit Mietern austauschen und digitale Aushänge im Portal veröffentlichen.",
            "Sinnvoll, wenn Mieter über das Portal erreichbar sein sollen. Wer per E-Mail oder Brief kommuniziert, braucht es nicht.",
            ["Nachrichten", "Aushänge mit Lesebestätigung", "Kommunikation je Vertrag"],
            [], $"{Admin}/nachrichten", [$"{Admin}/nachrichten", $"{Admin}/aushaenge", "/portal/nachrichten", "/portal/aushaenge"],
            [($"{Admin}/vertraege", "kommunikation")], ["notice-notifications"]),

        new(AppModule.Documents, "Dokumente & Schreiben",
            "Dokumente pro Objekt, Vertrag und Person ablegen, Schreiben aus Vorlagen erstellen und Mietern im Portal bereitstellen.",
            "Sinnvoll als zentrale Ablage und für Serienschreiben. Reine Zahlenverwaltung kommt ohne aus.",
            ["Dokumentenablage", "Schreiben an Mieter", "Dokumente im Mieterportal"],
            [], $"{Admin}/dokumente", [$"{Admin}/dokumente", "/portal/dokumente"], [], ["document-notifications"]),

        new(AppModule.Deadlines, "Fristen & Wartung",
            "Fristen, Wartungen und Prüftermine im Kalender verfolgen und per Mail erinnert werden.",
            "Sinnvoll für Heizungswartung, Rauchmelder, Kündigungs- und Abrechnungsfristen. Wer das anderswo führt, braucht es nicht.",
            ["Kalender", "Wartungsaufgaben", "Erinnerungsmails und Wochenübersicht"],
            [], $"{Admin}/fristen", [$"{Admin}/fristen", $"{Admin}/einstellungen/fristen"], [], ["deadline-reminders"]),
    ];

    public const string RentModelGroup = "Mietmodelle";
    public const string BillingGroup = "Abrechnung der Nebenkosten";
    public const string TaxGroup = "Steuer";
    public const string UnitsGroup = "Vermietete Einheiten";
    public const string LeaseGroup = "Angaben im Mietvertrag";
    public const string DunningGroup = "Mahnwesen";

    public static readonly IReadOnlyList<OptionInfo> Options =
    [
        new(AppOption.RentModelGraduated, RentModelGroup, "Staffelmiete",
            "Die Miete steigt zu festen Terminen um feste Beträge (§ 557a BGB). Elocaro übernimmt die Staffeln automatisch.",
            [], [], ["graduated-rent"], AppModule.RentIncreases),
        new(AppOption.RentModelIndex, RentModelGroup, "Indexmiete",
            "Die Miete folgt dem Verbraucherpreisindex (§ 557b BGB). Schaltet die Seite „Verbraucherpreisindex“ und die Indexfelder frei.",
            [$"{Admin}/vpi"], [], [], AppModule.RentIncreases),
        new(AppOption.BillingPrepayment, BillingGroup, "Vorauszahlung mit jährlicher Abrechnung",
            "Mieter zahlen monatlich Vorauszahlungen für Betriebs- und Heizkosten, einmal im Jahr folgt die Abrechnung.",
            [], [], [], null),
        new(AppOption.BillingFlatRate, BillingGroup, "Betriebskostenpauschale",
            "Eine feste Pauschale ohne Abrechnung, z. B. bei WG-Zimmern.",
            [], [], [], null),
        new(AppOption.BillingInclusive, BillingGroup, "Inklusivmiete (Warmmiete)",
            "Die Nebenkosten sind in der Miete enthalten, es gibt keine Vorauszahlung.",
            [], [], [], null),
        new(AppOption.TaxLoans, TaxGroup, "Darlehen",
            "Darlehen für Objekte mit Tilgungsplan, Jahresbescheinigungen, Sondertilgungen und Zinsen für die Anlage V.",
            [$"{Admin}/finanzen/darlehen"], [], [], AppModule.Tax),
        new(AppOption.TaxDepreciation, TaxGroup, "Abschreibung (AfA) und Erhaltungsaufwand",
            "Anschaffungskosten und Abschreibung je Objekt sowie größere Reparaturen, die auf mehrere Jahre verteilt werden.",
            [], [($"{Admin}/objekte", "afa"), ($"{Admin}/objekte", "erhaltungsaufwand")], [], AppModule.Tax),
        new(AppOption.TaxDatev, TaxGroup, "DATEV-Export",
            "Buchungen und Belege für den Steuerberater im DATEV-Format ausgeben.",
            [$"{Admin}/finanzen/datev"], [], [], AppModule.Tax),
        new(AppOption.TaxOwnerPortal, TaxGroup, "Eigentümer-Portal",
            "Miteigentümer sehen ihre Objekte und die Anlage V in einem eigenen Zugang.",
            ["/eigentuemer"], [], [], AppModule.Tax),
        new(AppOption.RoomRental, UnitsGroup, "Zimmerweise Vermietung (WG)",
            "Eine Wohnung wird in einzelne Zimmer aufgeteilt, die einzeln vermietet werden.",
            [$"{Admin}/zimmer"], [], [], null),
        new(AppOption.UnitParking, UnitsGroup, "Garagen und Stellplätze als eigene Einheit",
            "Stellplätze erscheinen als Art einer Einheit und können eigene Verträge haben.",
            [], [], [], null),
        new(AppOption.UnitCommercial, UnitsGroup, "Gewerbeeinheiten",
            "Gewerbe erscheint als Art einer Einheit. Elocaro rechnet dafür keine gewerblichen Besonderheiten wie Umsatzsteuer.",
            [], [], [], null),
        new(AppOption.LeaseDeposit, LeaseGroup, "Kaution",
            "Kautionsbetrag und Notiz im Mietvertrag. Elocaro verwaltet kein Kautionskonto.",
            [], [], [], null),
        new(AppOption.LeaseGuarantor, LeaseGroup, "Bürgen",
            "Bürgen als weitere Vertragspartei neben den Mietern.",
            [], [], [], null),
        new(AppOption.DunningFees, DunningGroup, "Mahngebühren",
            "Gebühr für Zahlungserinnerung und Mahnungen. Zulässig sind nur tatsächlich entstandene Kosten.",
            [], [], [], AppModule.Dunning),
        new(AppOption.DunningInterest, DunningGroup, "Verzugszinsen",
            "Zinsen auf Rückstände ab der 1. Mahnung (Basiszinssatz plus Aufschlag, § 288 BGB). Der Basiszinssatz muss halbjährlich gepflegt werden.",
            [], [], [], AppModule.Dunning),
    ];

    public static OptionInfo Info(AppOption option) => Options.First(o => o.Option == option);

    /// <summary>The option that owns a path (e.g. the VPI page), or null.</summary>
    public static OptionInfo? OptionOf(string path)
    {
        path = path.TrimEnd('/');
        return Options.FirstOrDefault(o => o.Owns(path));
    }

    public static OptionInfo? OptionOfJob(string jobName) => Options.FirstOrDefault(o => o.Jobs.Contains(jobName));

    public static AppOption Of(RentModel model) => model switch
    {
        RentModel.Graduated => AppOption.RentModelGraduated,
        RentModel.Index => AppOption.RentModelIndex,
        _ => throw new ArgumentOutOfRangeException(nameof(model), "Die Vergleichsmiete ist immer verfügbar."),
    };

    public static AppOption Of(BillingType type) => type switch
    {
        BillingType.Prepayment => AppOption.BillingPrepayment,
        BillingType.FlatRate => AppOption.BillingFlatRate,
        _ => AppOption.BillingInclusive,
    };

    public static ModuleInfo Info(AppModule module) => All.First(m => m.Module == module);

    /// <summary>The module that owns a path, or null for core pages.</summary>
    public static ModuleInfo? OwnerOf(string path) => All.FirstOrDefault(m => m.Owns(path));

    /// <summary>The module a background job belongs to, or null.</summary>
    public static ModuleInfo? OwnerOfJob(string jobName) => All.FirstOrDefault(m => m.Jobs.Contains(jobName));

    /// <summary>The selection with all required modules switched on as well.</summary>
    public static HashSet<AppModule> WithRequirements(IEnumerable<AppModule> enabled)
    {
        var result = new HashSet<AppModule>(enabled);
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var module in result.ToArray())
            {
                foreach (var required in Info(module).Requires)
                {
                    changed |= result.Add(required);
                }
            }
        }

        return result;
    }
}

/// <summary>Marks a dashboard todo source with the module it belongs to; the dashboard skips it while the module is off.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ModuleAttribute(AppModule module) : Attribute
{
    public AppModule Module { get; } = module;

    /// <summary>The module a type belongs to, or null for core types.</summary>
    public static AppModule? Of(Type type) => ((ModuleAttribute?)GetCustomAttribute(type, typeof(ModuleAttribute)))?.Module;
}
