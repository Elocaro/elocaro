namespace Elocaro.Web.Components.Common;

/// <summary>A block of an info dialog: a heading and a few short points.</summary>
public sealed record InfoSection(string Heading, params string[] Items);

/// <summary>A related page offered at the end of an info dialog.</summary>
public sealed record InfoLink(string Text, string Href);

/// <summary>Additional information about the content of a page, shown in a dialog behind the info button in the page header.</summary>
/// <param name="Title">Heading of the dialog.</param>
/// <param name="Intro">One or two sentences: what the page is and what it is for.</param>
public sealed record PageInfo(string Title, string Intro, InfoSection[] Sections, InfoLink[]? Links = null);

/// <summary>
/// The info texts by page. A page is found by its path in which ids are replaced by <c>{id}</c> (for example
/// <c>/verwaltung/objekte/{id}/zaehler</c>); pages without an entry have no info button.
/// </summary>
public static class PageInfoCatalog
{
    private const string Admin = "/verwaltung";
    private const string Id = "/{id}";

    private static readonly Dictionary<string, PageInfo> Pages = Build();

    /// <summary>All paths that have an info text.</summary>
    public static IReadOnlyCollection<string> Paths => Pages.Keys;

    /// <summary>The info for the page at <paramref name="path"/> (without query), or null.</summary>
    public static PageInfo? For(string path) => Pages.GetValueOrDefault(Normalize(path));

    /// <summary>Lower case, no trailing slash, ids as <c>{id}</c>.</summary>
    public static string Normalize(string path)
    {
        var parts = path.Trim().TrimEnd('/').ToLowerInvariant().Split('/');
        for (var i = 0; i < parts.Length; i++)
        {
            if (Guid.TryParse(parts[i], out _))
            {
                parts[i] = "{id}";
            }
        }

        return string.Join('/', parts);
    }

