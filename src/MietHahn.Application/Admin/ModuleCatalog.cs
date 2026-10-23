using MietHahn.Domain.Leases;

namespace MietHahn.Application.Admin;

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
}

/// <param name="Question">Short question for the wizard that the options of one <paramref name="Group"/> answer together.</param>
public sealed record OptionInfo(AppOption Option, string Group, string Title, string Description, string[] Prefixes, string[] Jobs, AppModule? Module);

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
    public bool Owns(string path)
    {
        path = path.TrimEnd('/');
        if (Prefixes.Any(prefix => path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        foreach (var (root, segment) in EntityPages)
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
            ["Belegablage", "Kostenpositionen", "Belegauslesung"],
            [], $"{Admin}/belege", [$"{Admin}/belege", $"{Admin}/system/belegauslesung"], [], []),

        new(AppModule.OperatingCosts, "Nebenkostenabrechnung",
            "Betriebs- und Heizkosten auf die Mieter umlegen und die jährliche Abrechnung erstellen.",
            "Sinnvoll, wenn Sie Vorauszahlungen für Betriebskosten erheben und jährlich abrechnen müssen. Bei Pauschalen ohne Abrechnung nicht nötig.",
            ["Umlageschlüssel", "Heizkosten", "Vorschau und Abrechnungs-Assistent", "Vereinbarte Kostenarten je Vertrag"],
            [AppModule.Receipts], $"{Admin}/objekte", [$"{Admin}/abrechnungen"],
            [($"{Admin}/objekte", "umlageschluessel"), ($"{Admin}/objekte", "heizung"), ($"{Admin}/objekte", "nebenkosten"),
             ($"{Admin}/objekte", "abrechnung"), ($"{Admin}/objekte", "abrechnungen"), ($"{Admin}/vertraege", "kostenarten")], []),

        new(AppModule.Metering, "Zähler & Verbrauch",
            "Zähler und Zählerstände erfassen, importieren und Mietern ihren Verbrauch im Portal zeigen.",
            "Sinnvoll bei verbrauchsabhängiger Abrechnung (Wasser, Heizung, Strom) oder wenn Mieter ihren Verbrauch einsehen sollen.",
            ["Zähler und Ablesungen", "CSV-Import von Funkablesesystemen", "Monatliche Verbrauchsinformation", "Verbrauch im Mieterportal"],
            [], $"{Admin}/objekte", [$"{Admin}/zaehler", "/portal/verbrauch"],
            [($"{Admin}/objekte", "zaehler"), ($"{Admin}/objekte", "ablesung"), ($"{Admin}/objekte", "zaehlerimport")], ["consumption-information"]),

        new(AppModule.RentIncreases, "Mieterhöhung & Indexmiete",
            "Mieterhöhungen vorbereiten, Staffelmieten übernehmen und Indexmieten mit dem Verbraucherpreisindex (VPI) anpassen.",
            "Sinnvoll, wenn Sie Staffel- oder Indexmietverträge haben oder Mieten regelmäßig anheben. Mit reinen Festmieten nicht nötig.",
            ["Verbraucherpreisindex", "Mieterhöhungs-Schreiben", "automatische Übernahme fälliger Staffeln"],
            [], $"{Admin}/vertraege", [$"{Admin}/vpi"], [($"{Admin}/vertraege", "mieterhoehung")], ["graduated-rent"]),

        new(AppModule.Tax, "Steuer & Eigentümer",
            "Anlage V, Abschreibung, Erhaltungsaufwand, Darlehen und DATEV-Export; Einsicht für Eigentümer.",
            "Sinnvoll, wenn Sie Ihre Einkünfte aus Vermietung mit MietHahn vorbereiten oder Eigentümer informieren. Wer die Steuer anderweitig erledigt, braucht es nicht.",
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

    public static readonly IReadOnlyList<OptionInfo> Options =
    [
        new(AppOption.RentModelGraduated, RentModelGroup, "Staffelmiete",
            "Die Miete steigt zu festen Terminen um feste Beträge (§ 557a BGB). MietHahn übernimmt die Staffeln automatisch.",
            [], ["graduated-rent"], AppModule.RentIncreases),
        new(AppOption.RentModelIndex, RentModelGroup, "Indexmiete",
            "Die Miete folgt dem Verbraucherpreisindex (§ 557b BGB). Schaltet die Seite „Verbraucherpreisindex“ und die Indexfelder frei.",
            [$"{Admin}/vpi"], [], AppModule.RentIncreases),
        new(AppOption.BillingPrepayment, BillingGroup, "Vorauszahlung mit jährlicher Abrechnung",
            "Mieter zahlen monatlich Vorauszahlungen für Betriebs- und Heizkosten, einmal im Jahr folgt die Abrechnung.",
            [], [], null),
        new(AppOption.BillingFlatRate, BillingGroup, "Betriebskostenpauschale",
            "Eine feste Pauschale ohne Abrechnung, z. B. bei WG-Zimmern.",
            [], [], null),
        new(AppOption.BillingInclusive, BillingGroup, "Inklusivmiete (Warmmiete)",
            "Die Nebenkosten sind in der Miete enthalten, es gibt keine Vorauszahlung.",
            [], [], null),
    ];

    public static OptionInfo Info(AppOption option) => Options.First(o => o.Option == option);

    /// <summary>The option that owns a path (e.g. the VPI page), or null.</summary>
    public static OptionInfo? OptionOf(string path)
    {
        path = path.TrimEnd('/');
        return Options.FirstOrDefault(o => o.Prefixes.Any(prefix => path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)));
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