    private static Dictionary<string, PageInfo> Build()
    {
        var pages = new Dictionary<string, PageInfo>(StringComparer.Ordinal);

        void Add(PageInfo info, params string[] paths)
        {
            foreach (var path in paths)
            {
                pages.Add(path, info);
            }
        }

        // ---- Overview
        Add(new("Was ist zu tun?",
            "Die Übersicht sammelt Aufgaben und Fristen aus allen eingeschalteten Bereichen, die dringendsten zuerst.",
            [
                new("Woher kommen die Einträge?",
                    "Offene Posten und Mahnvorschläge, Fristen und Wartungen, Tickets und Nachrichten.",
                    "Bankumsätze, die noch keinem Mieter oder keiner Kostenart zugeordnet sind.",
                    "Belege ohne Kostenaufteilung, auslaufende Dokumente, fällige Staffeln und Indexanpassungen.",
                    "Hinweise zum Betrieb, etwa eine fehlgeschlagene Datensicherung."),
                new("Gut zu wissen",
                    "Ein Eintrag verschwindet von selbst, sobald die Aufgabe erledigt ist.",
                    "Bereiche, die Sie nicht nutzen, schalten Sie unter Einstellungen → Module ab. Ihre Aufgaben erscheinen dann hier nicht mehr."),
            ],
            [new("Module ein- und ausschalten", $"{Admin}/einstellungen/module")]), Admin);

        // ---- Properties
        Add(new("Objekte, Einheiten und Zimmer",
            "Ein Objekt ist ein Haus oder eine Wohnanlage. Darunter liegen die Einheiten (Wohnung, Gewerbe, Stellplatz), die vermietet werden.",
            [
                new("So hängt es zusammen",
                    "Objekt → Einheit → Mietvertrag. Bei WG-Vermietung (wenn aktiviert) liegen Zimmer zwischen Einheit und Vertrag.",
                    "Anlegen Sie zuerst das Objekt und seine Einheiten, dann die Personen und zuletzt den Mietvertrag."),
                new("Wofür die Angaben gebraucht werden",
                    "Wohnflächen sind die Grundlage für die Umlage nach Fläche in der Nebenkostenabrechnung.",
                    "Heizungsart und Energieträger bestimmen die Heizkosten- und CO₂-Kostenaufteilung.",
                    "Die Eigentümer mit ihren Anteilen brauchen Sie für die Anlage V.",
                    "Das Vermieterprofil des Objekts bestimmt den Absender auf Schreiben und Abrechnungen."),
            ]), $"{Admin}/objekte");

        Add(new("Objekt anlegen",
            "Es genügen Name und Adresse. Alles Weitere ergänzen Sie danach im Objekt.",
            [
                new("Gleich danach",
                    "Einheiten anlegen (Wohnungen, Gewerbe, Stellplätze).",
                    "Wohnflächen mit Stichtag eintragen, wenn nach Fläche umgelegt werden soll.",
                    "Heizungsart und Energieträger angeben, wenn Heizkosten abgerechnet werden.",
                    "Eigentümer eintragen, wenn Sie die Anlage V vorbereiten."),
            ]), $"{Admin}/objekte/neu");

        Add(new("Das Objekt im Überblick",
            "Die Seite bündelt alles zu einem Haus. Oben stehen Hinweise auf fehlende Angaben, darunter die Reiter.",
            [
                new("Die Reiter",
                    "Einheiten: alle Wohnungen und Co. mit Belegung. Leerstand erkennen Sie am gelben Hinweis.",
                    "Flächen: Wohnfläche und Nebenflächen mit Gültigkeitsdatum. Ändert sich eine Fläche, tragen Sie die neue mit Stichtag ein, die alte bleibt für frühere Abrechnungen erhalten.",
                    "Eigentümer: Anteile für die Anlage V und die Sicht der Eigentümer.",
                    "Angaben, Dokumente und Verlauf (wer hat wann was geändert)."),
                new("Hinweise „Bitte prüfen“",
                    "Fehlt die Wohnfläche, kann nach Fläche nicht umgelegt werden. Tragen Sie sie im Reiter Flächen nach."),
            ],
            [new("Zähler", $"{Admin}/objekte/{{id}}/zaehler")]), $"{Admin}/objekte/{{id}}");

        Add(new("Einheit",
            "Eine Einheit ist das, was vermietet wird: eine Wohnung, ein Gewerberaum oder ein Stellplatz.",
            [
                new("Was Sie hier finden",
                    "Die Belegung mit der Vertragshistorie: wer wohnt oder wohnte wann hier.",
                    "Flächen und Ausstattung der Einheit.",
                    "Bei zimmerweiser Vermietung die Zimmer. Danach sind nur noch die Zimmer einzeln vermietbar."),
            ]), $"{Admin}/einheiten/{{id}}", $"{Admin}/zimmer/{{id}}");

        // ---- Metering
        Add(new("Zähler",
            "Hier verwalten Sie die Zähler eines Objekts. Ihre Stände sind die Grundlage für Heizkosten, Wasser und die Verbrauchsinformation der Mieter.",
            [
                new("Gut zu wissen",
                    "Jeder Zähler gehört zu einem Objekt, einer Einheit oder einem Zimmer.",
                    "Stände erfassen Sie in einer Ablese-Runde oder importieren sie aus dem CSV-Export eines Funkablesesystems.",
                    "Eichfristen werden als Frist überwacht und erscheinen in der Übersicht.",
                    "Für die Abrechnung brauchen Sie einen Anfangs- und einen Endstand zum Abrechnungszeitraum."),
            ]), $"{Admin}/objekte/{{id}}/zaehler", $"{Admin}/zaehler/{{id}}");

        Add(new("Ablese-Runde",
            "Die Runde führt Zähler für Zähler durch die Ablesung zu einem Stichtag.",
            [
                new("So gehen Sie vor",
                    "Gehen Sie die Zähler der Reihe nach durch. Was Sie nicht ablesen konnten, überspringen Sie und tragen es später nach.",
                    "Am Ende zeigt eine Zusammenfassung alle erfassten und fehlenden Stände.",
                    "Zum Jahreswechsel brauchen Sie Stände zum Beginn und zum Ende des Abrechnungszeitraums, zwischendurch genügen Zwischenablesungen."),
            ]), $"{Admin}/objekte/{{id}}/ablesung");

        Add(new("Zählerstände importieren",
            "Funkablesesysteme liefern ihre Werte als CSV-Datei. Der Import ordnet sie Ihren Zählern zu.",
            [
                new("Tipps",
                    "Die Zuordnung erfolgt über die Zählernummer. Pflegen Sie die Nummern vorher vollständig in der Zählerliste.",
                    "Vor dem Übernehmen sehen Sie je Zeile, ob sie passt, und können Fehler beheben.",
                    "Prüfen Sie nach dem Import die Stände in der Zählerliste, bevor Sie abrechnen."),
            ]), $"{Admin}/objekte/{{id}}/zaehlerimport");

        Add(new("Kassenbuch importieren",
            "Ein Kassenbuch oder eine Kostenliste als CSV wird zu Belegen mit je einer Kostenposition.",
            [
                new("Tipps",
                    "Die Spalten Datum, Gruppe, Empfänger, Betrag und Info werden an der Kopfzeile erkannt. Aus Excel speichern Sie die Tabelle als CSV.",
                    "Ordnen Sie jede Gruppe einer Kostenart zu; Gruppen ohne Kostenart werden übersprungen.",
                    "Ein erneuter Import legt nichts doppelt an: Zeilen mit gleichem Datum, Empfänger und Betrag gelten als schon erfasst."),
            ]), $"{Admin}/objekte/{{id}}/belegimport");

        Add(new("Belegung",
            "Die Zeitleiste zeigt je Wohnung und WG-Zimmer, wer wann gemietet hat und wo Leerstand war.",
            [
                new("Gut zu wissen",
                    "Grün sind unbefristet laufende Verträge, gelb befristete oder gekündigte, grau beendete. Schraffierte Flächen sind Leerstand.",
                    "Ein Klick auf einen Balken öffnet den Vertrag; die senkrechte Linie zeigt den heutigen Tag.",
                    "Wechseln Sie mit den Pfeilen oben das Jahr. Unter „Als Tabelle“ stehen dieselben Angaben als Liste."),
            ]), $"{Admin}/objekte/{{id}}/belegung");

        // ---- Operating costs
        Add(new("Versorgungsverträge",
            "Wasser, Abwasser, Strom und Gas rechnet Elocaro nach Verbrauch und Preis, nicht nach den Zahlungen an den Versorger.",
            [
                new("Gut zu wissen",
                    "Die Rechnungen der Versorger kommen oft Monate nach dem Jahresende. Mit dem Tarif lässt sich die Abrechnung trotzdem rechnen.",
                    "Abschlagsrechnungen sind keine Kosten. Erfassen Sie später die Jahresabrechnung als Beleg mit der Markierung „Jahresabrechnung des Versorgers“; sie ersetzt die Berechnung.",
                    "Ändert sich der Preis, legen Sie einen neuen Tarif mit Stichtag an; im Abrechnungszeitraum wird tagesgenau geteilt.",
                    "Der Verbrauch kommt aus den Zählerständen der gewählten Zähler. Der Tarif muss den ganzen Abrechnungszeitraum abdecken, sonst gelten die Belege."),
            ]), $"{Admin}/objekte/{{id}}/versorgung");

        Add(new("Umlageschlüssel",
            "Der Umlageschlüssel bestimmt, wie eine Kostenart auf die Mieter verteilt wird, etwa nach Wohnfläche, Personenzahl, Verbrauch oder je Einheit.",
            [
                new("Gut zu wissen",
                    "Maßgeblich ist, was im Mietvertrag vereinbart ist. Abweichende Vereinbarungen pflegen Sie am Vertrag unter „Vereinbarte Kostenarten“.",
                    "Ändern Sie einen Schlüssel mit Gültigkeitsdatum: frühere Abrechnungen bleiben nachvollziehbar.",
                    "Das Rechenprotokoll der Abrechnung zeigt je Kostenart, wie verteilt wurde."),
            ]), $"{Admin}/objekte/{{id}}/umlageschluessel");

        Add(new("Heizkosten (HeizkostenV)",
            "Bei einer zentralen Heizanlage gilt die Heizkostenverordnung: Die Kosten werden teils nach Verbrauch und teils nach Fläche verteilt.",
            [
                new("Die Regeln in Kürze",
                    "Zwischen 50 und 70 % der Kosten werden nach Verbrauch verteilt, der Rest nach Fläche. Eine Abrechnung ausschließlich nach Verbrauch ist nicht zulässig.",
                    "Bei gemeinsamer Anlage für Heizung und Warmwasser wird der Brennstoff nach § 9 HeizkostenV aufgeteilt.",
                    "Die CO₂-Kosten teilen sich Vermieter und Mieter nach einem Stufenmodell: Je höher der Ausstoß je m² Wohnfläche, desto größer ist der Vermieteranteil."),
                new("Hinweis",
                    "Stufentabelle und Gradtagszahlen sind versionierte Daten. Prüfen Sie sie vor dem Versand einer Abrechnung gegen die amtlichen Quellen."),
            ]), $"{Admin}/objekte/{{id}}/heizung");

        Add(new("Vorschau der Nebenkosten",
            "Die Vorschau rechnet die Abrechnung eines Jahres durch, ohne etwas festzulegen. Sie können sie beliebig oft öffnen.",
            [
                new("Gut zu wissen",
                    "Im Rechenprotokoll sehen Sie für jede Kostenart, welche Belege eingeflossen sind und wie verteilt wurde.",
                    "Fehlende Belege, Zähler oder Flächen führen zu Hinweisen. Beheben Sie diese, bevor Sie im Abrechnungs-Assistenten freigeben."),
            ],
            [new("Abrechnungs-Assistent", $"{Admin}/objekte/{{id}}/abrechnung")]), $"{Admin}/objekte/{{id}}/nebenkosten");

        Add(new("Abrechnungs-Assistent",
            "Der Assistent führt in sechs Schritten durch die Prüfung und gibt die Abrechnung am Ende frei.",
            [
                new("Die Frist",
                    "Die Abrechnung muss dem Mieter spätestens zwölf Monate nach Ende des Abrechnungszeitraums zugehen (§ 556 Abs. 3 BGB). Danach sind Nachforderungen in der Regel ausgeschlossen, Guthaben bleiben fällig."),
                new("Die Freigabe",
                    "Mit der Freigabe wird die Abrechnung eingefroren. Spätere Korrekturen erzeugen eine neue Fassung mit Differenz-Abrechnung und brauchen eine Begründung.",
                    "Hinweise sollten Sie vorher prüfen. Fehlende Pflichtangaben sperren die Freigabe."),
            ],
            [new("Freigegebene Abrechnungen", $"{Admin}/objekte/{{id}}/abrechnungen")]), $"{Admin}/objekte/{{id}}/abrechnung");

        Add(new("Abrechnungen",
            "Alle freigegebenen Fassungen der Nebenkostenabrechnung dieses Objekts.",
            [
                new("Gut zu wissen",
                    "Eine freigegebene Fassung ist unveränderlich. Korrekturen entstehen als neue Fassung.",
                    "Versendet wird je Mietverhältnis. Der Zugang beim Mieter wird mit Datum und Nachweis vermerkt, das ist wichtig für die Frist.",
                    "Nach der Abrechnung schlägt Elocaro eine Anpassung der Vorauszahlungen vor."),
            ]), $"{Admin}/objekte/{{id}}/abrechnungen", $"{Admin}/abrechnungen/{{id}}");

        // ---- Tax
        Add(new("Abschreibung (AfA)",
            "Die Anschaffungs- oder Herstellungskosten eines Gebäudes werden über Jahre als Werbungskosten abgeschrieben.",
            [
                new("Gut zu wissen",
                    "Abgeschrieben wird nur der Gebäudeanteil, nicht der Grundstücksanteil.",
                    "Der Satz richtet sich nach Baujahr und Art des Gebäudes. Bei Wohngebäuden beträgt er häufig 2 % im Jahr.",
                    "Das Ergebnis fließt in die Anlage V ein. Lassen Sie Aufteilung und Satz vom Steuerberater bestätigen."),
            ]), $"{Admin}/objekte/{{id}}/afa");

        Add(new("Erhaltungsaufwand",
            "Reparaturen und Instandhaltung sind Werbungskosten. Größere Maßnahmen können auf mehrere Jahre verteilt werden.",
            [
                new("Gut zu wissen",
                    "Bei Wohngebäuden ist eine Verteilung auf zwei bis fünf Jahre möglich. Bereits verteilte Beträge lassen sich zurücknehmen.",
                    "Hohe Aufwendungen kurz nach dem Kauf können als „anschaffungsnaher Aufwand“ zu den Anschaffungskosten zählen. Das klären Sie am besten mit dem Steuerberater."),
            ]), $"{Admin}/objekte/{{id}}/erhaltungsaufwand");

        Add(new("Anlage V",
            "Die Anlage V der Steuererklärung erfasst die Einkünfte aus Vermietung und Verpachtung. Hier sehen Sie die vorbereiteten Werte je Objekt und Jahr.",
            [
                new("Woher die Zahlen kommen",
                    "Einnahmen aus den gebuchten Mieten und Zahlungen, Ausgaben aus den Belegen, dazu AfA, verteilter Erhaltungsaufwand und Darlehenszinsen.",
                    "Die Zuordnung der Kostenarten zu den Zeilen der Anlage V steht unter Darlehen & Steuer → Anlage-V-Zeilen.",
                    "Bei mehreren Eigentümern wählen Sie den Eigentümer, die Zahlen werden nach Anteil gerechnet."),
                new("Hinweis",
                    "Die Werte sind eine Vorbereitung. Reichen Sie die Steuererklärung nach Prüfung selbst oder über Ihren Steuerberater ein."),
            ]), $"{Admin}/objekte/{{id}}/anlage-v");

        Add(new("Darlehen",
            "Darlehen für Ihre Objekte mit Tilgungsplan. Die Zinsen sind Werbungskosten in der Anlage V.",
            [
                new("Gut zu wissen",
                    "Jahresbescheinigungen der Bank und Sondertilgungen pflegen Sie am Darlehen.",
                    "Das Ende der Zinsbindung wird als Frist überwacht.",
                    "Die Zinsen des Jahres übernimmt die Anlage V automatisch."),
            ]), $"{Admin}/finanzen/darlehen", $"{Admin}/finanzen/darlehen/{{id}}");

        Add(new("Anlage-V-Zeilen",
            "Hier legen Sie fest, welche Kostenart in welcher Zeile der Anlage V landet.",
            [
                new("Gut zu wissen",
                    "Die Voreinstellung deckt die üblichen Kostenarten ab. Ändern müssen Sie nur Abweichungen, etwa wenn Ihr Steuerberater eine Kostenart anders einordnet.",
                    "Änderungen wirken auf alle Auswertungen, die Sie danach öffnen."),
            ]), $"{Admin}/finanzen/anlage-v-zeilen");

        Add(new("DATEV-Export",
            "Der Export liefert einen Buchungsstapel für den Steuerberater: erhaltene Mieten und bezahlte Belege nach Zahlungsdatum.",
            [
                new("Gut zu wissen",
                    "Fragen Sie Ihren Steuerberater, welche Konten und welchen Zeitraum er braucht.",
                    "Exportieren Sie erst, wenn Belege und Zahlungen des Zeitraums zugeordnet sind. Was fehlt, fehlt auch im Export."),
            ]), $"{Admin}/finanzen/datev");

        // ---- People
        Add(new("Personen",
            "Mieter, Bürgen und Kontakte wie Hausmeister, Handwerker, Versorger oder Ämter.",
            [
                new("Gut zu wissen",
                    "Eine Person wird erst durch ihre Rolle zum Mieter: Sie ist Partei in einem Mietvertrag.",
                    "Kontaktarten wie Hausmeister oder Handwerker brauchen Sie für Tickets und Aufgaben.",
                    "Unter „Änderungen“ sehen Sie Kontaktänderungen, die Mieter im Portal vorschlagen."),
                new("Datenschutz",
                    "Personen, deren Daten nicht mehr benötigt werden, schlägt Elocaro unter „Datenschutz“ zur Anonymisierung vor."),
            ]), $"{Admin}/personen", $"{Admin}/personen/neu", $"{Admin}/personen/{{id}}");

        Add(new("Änderungen der Kontaktdaten",
            "Mieter können im Portal neue Kontaktdaten vorschlagen. Sie übernehmen sie hier oder lehnen sie ab.",
            [new("Gut zu wissen", "Bis zur Freigabe bleiben die bisherigen Daten gültig. So kann niemand fremde Daten unbemerkt ändern.")]), $"{Admin}/personen/aenderungen");

        Add(new("Datenschutz",
            "Personenbezogene Daten dürfen nur so lange gespeichert werden, wie sie gebraucht werden. Hier erscheinen Personen, bei denen die Aufbewahrungsfrist abgelaufen ist.",
            [
                new("Gut zu wissen",
                    "Die Anonymisierung entfernt personenbezogene Angaben, die Zahlen in Abrechnungen und Auswertungen bleiben erhalten.",
                    "Sie ist nicht umkehrbar. Elocaro fragt deshalb vorher nach."),
            ]), $"{Admin}/datenschutz");

        // ---- Leases
        Add(new("Mietverträge",
            "Alle Mietverhältnisse mit Objekt, Mieter, Laufzeit und Miete.",
            [
                new("Gut zu wissen",
                    "Ein Vertrag verbindet eine Einheit (oder ein Zimmer) mit einem oder mehreren Mietern. Pro Einheit kann es zu einem Zeitpunkt nur einen laufenden Vertrag geben.",
                    "Miete, Nebenkosten und Personenzahl haben ein Gültigkeitsdatum. So bleibt die Mietentwicklung nachvollziehbar.",
                    "Aus dem Vertrag entstehen die Sollstellungen auf dem Mieterkonto."),
            ]), $"{Admin}/vertraege");

        Add(new("Mietvertrag anlegen",
            "Das Formular nimmt nur auf, was für den Start nötig ist. Alles Weitere ergänzen Sie danach im Vertrag.",
            [
                new("Gut zu wissen",
                    "Die Nebenkosten-Art bestimmt die Felder: Vorauszahlung mit Abrechnung, Pauschale oder Inklusivmiete.",
                    "Welche Mietmodelle, Kaution und Bürgen das Formular anbietet, stellen Sie unter Einstellungen → Module ein.",
                    "Fehlt eine Person, legen Sie sie direkt hier über „Neue Person anlegen“ an."),
            ]), $"{Admin}/vertraege/neu");

        Add(new("Der Mietvertrag",
            "Die Seite zeigt alle Angaben eines Mietverhältnisses, geordnet in Reiter.",
            [
                new("Die Reiter",
                    "Miete: Bestandteile mit Gültigkeitsdatum, Mietmodell und Anpassungen.",
                    "Parteien & Haushalt: Mieter, Bürgen und die Personenzahl für die Umlage nach Personen.",
                    "Kündigung, Angaben, Dokumente und Verlauf.",
                    "Oben stehen die weiteren Seiten des Vertrags, etwa Konto, Vertragsdokument, Kommunikation und Ein- & Auszug."),
                new("Kündigungsfristen",
                    "Mieter kündigen mit drei Monaten Frist. Für Vermieter verlängert sich die Frist mit der Mietdauer (§ 573c BGB), und sie brauchen einen Kündigungsgrund (§ 573 BGB)."),
            ]), $"{Admin}/vertraege/{{id}}");

        Add(new("Mieterkonto",
            "Das Mieterkonto zeigt alle Forderungen und Zahlungen eines Mietverhältnisses.",
            [
                new("Gut zu wissen",
                    "Die Monatsmieten entstehen automatisch als Sollstellungen, sobald unter Forderungen → Einstellungen ein Buchhaltungsbeginn gesetzt ist.",
                    "Zahlungen aus dem Bankabgleich werden den Forderungen zugeordnet. Teilzahlungen verrechnet Elocaro nach der gewählten Reihenfolge.",
                    "Rückstände aus der Zeit vor dem Buchhaltungsbeginn erfassen Sie als Saldovortrag."),
            ]), $"{Admin}/vertraege/{{id}}/konto");

        Add(new("Vertragsdokument",
            "Aus den Vertragsdaten entsteht das Mietvertragsdokument als PDF.",
            [new("Gut zu wissen",
                "Das Dokument nutzt die Vorlage und das Vermieterprofil des Objekts. Prüfen Sie beides unter Einstellungen, bevor Sie es versenden.",
                "Das erzeugte PDF wird bei den Dokumenten des Vertrags abgelegt.")]), $"{Admin}/vertraege/{{id}}/vertragsdokument");

        Add(new("Mieterhöhung",
            "Hier bereiten Sie eine Mieterhöhung für dieses Mietverhältnis vor.",
            [
                new("Die Mietmodelle",
                    "Vergleichsmiete (§ 558 BGB): Der Mieter muss der Erhöhung zustimmen. Frühestens 15 Monate nach Einzug oder letzter Erhöhung wirksam, höchstens 20 % in drei Jahren (15 % in angespannten Märkten).",
                    "Staffelmiete (§ 557a BGB): feste Beträge zu festen Terminen, jeweils mindestens ein Jahr unverändert.",
                    "Indexmiete (§ 557b BGB): Anpassung nach dem Verbraucherpreisindex, frühestens ein Jahr nach der letzten Änderung."),
                new("Gut zu wissen",
                    "Erklärungen gehen in Textform zu. Bewahren Sie Schreiben und Zugangsnachweis bei den Dokumenten auf."),
            ]), $"{Admin}/vertraege/{{id}}/mieterhoehung");

        Add(new("Verbraucherpreisindex (VPI)",
            "Der VPI misst die Preisentwicklung eines typischen Warenkorbs. Er ist die Grundlage der Indexmiete.",
            [
                new("Gut zu wissen",
                    "Pflegen Sie die monatlichen Werte hier, oder importieren Sie die Tabelle des Statistischen Bundesamts.",
                    "Die Anpassung einer Indexmiete rechnet Elocaro aus Ausgangsindex und neuestem Wert. Die Änderung der Miete in Prozent entspricht der Änderung des Index.",
                    "Zwischen zwei Anpassungen muss mindestens ein Jahr liegen. Die neue Miete gilt ab dem übernächsten Monat nach Zugang der Erklärung."),
            ]), $"{Admin}/vpi");

        Add(new("Kommunikation",
            "Nachrichten und Schreiben zu diesem Mietverhältnis an einem Ort.",
            [new("Gut zu wissen", "Mieter lesen Nachrichten im Portal, Antworten erscheinen in derselben Unterhaltung.")]), $"{Admin}/vertraege/{{id}}/kommunikation");

        Add(new("Vereinbarte Kostenarten",
            "Welche Betriebskosten sind mit diesem Mieter vereinbart? Nur vereinbarte Kostenarten dürfen umgelegt werden.",
            [new("Gut zu wissen",
                "Nicht vereinbarte Kosten tauchen in der Abrechnung dieses Mieters nicht auf, sie bleiben beim Vermieter.",
                "Abweichende Umlageschlüssel pflegen Sie hier je Kostenart.")]), $"{Admin}/vertraege/{{id}}/kostenarten");

        Add(new("Übergabeprotokoll",
            "Das Protokoll hält den Zustand der Wohnung bei Ein- oder Auszug fest.",
            [new("Gut zu wissen",
                "Tragen Sie Zählerstände, Schlüssel und Mängel je Raum ein. Das Protokoll lässt sich auf dem Handy ausfüllen und unterschreiben.",
                "Es ist die Grundlage für spätere Streitfälle, etwa zur Kaution. Fotos helfen.")]), $"{Admin}/vertraege/{{id}}/uebergabe/einzug", $"{Admin}/vertraege/{{id}}/uebergabe/auszug");

        Add(new("Mieterwechsel",
            "Beim Auszug erfassen Sie Datum und neue Adresse. Von hier aus geht es weiter zum neuen Vertrag.",
            [new("Gut zu wissen",
                "Die Zählerstände zum Auszug brauchen Sie für die Abrechnung des Auszugsjahres.",
                "Die neue Adresse des Mieters benötigen Sie für die Nebenkostenabrechnung nach dem Auszug.")]), $"{Admin}/vertraege/{{id}}/auszug");

        // ---- Documents and communication
        Add(new("Dokumente",
            "Die zentrale Ablage für Dateien zu Objekten, Einheiten, Verträgen und Personen.",
            [
                new("Gut zu wissen",
                    "Jedes Dokument gehört zu einem Bezug und hat eine Kategorie. Für Dokumente mit Ablaufdatum, etwa Versicherungen, erinnert Elocaro rechtzeitig.",
                    "Dokumente lassen sich für den Mieter im Portal freigeben. Mieter bekommen dann eine Benachrichtigung.",
                    "Ausführbare Dateien werden nicht angenommen."),
            ]), $"{Admin}/dokumente", $"{Admin}/dokumente/neu", $"{Admin}/dokumente/{{id}}");

        Add(new("Schreiben an Mieter",
            "Ein Schreiben aus einer Vorlage an einen oder alle Mieter eines Objekts. Jedes Schreiben wird als PDF bei den Dokumenten abgelegt.",
            [new("Gut zu wissen",
                "Die Vorlagen bearbeiten Sie unter Einstellungen → Vorlagen. Platzhalter wie Name und Adresse füllt Elocaro selbst.",
                "Absender und Briefkopf kommen aus dem Vermieterprofil des Objekts.")],
            [new("Vorlagen", $"{Admin}/einstellungen/vorlagen")]), $"{Admin}/dokumente/schreiben");

        Add(new("Nachrichten",
            "Nachrichten an Mieter über das Mieterportal. Antworten landen in derselben Unterhaltung.",
            [new("Gut zu wissen",
                "Mieter bekommen eine Mail als Hinweis, den Text lesen sie im Portal. So bleibt die Kommunikation nachvollziehbar.",
                "Offene Antworten erscheinen in der Übersicht.")]), $"{Admin}/nachrichten", $"{Admin}/nachrichten/neu", $"{Admin}/nachrichten/{{id}}");

        Add(new("Aushänge",
            "Ein Aushang ist eine Mitteilung an alle Mieter eines Objekts, im Portal und per Mail, etwa die Ankündigung von Bauarbeiten.",
            [new("Gut zu wissen",
                "Mieter können das Lesen bestätigen. Wer noch nicht bestätigt hat, sehen Sie am Aushang.",
                "Ein Aushang lässt sich zurückziehen. Bereits versendete Mails bleiben natürlich bestehen.")]), $"{Admin}/aushaenge", $"{Admin}/aushaenge/neu", $"{Admin}/aushaenge/{{id}}");

        // ---- Tickets
        Add(new("Tickets",
            "Ein Ticket hält einen Schaden, Auftrag oder eine Anfrage fest, weist ihn einer Person zu und begleitet ihn bis zur Erledigung.",
            [
                new("Gut zu wissen",
                    "Mieter können Tickets im Portal selbst melden. Hausmeister und Handwerker arbeiten über ihren eigenen Bereich.",
                    "Handwerker bekommen auf Wunsch einen Link und sehen nur ihren Auftrag.",
                    "Fotos und Notizen am Ticket helfen später bei Rechnung und Versicherung."),
            ]), $"{Admin}/tickets", $"{Admin}/tickets/neu", $"{Admin}/tickets/{{id}}");

        Add(new("Hausmeister-Aufgaben",
            "Regelmäßige Aufgaben je Objekt, die der Hausmeister im Service-Bereich abhakt, etwa Treppenhausreinigung oder Rauchmelder-Sichtprüfung.",
            [new("Gut zu wissen",
                "Jede Aufgabe hat einen Rhythmus. Offene Prüfungen erscheinen in der Übersicht.",
                "Der Hausmeister sieht nur die Objekte, die ihm zugewiesen sind.")]), $"{Admin}/tickets/routinen", $"{Admin}/tickets/routinen/{{id}}");

        // ---- Deadlines
        Add(new("Fristen und Wartung",
            "Alles, was ansteht: abgeleitet aus Verträgen, Zählern, Darlehen und Dokumenten, dazu Ihre eigenen Wartungen.",
            [
                new("Woher die Termine kommen",
                    "Vertragsenden, Eichfristen der Zähler, Zinsbindungen von Darlehen, ablaufende Dokumente und die Abrechnungsfrist.",
                    "Eigene Wartungen, etwa Heizungswartung oder Rauchmelderprüfung, tragen Sie im Reiter Wartung ein."),
                new("Erinnerungen",
                    "Per Mail erinnert Elocaro vor der Fälligkeit und schickt montags eine Wochenübersicht. Das stellen Sie unter Einstellungen → Fristen ein.",
                    "Den Kalender können Sie als Abo in Ihren eigenen Kalender übernehmen."),
            ],
            [new("Einstellungen Fristen", $"{Admin}/einstellungen/fristen")]), $"{Admin}/fristen", $"{Admin}/fristen/wartung", $"{Admin}/fristen/wartung/{{id}}");

        Add(new("Einstellungen Fristen",
            "Hier stellen Sie ein, wann und wie Elocaro an Fristen erinnert.",
            [new("Gut zu wissen",
                "Der Vorlauf gilt für alle Fristen ohne eigenen Vorlauf. Mails gehen an alle Administratoren.",
                "Der Kalender-Link ist geheim. Wer ihn kennt, sieht Ihre Fristen. Ein neuer Link macht den alten ungültig.")]), $"{Admin}/einstellungen/fristen");

        // ---- Bank
        Add(new("Umsätze zuordnen",
            "Neue Kontoumsätze erscheinen hier mit einem Vorschlag, wohin sie gehören. Sie bestätigen, ändern, teilen auf oder ignorieren.",
            [
                new("So arbeiten Sie",
                    "Ein Vorschlag nennt seine Begründung, etwa die IBAN oder den Verwendungszweck.",
                    "Eine Sammelzahlung teilen Sie auf mehrere Forderungen auf.",
                    "Aus einem Umsatz können Sie direkt eine Regel erstellen, damit gleichartige Umsätze künftig automatisch zugeordnet werden."),
            ],
            [new("Regeln", $"{Admin}/bank/regeln")]), $"{Admin}/bank/zuordnung", $"{Admin}/bank/umsaetze/{{id}}/zuordnen");

        Add(new("Alle Umsätze",
            "Die vollständige Liste aller importierten Kontoumsätze mit Filter und Suche.",
            [new("Gut zu wissen", "Zugeordnete Umsätze sind mit dem Mieterkonto oder dem Beleg verknüpft. Eine Zuordnung können Sie lösen und neu setzen.")]), $"{Admin}/bank/umsaetze");

        Add(new("Zuordnungsregeln",
            "Eine Regel ordnet passende Umsätze künftig automatisch zu, zum Beispiel jede Zahlung der Stadt Köln der Grundsteuer.",
            [new("Gut zu wissen",
                "Alle Bedingungen einer Regel müssen zutreffen. Mehrere Begriffe trennen Sie mit Semikolon, dann genügt einer.",
                "Prüfen Sie neue Regeln am besten zuerst an ein paar Umsätzen, bevor Sie sie auf alle anwenden.")]), $"{Admin}/bank/regeln", $"{Admin}/bank/regeln/neu", $"{Admin}/bank/regeln/{{id}}");

        Add(new("Bankkonten",
            "Konten für Mieteingänge, Darlehen und Rücklagen. Die Umsätze kommen per Datei-Import oder FinTS.",
            [
                new("Gut zu wissen",
                    "Beim Datei-Import liest Elocaro die CSV-Datei Ihrer Bank. Dafür brauchen Sie ein passendes CSV-Profil.",
                    "FinTS holt die Umsätze direkt bei der Bank. Die Einrichtung steht in der Dokumentation (docs/banking-fints.md).",
                    "Doppelt importierte Umsätze erkennt Elocaro und markiert sie in der Vorschau, sie werden nicht ein zweites Mal übernommen."),
            ],
            [new("CSV-Profile", $"{Admin}/bank/csv-profile")]), $"{Admin}/bank/konten", $"{Admin}/bank/konten/neu", $"{Admin}/bank/konten/{{id}}", $"{Admin}/bank/importe/{{id}}");

        Add(new("CSV-Profile",
            "Ein CSV-Profil beschreibt, wie die Datei einer Bank aufgebaut ist: Trennzeichen, Spalten, Datums- und Zahlenformat.",
            [new("Gut zu wissen",
                "Banken exportieren unterschiedlich. Legen Sie pro Bank ein Profil an und verwenden Sie es bei jedem Import.",
                "Passt ein Import nicht, sehen Sie vor dem Übernehmen eine Vorschau mit den erkannten Spalten.")]), $"{Admin}/bank/csv-profile");

        // ---- Finance
        Add(new("Offene Posten",
            "Fällige, nicht vollständig bezahlte Forderungen, nach Mietverhältnis gruppiert.",
            [new("Gut zu wissen",
                "Ein Posten ist offen, sobald er fällig ist und nicht vollständig gedeckt wurde.",
                "Als CSV lässt sich die Liste exportieren.",
                "Aus Rückständen entstehen im Mahnwesen Vorschläge, die Sie freigeben.")]), $"{Admin}/finanzen/offene-posten");

        Add(new("Mahnwesen",
            "Elocaro erkennt Rückstände und schlägt Mahnungen vor. Es wird nichts versendet, bevor Sie freigeben.",
            [
                new("Die Stufen",
                    "Zahlungserinnerung, 1. Mahnung, 2. Mahnung. Die Wartezeiten stellen Sie unter Forderungen → Einstellungen ein.",
                    "Verzugszinsen betragen für Verbraucher fünf Prozentpunkte über dem Basiszinssatz (§ 288 BGB) und setzen den Verzug voraus.",
                    "Mahngebühren dürfen nur tatsächlich entstandene Kosten abdecken, etwa Porto."),
                new("Hinweis",
                    "Eine Kündigung wegen Zahlungsverzugs hat eigene Voraussetzungen. Elocaro weist nur darauf hin, es nimmt Ihnen die rechtliche Prüfung nicht ab."),
            ]), $"{Admin}/finanzen/mahnungen", $"{Admin}/finanzen/mahnungen/{{id}}");

        Add(new("Einstellungen Finanzen",
            "Buchhaltungsbeginn, Verrechnung von Zahlungen und Mahnwesen.",
            [
                new("Buchhaltung ab",
                    "Ab diesem Monat entstehen die Monatsmieten automatisch als Sollstellungen. Ohne Angabe bleibt das Mieterkonto leer.",
                    "Rückstände aus früheren Zeiten tragen Sie als Saldovortrag im Mieterkonto ein."),
                new("Verrechnung von Teilzahlungen",
                    "Hat der Mieter bestimmt, was er bezahlt, gilt das zuerst. Sonst gilt die gewählte Reihenfolge: nach gesetzlicher Regel, älteste zuerst oder Miete zuerst."),
            ]), $"{Admin}/finanzen/einstellungen");

        Add(new("Belege",
            "Rechnungen und Kostenbelege mit Datei. Aus ihnen entstehen Nebenkostenabrechnung und Steuerauswertung.",
            [
                new("So gehen Sie vor",
                    "Beleg mit Datei erfassen, dann den Betrag auf Kostenarten und Objekte aufteilen. Erst die Aufteilung lässt ihn in Abrechnung und Anlage V einfließen.",
                    "Belege ohne Aufteilung erscheinen in der Übersicht.",
                    "Haushaltsnahe Leistungen und Lohnkosten markieren Sie, damit Mieter sie für die Steuer nutzen können (§ 35a EStG)."),
                new("Automatisches Auslesen",
                    "Wenn eingerichtet, schlägt Elocaro Lieferant, Beträge und Datum vor. Sie prüfen und bestätigen, nichts wird ungeprüft gespeichert."),
            ]), $"{Admin}/belege", $"{Admin}/belege/neu", $"{Admin}/belege/{{id}}");

        // ---- System and settings
        Add(new("Benutzer",
            "Wer darf sich anmelden und was darf er? Hier laden Sie Personen ein und verwalten ihre Rechte.",
            [
                new("Rollen",
                    "Administratoren dürfen alles. Verwalter bekommen die Bereiche Stammdaten und Finanzen, bei Bedarf weitere Rechte.",
                    "Hausmeister, Handwerker, Mieter und Eigentümer sehen nur ihren eigenen Bereich."),
                new("Sicherheit",
                    "Administratoren und Verwalter müssen die Zwei-Faktor-Anmeldung einrichten.",
                    "Gesperrte Benutzer können sich nicht mehr anmelden. Ihre Daten bleiben erhalten."),
            ]), $"{Admin}/benutzer", $"{Admin}/benutzer/{{id}}");

        Add(new("Vermieter",
            "Absenderangaben für Briefkopf, Fußzeile und Zahlungshinweise in Dokumenten.",
            [new("Gut zu wissen",
                "Das Standardprofil gilt für alle Objekte. Für Objekte mit anderem Eigentümer oder anderer Gesellschaft legen Sie weitere Profile an und wählen sie am Objekt.",
                "IBAN und Steuernummer erscheinen in Schreiben und Abrechnungen. Prüfen Sie sie sorgfältig.")]), $"{Admin}/einstellungen/vermieter");

        Add(new("Daten importieren",
            "Bestand aus einer Tabelle übernehmen, statt alles von Hand einzugeben.",
            [new("So geht es",
                "Laden Sie die Vorlage, tragen Sie je vermietete oder leere Einheit eine Zeile ein und speichern Sie als „CSV (Trennzeichen-getrennt)“.",
                "Die Prüfung zeigt je Zeile, was angelegt wird und was nicht stimmt. Erst ohne Fehler lässt sich importieren; es wird alles oder nichts übernommen.",
                "Ein zweiter Lauf mit derselben Datei legt nichts doppelt an: Objekte, Einheiten, Personen (nach E-Mail oder Name) und Verträge werden wiedererkannt."),
             new("Gut zu wissen",
                "Die Wohnfläche gilt ab Jahresbeginn vor dem ersten Mietbeginn, damit frühere Abrechnungen sie finden.",
                "Ein Saldo wird als Saldovortrag (offen) oder als Guthaben im Mieterkonto gebucht.")],
            [new("Objekte", $"{Admin}/objekte"), new("Verträge", $"{Admin}/vertraege")]), $"{Admin}/einstellungen/import");

        Add(new("Rechtsstände",
            "Die Werte aus Gesetz und Statistik, auf die sich Abrechnungen, Mahnungen und Mieterhöhungen stützen, mit Stand und Quelle.",
            [new("Gut zu wissen",
                "Der Basiszinssatz ändert sich zum 1.1. und 1.7., der Verbraucherpreisindex erscheint monatlich: beide pflegen Sie selbst. Die Übersicht erinnert, wenn ein Wert fehlt oder veraltet ist.",
                "Stufentabelle (CO₂) und Gradtagszahlen sind mitgeliefert. Steht dort „prüfen“, vergleichen Sie sie vor dem Versand einer Heizkostenabrechnung mit der amtlichen Quelle.",
                "Elocaro ist keine Rechtsberatung: Die maßgebliche Quelle steht bei jedem Wert.")],
            [new("Forderungen: Einstellungen", $"{Admin}/finanzen/einstellungen"), new("Verbraucherpreisindex", $"{Admin}/vpi")]), $"{Admin}/einstellungen/rechtsstaende");

        Add(new("Module",
            "Schalten Sie ab, was Sie nicht brauchen. Die Daten bleiben erhalten.",
            [new("Gut zu wissen",
                "Ein abgeschaltetes Modul verschwindet aus Navigation, Suche und Übersicht, und seine Seiten sind nicht erreichbar.",
                "Unter „Auswahl innerhalb der Bereiche“ legen Sie fest, welche Mietmodelle, Abrechnungsarten und Angaben die Formulare anbieten.",
                "Den Einrichtungsassistenten können Sie jederzeit erneut starten.")]), $"{Admin}/einstellungen/module");

        Add(new("Vorlagen",
            "Vorlagen für Schreiben, Verträge und Mails. Platzhalter wie Namen und Beträge füllt Elocaro beim Erstellen.",
            [new("Gut zu wissen",
                "Änderungen erzeugen eine neue Fassung. Schon erstellte Dokumente behalten die Fassung, mit der sie erstellt wurden.",
                "Neue Vorlagen übernehmen Sie am besten aus einer vorhandenen und passen sie an.")]), $"{Admin}/einstellungen/vorlagen", $"{Admin}/einstellungen/vorlagen/neu", $"{Admin}/einstellungen/vorlagen/{{id}}");

        Add(new("E-Mail-Versand",
            "Ausgehende Nachrichten und ihr Zustellstatus.",
            [new("Gut zu wissen",
                "Mails werden zuerst in eine Warteschlange gestellt und kurz danach versendet. Fehlgeschlagene sehen Sie hier mit der Meldung des Servers.",
                "Mit einer Test-Mail prüfen Sie, ob der Versand eingerichtet ist.")]), $"{Admin}/system/mails");

        Add(new("Hintergrundjobs",
            "Geplante Aufgaben, die Elocaro regelmäßig selbst erledigt, etwa Mietbuchung, Mahnvorschläge und Erinnerungen.",
            [new("Gut zu wissen",
                "Jobs von abgeschalteten Modulen laufen leer.",
                "Das Laufprotokoll zeigt, wann ein Job zuletzt lief und wie es ausging.")]), $"{Admin}/system/jobs");

        Add(new("Datensicherung",
            "Nächtliches Backup, Wiederherstellung und Vollexport aller Daten.",
            [new("Gut zu wissen",
                "Eine Sicherung, die nie zurückgespielt wurde, ist nicht erprobt. Testen Sie die Wiederherstellung einmal auf einem anderen Rechner.",
                "Bewahren Sie Sicherungen außerhalb des Servers auf.",
                "Der Vollexport enthält alle Daten in lesbarer Form, etwa für einen Umzug oder die Archivierung.")]), $"{Admin}/system/datensicherung");

        Add(new("Belegauslesung",
            "Beim Erfassen von Belegen schlägt Elocaro Lieferant, Beträge, Datum und Kostenart automatisch vor.",
            [new("Gut zu wissen",
                "Standardmäßig ist die Auslesung aus. Dann verlässt kein Dokument den Server.",
                "Mit einem externen Dienst werden Belege dorthin gesendet. Das müssen Sie ausdrücklich erlauben und in Ihrer Datenschutzdokumentation berücksichtigen.",
                "Die Vorschläge sind unverbindlich. Unsichere Felder sind markiert.")]), $"{Admin}/system/belegauslesung");

        return pages;
    }
}
