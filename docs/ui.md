# UI-Leitfaden

Verbindlich für alle neuen und geänderten Seiten. Grundlage: ADR 0003 (eigenes CSS statt Framework) und ADR 0004 (Material 3 Expressive). Technische Details zu Blazor-Formularen stehen in [entwicklung.md](entwicklung.md#oberfläche).

## Prinzipien

1. **Zwei Arten von Seiten.** Statische Seiten (Standard, ohne eigenes JavaScript: Links, GET-/POST-Formulare, `<details>`) und interaktive Seiten (`@rendermode InteractiveServer`, MudBlazor, siehe [Interaktive Seiten](#interaktive-seiten)). Seiten mit Eingaben, Live-Suche, Dialogen oder Assistenten sind interaktiv (alle Bereiche der Verwaltung sind umgestellt); reine Anzeigeseiten bleiben statisch: Start, Hintergrundjobs, Notfallmappe (einfaches Formular; das Passwort läuft nie über die Verbindung einer interaktiven Seite), Belegauslesung, Offene Posten sowie Anmeldung, Konto und Portale.
2. **Mobil zuerst.** Jede Seite funktioniert bei 360 px ohne horizontales Scrollen; Touch-Ziele ≥ 44 px (Buttons, Tabs, Aufklapper, Info; ein Browser-Test prüft das). Buttons sind Pills, haben im Ruhezustand keinen Schatten (Schatten nur beim Überfahren und bei der schwebenden Hauptaktion) und werden beim Drücken eckiger (Formwechsel, Material 3 Expressive).
3. **Bausteine statt Rohmarkup.** Gibt es für etwas einen Baustein (siehe unten), wird er benutzt. Fehlt einer, wird er im Design-System ergänzt und hier dokumentiert, nicht in der Seite gebaut.
4. **Farben nur über Rollen.** Keine Hex-Werte in Seiten oder Komponenten. Hell und Dunkel müssen beide lesbar sein.
5. **Barrierearm.** Kontrast nach AA, sichtbarer Fokus, `role="alert"` bzw. `"status"` bei Meldungen, Beschriftung für jedes Feld.

## App-Gerüst

Die Verwaltung verhält sich wie eine Anwendung, nicht wie eine lange Webseite. Das Dokument scrollt nie; es scrollt nur `main.content` (und Tabellen/Dialoge in sich).

| Bereich | Desktop (ab 900 px) | Smartphone |
|---|---|---|
| Kopfleiste (`.top-bar`) | feststehend: Marke, Benutzermenü | entfällt, der Seitenkopf ist die App-Leiste |
| Hauptnavigation | Seitenleiste (`.sidebar`), eigener Scroll, nach Bereichen gruppiert | Untere Leiste (`.bottom-bar`): Übersicht, Bestand, Geld, Kommunikation, **Menü** (Blatt mit allen Bereichen und Benutzermenü) |
| Tabs (`TabBar`, `ContextTabs`) | unter der Kopfleiste, feststehend | gleiche Leiste, seitlich wischbar, aktiver Tab wird sichtbar gehalten; wo weitere Tabs warten, blendet die Leiste am Rand aus |
| Seitenkopf (`PageHeader`) | `position: sticky` im Inhaltsbereich | Zurück als Icon links vom Titel; die gefüllte Hauptaktion wird zur schwebenden Schaltfläche über der unteren Leiste (beim Hinunterscrollen weicht sie aus, beim Hinaufscrollen kommt sie zurück, `js/shell.js`) |
| Tabellen | `MudTable` füllt die freie Höhe, Kopfzeile bleibt stehen | Karten (`Breakpoint.Sm`) |
| Dialoge | zentriert | als Blatt vom unteren Rand |

**Navigationsmodell** (`Layout/AdminNavModel.cs`): einzige Stelle, die weiß, was zusammengehört.
- *Bereiche* (Seitenleiste, unteres Menü) mit Berechtigung; jeder Bereich kennt seine Seiten (`Prefixes`) und Geschwisterseiten (`Tabs`). `ContextTabs` zeigt die Tabs des Bereichs automatisch, eine neue Seite braucht also nur einen Eintrag dort, keinen eigenen Navigationscode.
- **Ortszeile** (`Layout/ContextBar.razor`, über den Tabs): Auf jeder Seite, die zu einem Objekt, einer Einheit, einem Zimmer, Zähler, einer Abrechnung, einem Vertrag oder einer Person gehört, zeigt sie die Spur mit den **Namen**, z. B. `Bestand › Objekte › Lindenweg 3 › Wohnung 4 › Zimmer 1` oder `Bestand › Verträge › Lindenweg 3 › Wohnung 1 · Schmidt`. Jeder Schritt außer der Seite selbst ist ein Link; auf dem Handy bleiben nur die Elternebene und die Seite. Die Namen löst `INavigationContext` (`Infrastructure/Admin/NavigationContextResolver`) aus dem Pfad auf, weil ein Layout in Static SSR nicht sieht, was die Seite später setzt. Sie ersetzt den Zurück-Link, der mit Tabs auf breiten Bildschirmen ausgeblendet ist. Darlehen (`/finanzen/darlehen/{id}`) nennen ihr Objekt in der Spur, behalten aber die Tabs von Geld. Neue Entitäts-Seiten brauchen dafür einen Eintrag im Resolver.
- **Objekt-Kopf** (`Layout/RecordHeader.razor`, zwischen Ortszeile und Tabs): Auf allen Seiten mit Objekt-Tabs zeigt er Objektname, Anschrift und Kennzahlen (Einheiten, Baujahr, Heizung) und bleibt auf jedem Tab gleich. Er ist kein `h1`; die Seite behält ihren eigenen `PageHeader` mit dem Seitentitel („Übersicht“, „Belegung“ …). Seiten wiederholen den Objektnamen deshalb nicht als Untertitel. Die Daten liefert `PageContext.Property`. Auf dem Handy entfallen die Kennzahlen.
- **Ausrichtung:** Ortszeile, Objekt-Kopf, Tabs, Seitenkopf und Inhalt teilen den Rand `--content-gutter`; der Inhalt wird nicht zentriert, Tabellen und Listen nutzen die volle Breite. Nur Formulare (`.form-grid`) haben eine Lesebreite von 72rem, linksbündig.
- **Objekt-Tabs auch außerhalb von `/objekte/{id}`:** Einheit, Zimmer, Zähler und Abrechnung (`/einheiten`, `/zimmer`, `/zaehler`, `/abrechnungen`) zeigen die Tabs ihres Objekts (`AdminNavModel.TabsFor(path, propertyId)`, `BelongsToProperty`); die Ebene springt beim Weg Objekt → Einheit also nicht mehr.
- Die Seitenleiste hat **sechs Bereiche** (höchstens sieben, `NavigationModelTests`): *Übersicht* · *Bestand* (Objekte, Personen, Verträge) · *Kommunikation* (Tickets, Mitteilungen, Dokumente, Fristen) · *Geld* (Bank, Forderungen, Belege, Darlehen & Steuer) · *Benutzer* · *Einstellungen*. Die Seitenadressen blieben unverändert; ein Bereich fasst nur die bisherigen Seiten unter Tabs zusammen. Gruppen lassen sich nicht schachteln: höchstens zwei Tab-Zeilen (Bereichs-Tabs, darunter die Seiten der aktiven Gruppe).
- *Arbeitsbereiche* einzelner Objekte (`/verwaltung/objekte/{id}/…`) und Verträge (`/verwaltung/vertraege/{id}/…`): Tabs wie Zähler, Heizkosten, Mieterkonto, Übergabe. Solche Verweise gehören **nicht** als Schaltflächen in den Seitenkopf. Im `Actions`-Slot stehen nur Aktionen an der Seite selbst (Bearbeiten, Neu); seltene und riskante Aktionen in ein `MudMenu` („Weitere Aktionen“).
- Zusammengehörige Seiten stehen als **Gruppe** unter einem Tab (`NavTab.Group`): Objekt = Übersicht · Zähler · *Nebenkosten* (Versorgungsverträge, Umlageschlüssel, Heizkosten, Vorschau, Abrechnungen) · *Steuer* (AfA, Erhaltungsaufwand, Anlage V); Vertrag = Übersicht · *Konto* (Mieterkonto, Kostenarten) · *Vertrag* (Vertragsdokument, Mieterhöhung) · *Ein- & Auszug*. Solange die Gruppe aktiv ist, zeigt `TabBar` eine zweite Zeile mit ihren Seiten.
- Innerhalb einer Gruppe trennt `NavTab.SeparatorBefore` zusammengehörige Teile mit einem Strich (Nebenkosten in Arbeitsreihenfolge: Einrichtung — Versorgungsverträge, Umlageschlüssel, Heizkosten | Durchführung — Vorab prüfen, Abrechnungen; die Gruppe öffnet auf den Abrechnungen, der Abrechnungs-Assistent startet dort mit „Neue Abrechnung“). Eine Gruppe mit nur einer sichtbaren Seite (Modul aus) wird zum einfachen Tab ohne zweite Zeile.
- Neue Unterseite eines Objekts oder Vertrags: Tab in `PropertyTabs`/`LeaseTabs` ergänzen, eine neue Gruppe nur für wirklich eigenständige Themen.
- **Breite Bildschirme:** Die Seitenleiste listet beim geöffneten Bereich seine Seiten direkt auf (zweite Ebene, Gruppen durch eine feine Trennlinie abgesetzt, ohne Überschrift, die nur den ersten Eintrag wiederholen würde, `AdminNavigation`). Die Tab-Leisten des Bereichs (`area-tabs`) sind dort ausgeblendet; Objekt und Mietvertrag behalten ihre Tab-Leiste. Auf dem Handy bleiben Tab-Leisten und das einfache Menü-Blatt. Weil dort keine Tab-Leiste den Seitennamen nennt, zeigt `PageHeader` die Überschrift auf breiten Bildschirmen wieder an (`is-compact-narrow`, `visually-hidden-narrow`); auf dem Handy bleibt sie zugunsten der Tabs ausgeblendet.
- Auf breiten Bildschirmen blendet der Seitenkopf den Zurück-Link aus, sobald eine Tab-Leiste da ist (der Tab führt dorthin); auf dem Handy bleibt er als Pfeil.

**Kennzahlen im Objekt-Kopf** erscheinen nur, wenn sie Werte haben (kein „Baujahr –“). Listen mit Aktion je Zeile (Umlageschlüssel: „Festlegen“/„Ändern“) zeigen die Aktion in der Zeile und werden auf dem Handy zu Karten.

**Konto-Seiten** (`Components/Account/Security`, Layout `AccountAreaLayout`): Statische Seiten in einer Karte, deren Logo zur App führt. Die Übersicht zeigt Profil (Initialen, Name, E-Mail, Rolle) und Einträge als Navigationszeilen (`.account-nav`: Titel, Status in einer Zeile, Pfeil) in den Gruppen „Sicherheit“ sowie „Geräte und Integrationen“; Abmelden steht darunter. Unterseiten haben oben den Zurück-Link `AccountBack` (kein „Zurück“ am Ende). Ein Formular, das nur bei Bedarf erscheint (Zugriffstoken erstellen), ist ein natives `<dialog class="info-dialog">`, geöffnet über `data-info-dialog`; kommt es mit Fehlern zurück, öffnet `data-open-on-load` es wieder. Ein einmal angezeigtes Geheimnis bekommt eine Schaltfläche mit `data-copy="#selector"` (Kopieren in die Zwischenablage, `shell.js`). Listen von Einträgen mit mehreren Angaben sind Karten (`.token-card`), keine Tabelle.

**Lange Seiten gliedern.** Eine Seite hat höchstens zwei, drei Bildschirmhöhen. Wird sie länger, weil mehrere fachliche Themen untereinander stehen (Mieterkonto: Buchungen, Mahnwesen, Auswertung; Darlehen; DATEV-Export), kommen die Themen in `MudTabs` (Wichtigstes zuerst, Einrichtung zuletzt oder bei der Erstnutzung zuerst). Lange Tabellen werden gepaged (`MudTable` mit `MudTablePager`) und bekommen Filter (Jahr, „nur offene“). Auf dem Dashboard zeigt jeder Bereich die vier dringendsten Einträge, der Rest klappt auf.

**Detailseiten** (Objekt, Vertrag, Person): Kopf mit Kerndaten als `.summary-card`, darunter `MudTabs` (`Class="detail-tabs"`) statt einer langen Abschnittsliste. Je Tab ein fachlicher Abschnitt (z. B. Miete, Parteien & Haushalt, Kündigung, Angaben, Dokumente, Verlauf). Ohne Daten bleibt ein Tab sichtbar mit `EmptyState`; ausgeblendet wird nur, was fachlich nicht möglich ist (`Visible`).

**Anlegen, Bearbeiten, Einladen:** Es gibt keine eigenen „Bearbeiten“-Seiten. Die Stammdaten von **Objekt, Person, Einheit, Zimmer und Bankkonto** bearbeitet die Schaltfläche „Bearbeiten“ im Seitenkopf in einem Dialog (`FormDialog`, kein Wechsel in einen anderen Tab; das Foto des Objekts liegt im Tab *Foto*). Ebenso Einheit, Zimmer und Bankkonto; „Einheit löschen“ und „Zimmer löschen“ stehen im Menü „Weitere Aktionen“. Eine Ausnahme ist der **Beleg**: Sein Tab *Angaben* bleibt, weil man dort neben dem Dokument liest und Auslesevorschläge prüft. **Jeder Eintrag in einer Liste (Staffel, Mietbestandteil, Buchung, Eigentümer, Bankverbindung, Sondertilgung …) lässt sich bearbeiten, nicht nur löschen** (Schaltfläche „Bearbeiten“ in der Zeile; Dialog oder das Formular der Seite im Bearbeiten-Modus). Zeitreihen mit Stichtag behalten zusätzlich ihr „ab Stichtag ändern“; die Korrektur ändert den Eintrag selbst. Kleine Formulare (Person einladen, Umsatz ignorieren, Zimmer, IBAN …) sind Dialoge, nicht Seiten oder Formulare über der Liste.

**Vorlagen-Editor** (`Templates/TemplateEditor`): links Felder und `CodeEditor`, rechts Tabs *Vorschau* und *Platzhalter*. Die Vorschau rendert die Vorlage mit Beispieldaten als HTML (`IDocumentTemplates.PreviewHtmlAsync`, ohne PDF-Dienst, daher beim Schreiben live) in einem abgesicherten Rahmen als Briefbogen; Fehler der Vorlage stehen sofort darüber. Die PDF-Vorschau bleibt für Seitenumbrüche. Platzhalter, Hilfsfunktionen und Bausteine fügt ein Klick an der Cursorposition ein (`data-insert`).

**Teilansicht** (`.split`, Beleg): Auf breiten Bildschirmen steht das Dokument rechts neben Formular und Tabs (Vorschau bleibt beim Scrollen stehen), darunter wird gestapelt.

**Abrechnungs-Assistent, Schritt 1:** Unter „Vorbereitung“ stehen Versorgungsverträge, Umlageschlüssel und Heizkosten mit Status (`#setup-checks`) und Link zur jeweiligen Einstellungsseite (mit `?zurueck=` zurück in den Assistenten). Nicht festgelegte Schlüssel und nicht bestätigte Heizangaben zeigen „Standard“ als Warnung, damit die stillen Standardannahmen sichtbar sind.

**Ungespeicherte Änderungen:** Lange Formulare auf einer Seite (Muster `Leases/HandoverPage`) verfolgen Änderungen über einen `EditContext` (`dirty`) und setzen ein `NavigationLock`: Beim Wegnavigieren fragt `Confirmations` nach, beim Schließen des Tabs fragt der Browser. Nach dem Speichern oder Neuladen ist der Zustand wieder sauber.

**Gruppen in der Seitenleiste:** Eine Gruppe, deren Name nicht schon ihre erste Seite ist (Geld: *Bank*, *Forderungen*, *Darlehen & Steuer*), bekommt eine kleine Überschrift; andere Gruppen und einzelne Seiten nach einer Gruppe werden nur durch eine Linie getrennt.

**Sammelaktionen und „Rückgängig“:** Wo viele gleichartige Zeilen dasselbe brauchen (Umsätze ignorieren), hat die Liste je offener Zeile ein Auswahlfeld mit sprechendem Namen, „Alle offenen … auswählen“ im Kopf und eine Leiste „n ausgewählt“ mit der Sammelaktion. Nicht für Aktionen, die etwas versenden oder rechtlich prüfen lassen (Mahnungen werden einzeln freigegeben). Eine Änderung, die sich verlustfrei zurücknehmen lässt, meldet sich mit `Notifier.Undoable` und der Schaltfläche „Rückgängig“ (8 Sekunden); Löschen bleibt bei der Rückfrage.

**Anlegen-Seiten haben „Abbrechen“:** Auf breiten Bildschirmen blendet das CSS den Zurück-Pfeil im Seitenkopf aus, wo Tabs oder Seitenleiste vorhanden sind. Jede Seite zum Anlegen (Person, Objekt, Beleg, Dokument, Ticket, Aushang, Nachricht) zeigt deshalb neben der Hauptschaltfläche einen Textlink „Abbrechen“ zur Liste. Exportschaltflächen stehen nur, wenn die Liste Daten hat.

**Häufige Aktionen nicht hinter Tabs:** Die Hauptaktion einer Seite steht im Seitenkopf, nicht in einem eigenen Reiter. Im Mieterkonto ist „Zahlung erfassen“ die Hauptaktion (Dialog vorbelegt mit heute und dem fälligen Betrag), „Forderung oder Gutschrift buchen“ liegt im Menü „Weitere Aktionen“. `…/konto?zahlung=erfassen` öffnet den Dialog beim Laden; die Offenen Posten verlinken je Mieter darauf. Ein Menüeintrag, der einen Dialog öffnet, startet ihn losgelöst (`_ = InvokeAsync(…)` mit `StateHasChanged` danach), damit das Menü nicht hinter dem Dialog offen bleibt.

**Mieterkonto:** Über den Tabs steht nur die Saldo-Karte; Handlungsbedarf (Mahnvorschlag, Kündigungshinweis) steht als Hinweis oberhalb, nicht im Reiter, der dann „Mahnwesen (Vorschlag)“ heißt (kein Badge: die Warnfarbe besteht den Kontrast im Dunkeln nicht). Das Diagramm liegt in „Auswertung“. Wartungsaktionen („Fehlende Monatsmieten nachtragen“) und „Löschen“ je Zeile stehen in Menüs. **Buchungen sind nach Monat gruppiert:** Mietbestandteile mit gleichem Fälligkeitstag (Kaltmiete, Betriebs-, Heizkostenvorauszahlung) erscheinen als eine Zeile „Monatsmiete MM/JJJJ“ mit Summe, Status und Saldo; „Details“ klappt die Bestandteile gedämpft und eingerückt auf. Zahlungen und Sonderposten bleiben einzelne Zeilen. Die Tabelle nutzt `Breakpoint.None` und eigenes CSS (`.account-rows`), weil MudBlazors Kartenform ab 960 px von der App-Grenze abweicht; der Name der „Details“-Schaltfläche beginnt mit dem sichtbaren Text.

**Mietvertrag anlegen** ist ein Assistent mit vier Schritten (`Leases/Create`); „Weiter“ prüft nur die Pflichtfelder bis zum aktuellen Schritt (`IValidatableObject` am Formular), `?schritt=` setzt den Startschritt.

**Assistenten** (`Components/Common/Wizard.razor`, Beispiel `Settings/SetupWizard`): Rahmen für mehrstufige Abläufe mit Schrittliste (links auf breiten Bildschirmen, Abschnitte mit aufklappenden Unterschritten, erledigte mit Haken), Fortschrittsbalken mit „Schritt x von y“, Karte des aktuellen Schritts und Navigation (Zurück, Weiter bzw. Abschluss, optional „Später“). Die Seite liefert `Steps` (`WizardStepInfo`: Titel, optional Abschnitt und Beschreibung), bindet `@bind-Current` und schreibt im `ChildContent` per `if / else if` den Inhalt des aktuellen Schritts, ohne eigene Überschrift (die setzt der Rahmen). `OnFinish` speichert, `SkipText` und `OnSkip` bieten „Später“ an, `Model` (optional) wird vor „Weiter“ per DataAnnotations geprüft. Der Rahmen enthält das `EditForm`, Eingabekomponenten brauchen keines. Schritte, die noch nicht erreicht wurden, sind in der Liste nicht anklickbar (`FreeNavigation="true"` erlaubt jeden Sprung). Nach jedem Schrittwechsel erhält die Überschrift den Fokus. Auf dem Handy entfällt die Seitenliste; Fortschritt und Abschnittsname bleiben, die Schritte stehen in einer aufklappbaren Liste („Schritte“). `WizardStepInfo.Badge` zeigt eine Markierung an einem Schritt (z. B. offene Hinweise), `FinishDisabled` sperrt den Abschluss. Alle Assistenten (Einrichtung, Abrechnung, Mietvertrag) nutzen dieses Control. **Fokusmodus:** Der Einrichtungsassistent (`/verwaltung/einrichtung`) blendet Seitenleiste und untere Leiste aus (`.app-admin.is-focus`, gesetzt in `AdminLayout`), damit nichts mitten in der Einrichtung wegführt. Schritte, die entfallen können (Absenderdaten, erstes Objekt, Buchhaltung), stehen nur in der Liste, solange sie nötig sind; die Seite schaltet über einen `StepKey` statt über Indizes.

**Seiteninfos** (`Components/Common/PageInfo.cs`, `InfoDialog.razor`): Neben den Aktionen im Seitenkopf steht ein „Info“-Button, der einen Dialog mit Hintergrundwissen zum Inhalt der Seite öffnet (Aufbau, Zusammenhänge, gesetzliche Eckpunkte, Tipps). Die Texte stehen zentral in `PageInfoCatalog`, gefunden werden sie über den Pfad der Seite mit `{id}` statt Guid (`/verwaltung/objekte/{id}/zaehler`); `PageHeader` zeigt den Button automatisch, `ShowInfo="false"` unterdrückt ihn. Der Dialog ist ein natives `<dialog>` (öffnet `shell.js`), funktioniert also auf statischen und interaktiven Seiten, schließt mit Esc, Button und Klick daneben. Neue Seiten in der Navigation brauchen einen Eintrag, ein Test prüft das. Texte knapp halten (2 bis 4 Punkte je Abschnitt), nur belegbare Aussagen, Gesetzesverweise nur wo sicher; der Dialog nennt selbst „keine Rechts- oder Steuerberatung“.

**Neuigkeiten** (`Admin/Operations/News`): Der Weg dorthin ist das Benutzermenü (Eintrag *Neuigkeiten*, für Benutzer mit dem Recht System; ein Punkt `.nav-dot` zeigt eine neue Version), zusätzlich der Tab unter Einstellungen → System. Die Seite hat oben eine Karte (`.news-hero`: Version, Status, Befehl mit „Kopieren“, Button „Jetzt prüfen“ `#news-check` als Formular-POST an `/verwaltung/system/neuigkeiten/pruefen` mit Rücksprung auf die Seite), darunter die Versionen als Karten (`.release`) mit dem Release-Text als Markdown (`.release-body`).

**Schnellsuche** (`Layout/QuickSearch.razor`, Strg+K bzw. Feld in der Kopfleiste, auf dem Handy im Menü-Blatt): springt zu Bereichen und Unterseiten (nach Berechtigung) sowie zu Objekten, Personen und Verträgen. Interaktive Insel im statischen Layout; Pfeiltasten und Eingabe wählen, Esc schließt. Neue Bereiche tauchen automatisch auf, sobald sie in `AdminNavModel` stehen.

**Arbeitslisten** (`.work-list`/`.work-card`, Beispiel Umsätze zuordnen): Wo je Eintrag eine Entscheidung fällt (Vorschlag bestätigen, ändern, ignorieren), steht eine Karte je Eintrag statt einer Tabelle: Betrag und Beschreibung, Vorschlag mit Begründung, Aktionen; auf dem Handy gestapelt.

**Rechentabellen mit Summenzeilen** (Kosten, Mieterabrechnung in der Vorschau) tragen `card-rows` und `data-label` an den Zellen: Auf dem Handy wird jede Zeile zur Karte, Summen stehen als Beschriftung und Betrag nebeneinander, damit kein Betrag aus dem sichtbaren Bereich rutscht. Mieterblöcke der Vorschau sind `details.group` (bis drei offen, sonst zugeklappt), die Überschrift nennt den Saldo.

**Zwei Tabellenarten, ein Aussehen:** `.data-table` (statisch) und `MudTable` (interaktiv) teilen Schrift, Zellabstand, Kopfzeile und Linien (`app.css`, Abschnitt „MudBlazor tables“); neue Listen dürfen beide nutzen.

**Tabellen auf dem Handy:** Statische `.data-table` ohne verbundene Zellen werden unter 700 px zu Karten (`shell.js` setzt `data-label` aus den Spaltenköpfen). Ausnahme mit `data-nostack` am `table`.

**Startseite** („Übersicht“, Untertitel „Was ist zu tun?“; dieselbe Bezeichnung in Seitenleiste und unterer Leiste): ab 1200 px links die Aufgaben, rechts Kennzahlen (`KpiTile`) und Diagramme; auf dem Handy Kennzahlen, Aufgaben, Diagramme. „So geht es los“ steht als eigener Block mit Fortschritt über den Aufgaben und verschwindet mit dem letzten Schritt. Die Aufgaben sind **eine** Liste, nach Dringlichkeit und Fälligkeit sortiert, der Bereich steht klein am Eintrag und filtert über `filter-chip` (`?bereich=`). Die Diagramme entfallen, solange der Verlauf nur aus Nullen besteht. Leere Einheiten (Leerstand) bieten in der Belegung direkt „Vertrag anlegen“ (vorbelegt mit dem Mietobjekt). In „Umsätze zuordnen“ ist „Bestätigen“ nur ab 70 % Treffsicherheit gefüllt, die Liste zeigt 50 Einträge und lädt auf Wunsch weitere. Rückfragen beim Löschen nennen den Namen (und bei Objekten die Zahl der Einheiten); das Löschen eines Zählers liegt im Menü „Weitere Aktionen“ statt in einem eigenen Tab.

`wwwroot/js/shell.js` ergänzt nur Kleinigkeiten (Menüs schließen, Inhalt nach Navigation nach oben scrollen, aktiven Tab zeigen); alles andere funktioniert ohne JavaScript. `manifest.webmanifest` macht die Anwendung auf dem Handy installierbar („Zum Startbildschirm“, Vollbild ohne Browserleiste).

Regeln für neue Seiten: keine eigenen `height`-/`overflow`-Konstrukte für das Seitenscrollen; Tabellen entweder `MudTable` als direktes Kind des Inhalts (füllt die Höhe) oder `.table-wrapper`; Hauptaktion einer Seite genau einmal gefüllt im `PageHeader`.

## Design-Tokens (`wwwroot/app.css`)

| Gruppe | Tokens | Verwendung |
|---|---|---|
| Farbe (Markenfarbe) | `--md-primary`, `--md-on-primary`, `--md-primary-container`, `--md-on-primary-container` | Hauptaktion, aktive Navigation |
| Farbe (ergänzend) | `--md-secondary*`, `--md-tertiary*` | Tonal-Buttons, Akzente |
| Farbe (Status) | `--md-info*`, `--md-success*`, `--md-warning*`, `--md-error*` | Meldungen, Status-Chips |
| Flächen | `--md-surface`, `--md-surface-container-lowest … -highest`, `--md-on-surface`, `--md-on-surface-variant`, `--md-outline`, `--md-outline-variant` | Hintergründe, Text, Linien |
| Form | `--shape-xs … -xl`, `--shape-full` | Rundungen: Eingabefelder `--shape-field` (= `md`, 12 px; Textfelder, Auswahl, Zahl, Uhrzeit, Datei), Anzeigebalken `xs`, Chips und Sub-Tabs `sm`, Karten/Tabellen `lg`, Dialoge `xl`, Buttons und Navigation `full` (Pill) |
| Abstand | `--space-xs`, `-sm`, `-md`, `-lg` (0,25 bis 1 rem) | Für neue und angefasste Regeln; `--spacing` bleibt für vorhandenen Code |
| Schrift | `--type-display`, `-headline`, `-headline-small`, `-title`, `-body`, `-label` | Überschriften, Fließtext, Beschriftungen |
| Bewegung | `--motion-spatial`, `--motion-effects`, `--motion-duration` | Übergänge; bei „reduzierte Bewegung“ wird die Dauer 0 |

Die älteren Namen `--color-*` und `--radius` sind Aliase und nur für vorhandenen Code gedacht.

Hell/Dunkel: Jede Farbrolle ist mit `light-dark(hell, dunkel)` definiert. Es gilt die Systemeinstellung; der Benutzer kann sie im Benutzermenü auf *Hell* oder *Dunkel* festlegen. Das speichert das Cookie `elocaro.theme` (`ThemePreference`) und setzt `data-theme` am `<html>`-Element.

## Bewegung

Zurückhaltend und nur dort, wo sie etwas erklärt; bei „reduzierte Bewegung“ entfällt alles (`--motion-duration: 0ms`, View-Transition aus).

- **Seitenwechsel:** Der Inhalt blendet in 160 ms über (`@view-transition`), Kopfleiste, Seitenleiste und untere Leiste stehen still (`view-transition-name`).
- **Langsamer Wechsel:** Dauert ein Wechsel länger als 250 ms, läuft ein dünner Fortschrittsbalken am oberen Rand (`html.is-navigating`, gesetzt von `shell.js` bei `enhancednavigationstart`/`-end`).
- **Ladetext** (`.loading`): erscheint erst nach 300 ms, mit kleinem Kreisel; schnelle Ladevorgänge zeigen nichts.
- **Aufklappen** (`details`): Inhalt gleitet sanft ein. Dialoge, Menüs, Tabs und Snackbars bringen ihre Bewegung von MudBlazor mit.
- **Palette von Anfang an:** MudBlazor lädt beim Vorrendern seine helle Palette (solange nicht „Dunkel“ gewählt ist), Tabellen und Buttons blitzten deshalb hell auf. `app.css` setzt die `--mud-palette-*` per `:root:root` aus den App-Tokens (gespiegelt zu `M3Theme`); ändert sich `M3Theme`, muss der Block dort mit.
- **Tab-Leisten auf dem Handy:** Die `MudTabs` der Detailseiten (`detail-tabs`) scrollen unter 900 px nativ (Wischen); MudBlazors Pfeiltasten und seine Verschiebung sind dort ausgeblendet, `shell.js` hält den aktiven Tab sichtbar (`revealMudTab`).
- **Doppelte Titel:** Wiederholt der `PageHeader`-Titel den gewählten Tab oder Unter-Tab (`AdminNavModel`), ist die `h1` nur für Screenreader da (`visually-hidden`); Untertitel und Aktionen bleiben. Dringlichkeit in Listen (`.todo-urgent/-soon/-info`) zeigt ein Punkt, kein Randstreifen. Filter an Listen sind `filter-chip` (`aria-pressed`), keine Schalter.
- **Verbindungsabbruch:** `Components/App.razor` enthält den Dialog `#components-reconnect-modal` (deutsch, App-Stil), `js/reconnect.js` öffnet und schließt ihn anhand der Blazor-Ereignisse und bietet „Erneut versuchen“ bzw. „Seite neu laden“.
- **Kein Springen:** Die Schrift (Roboto Flex) wird vorgeladen; ein Browser-Test (`Pages_do_not_shift_while_loading`) hält den Layout-Sprung beim Laden unter 0,1. Neue Elemente, die erst nach dem Laden erscheinen, reservieren ihren Platz.
- Keine Eingangsanimationen für Karten oder Listen: Die Anwendung soll sich wie ein Werkzeug anfühlen, nicht wie eine Präsentation.

## Schrift, Icons, Felder

- **Schrift:** Roboto Flex (Variable Font, OFL), lokal in `wwwroot/fonts/` mit Lizenztext; kein CDN.
  Gewichte 400 bis 600 (nie 700); Hierarchie über Größe und Farbe. Beschriftungen in Satzschreibweise, kein `text-transform: uppercase`. Schatten über `--shadow`, `--shadow-menu`, `--shadow-fab`.
  Abstände (`padding`, `margin`, `gap`, Höhen) liegen auf dem 4-px-Raster (Vielfache von 0,25 rem; 2 px nur für Feinabstimmung in kleinen Bedienelementen); Icon-Größen sind davon ausgenommen. `UiRulesTests` prüft Gewichte und Großbuchstaben in `app.css`.
- **Icons:** Inline-SVG über `Icon`, Pfade in `AppIcons` (aktuell die Material-Icons aus MudBlazor).
- **Felder:** Outlined, Beschriftung im Rahmen (`FormField` und `div.field`). Der Hintergrund der Beschriftung kommt aus `--field-surface`; Container mit anderer Fläche setzen sie (siehe `.account-card`).

## Bausteine

| Bedarf | Baustein | Regel |
|---|---|---|
| Seitenkopf | `PageHeader` | Jede Seite außer Anmeldung und Fehlerseiten; genau ein `<h1>`; Aktionen im `Actions`-Slot (höchstens eine Hauptaktion, wenige Nebenaktionen, Rest im Menü) |
| Formularfeld | `FormField` + `Input*` | Beschriftung immer sichtbar; Pflicht und Hinweis über Parameter |
| Geld, Fläche | `InputAmount`, `InputArea` | Nie `type="text"` für Zahlen |
| Jahr | `AppYearSelect` (Auswahl der letzten 15 Jahre bis nächstes Jahr, `int` oder `int?` mit `EmptyText`) | Nie ein freies Zahlenfeld für ein Jahr |
| Liste von Einträgen mit mehreren Werten (Brennwert je Jahr) | `MudTable` mit „Bearbeiten“/„Entfernen“ je Zeile, „… hinzufügen“ öffnet einen Dialog | Keine Reihe loser Eingabefelder |
| Datum, Monat | `AppDateField` (TT.MM.JJJJ), `AppMonthField` (MM.JJJJ) | Platzhalter mit Format; Eingabe mit und ohne Punkte (`01112026`, `1.2.27`, ISO) wird gelesen (`LenientDateConverter`) und im Normalformat angezeigt; Unlesbares meldet einen Fehler unter dem Feld statt still zu verschwinden. „(Monat)“-Felder nehmen `AppMonthField`, nie den Tageskalender |
| Tabelle | `.table-wrapper > .data-table` | Immer im Wrapper; Zahlen in `td.number`; Sortieren mit `SortHeader`, Blättern mit `Pagination` |
| Hinweis | `Notice` (Level Info/Success/Warning/Error), `StatusMessage` (nach Weiterleitung), `ErrorList` (Fehler aus Diensten) | Kein eigenes `class="message …"`; `role` setzt `Notice` selbst |
| Leerer Zustand | `EmptyState` | Unterscheiden: „Keine Treffer für …“ (Filter aktiv) und „Noch keine …“. „Noch keine …“ bietet den nächsten Schritt als gefüllte Schaltfläche im Inhalt; solange die Liste leer ist, entfällt dieselbe Aktion im Seitenkopf, damit sie genau einmal da ist |
| Riskante Aktion | `DangerAction` (eigenes Formular), `RowConfirm` (Zeile in Tabelle/Liste, im umgebenden Formular) | Siehe „Riskante Aktionen“ |
| Filter, Suche | `FilterBar` | Siehe „Filter“ |
| Status in Tabelle/Detail | `StatusChip` (`StatusLevel`: Neutral, Success, Warning, Error) | Kein eigenes `class="status …"`; Zuordnung Enum → Stufe in `Labels.Level(…)` |
| Icon | `Icon` mit `AppIcons.*` | Nur Icons aus `AppIcons` (eine Stelle kennt die Quelle); dekorativ, außer mit `Label` |
| Marke, Sprunglink | `BrandMark`, `SkipLink` | In jedem Layout |
| Erklärung | `PageHeader.Subtitle`, `p.help`, `details.explain` | Jede Seite sagt im Untertitel, wozu sie da ist. Kurze Hilfe zu einem Abschnitt als `<p class="help">` direkt unter der Überschrift; längere Hintergründe (Rechtsgrundlage, Rechenweg) als einklappbares `<details class="explain">` mit sprechender Überschrift, nicht als Textblock vor dem Formular |
| Gruppe in langer Liste | `details.group` | Eine Liste mit vielen Zeilen wird nach einem fachlichen Merkmal gruppiert (Beispiel Offene Posten: ein Block je Mietverhältnis mit Summe in der Kopfzeile; bis drei Gruppen offen, sonst zugeklappt) |
| Verlauf | `ChangeHistory` | Auf Detailseiten von Stammdaten |
| Text mit Markup (Vorlagen) | `CodeEditor` | Textarea mit Syntaxfarben (HTML und `{{ … }}`), Werkzeugleiste (Fett, Absatz, Liste, Tabelle, Wenn/Je Eintrag, Baustein) und Kürzeln (Strg+B/I/U/S); gibt den Text kurz nach dem Tippen und beim Verlassen zurück (`wwwroot/js/code-editor.js`) |
| Balkendiagramm der Mieterabrechnung | `StatementResultChart`, `StatementCostChart`, `StatementComparisonChart` (`.cost-chart.statement-chart`) | Alle Zeilen auf einer Skala, feste Wertspalte; Ergebnis in Worten, ruhige Fläche statt Warn- oder Fehlerfarbe für eine Nachzahlung; alle Beträge als sichtbarer Text, Legende in `.chart-legend` (`.swatch`, `.swatch-marker`); ohne darstellbare Werte kein Diagramm |
| Kennzahl | `KpiTile` (`Label`, `Value`, `Note`, `Href`, `Level`) | Link, sobald eine Seite dahinter steht; optional `ProgressMeter` als Inhalt |
| Fortschritt | `ProgressMeter` (`Value`, `Total`, `Text`) | Ist gegen Soll; die Zahlen stehen zusätzlich als Text daneben |
| Teile eines Ganzen | `SegmentBar` (`ChartSegment`) | Gestapelter Balken mit Legende; Teile ohne Wert entfallen |
| Verlauf über die Zeit | `LineChart` (`ChartPoint`, `Steps`, `StartAtZero`, `FormatValue`) | SVG ohne JavaScript, Achsen als Text, immer „Als Tabelle anzeigen“; Marker (`ChartPoint.Marker`) erscheinen in der Legende; Indexreihen mit `StartAtZero="false"`, Beträge bleiben bei null |
| Beträge je Zeile | `CostBars` (`ChartBar`) | Gemeinsame Skala, Betrag als Text |
| Stand eines Ablaufs | `ProcessTrack` (`ProcessStep`, `ProcessState`) | Zustand **aus den Daten ableiten**, keine eigene Checkliste speichern; was die Daten nicht wissen, ist `Unknown` (Link ohne Haken). Waagerecht, unter 900 px senkrecht |
| Fachbegriff erklären | `HelpTerm` (`Term`, `Text`) | Klick oder Tippen statt Überfahren (Popover, ohne JavaScript); Texte zentral in `Glossary`, auch für die Hilfe |
| Hausplan | `HouseOverview` | Geschosse von oben nach unten, Breite folgt der Wohnfläche; Miete und Rückstand nur mit Finanz-Recht. Geschoss aus `Unit.Floor`, sonst aus der Lage (`BuildingFloor`) |
| Rücksprung aus einem Ablauf | `ReturnBar` (im Layout), `ReturnTarget.AppendTo(link, rueckweg)` | Wer eine Seite aus einem Ablauf heraus öffnet (Abrechnungsassistent „Beheben“, Schritte des Mieterwechsels), hängt `?zurueck=<Pfad>` an; die Leiste unter den Tabs führt zurück. Angenommen werden nur Pfade unter `/verwaltung/`, alles andere ignoriert die Leiste |
| Erstes leeres Objekt | `EmptyIllustration` | Nur beim allerersten leeren Zustand einer Installation |
| Datum, Geld, Zahl | `Format.*`, Enum-Texte `Labels.*` | Nie `ToString("dd.MM.yyyy")` o. ä. in Seiten |

### Buttons

Je Abschnitt (Formular, Dialog, Karte) gibt es **höchstens eine** hervorgehobene Aktion (`button-primary`, gefüllt): die, die der Benutzer an dieser Stelle am wahrscheinlichsten will. Abschließende Aktionen eines Formulars sind primär, Nebenaktionen (Verwerfen, Zurücksetzen) nicht.

| Klasse | Zweck |
|---|---|
| `button button-primary` | Hauptaktion (Speichern, Anlegen) |
| `button` (tonal) | Gleichwertige Nebenaktion |
| `button button-outlined` | Abbrechen, Zurück, Alternativen |
| `button button-text` | Tertiäre Aktion in Zeilen und Kopfleisten |
| `button button-danger` | Nur innerhalb von `DangerAction` (siehe unten) |

### Meldungen

Vier Stufen über `<Notice Level="NoticeLevel.…">`; Farbe allein trägt keine Bedeutung, der Text sagt es auch.

| Stufe | `NoticeLevel` | Zweck | `role` |
|---|---|---|---|
| Information | `Info` (blau) | Neutrale Hinweise, Zustände („Dieser Vorschlag wurde verworfen“) | `status` |
| Erfolg | `Success` (grün) | Aktion erfolgreich | `status` |
| Warnung | `Warning` (gelb) | Fachlicher Hinweis, Handlung empfohlen (z. B. anschaffungsnahe Herstellungskosten) | `status` |
| Fehler | `Error` (rot) | Aktion fehlgeschlagen, Eingabe unzulässig, Abrechnung nicht belastbar | `alert` |

`id` und weitere Attribute werden an das Element durchgereicht (`<Notice Level="…" id="deadline">`).

### Riskante Aktionen

Alles, was löscht, sperrt, storniert oder sich nicht einfach zurücknehmen lässt, läuft **immer** mit Rückfrage: als eigene Aktion über `DangerAction`, in Tabellenzeilen und Listen über `RowConfirm` (`Name`/`Value` wie beim bisherigen Zeilenknopf, `Label` nennt das Betroffene für Screenreader). `button-danger` außerhalb dieser Bausteine meldet ein Architekturtest. Ausgenommen ist nur das Entfernen einer noch nicht gespeicherten Entwurfszeile.

### Filter

Filter und Suche einer Liste oder Auswertung stehen in `<FilterBar>`: GET-Formular mit `role="search"`, die Felder als `div.field` im Inhalt, Werte über `[SupplyParameterFromQuery]`, deutsche Query-Parameter. „Zurücksetzen“ erscheint automatisch, sobald die URL Parameter hat (`AllowReset="false"` für Assistenten und Jahresauswahl). Weitere Aktionen zur aktuellen Ansicht (Export) gehören in den Slot `Actions`. Ein rohes `<form method="get">` meldet ein Architekturtest.

## Interaktive Seiten

Grundlage: [ADR 0005](adr/0005-interaktive-seiten-mit-mudblazor.md). Muster: `Components/Pages/Admin/People/ListPage.razor`.

- `@rendermode InteractiveServer`, `@using MudBlazor` in der Seite (nicht global), Inhalt in `<InteractiveShell>` (Provider, Hell/Dunkel, Marker `data-interactive`). Ein Architekturtest meldet interaktive Seiten ohne `<InteractiveShell>` und ein `@using MudBlazor` in einer `_Imports.razor`.
- MudBlazor-Komponenten dürfen direkt verwendet werden. Look über `M3Theme` und die Tokens, keine eigenen Farben an der Komponente.
- Eigene Komponenten haben **keine MudBlazor-Typen in der Schnittstelle**, damit sie ersetzbar bleiben (ein Architekturtest prüft alle `[Parameter]` und `[CascadingParameter]`; einzige Ausnahme ist die `IMudDialogInstance`, die der Dialogdienst jedem Dialog selbst übergibt).
- Rückmeldung nach einer Aktion über `Notifier` (`Success`, `Info`, `Warning`, `Error`), nicht über `StatusMessage`/Redirect-Cookie (eine Circuit kann keine Cookies setzen). Nach einer Weiterleitung hängt die Seite `?meldung=<Schlüssel>` an die URL (siehe unten); `InteractiveShell` der Zielseite zeigt den Text als Snackbar. `<StatusMessage>` und `Redirector` gehören auf statische Seiten, ein Architekturtest meldet sie in interaktiven Seiten.
- Formulare sind ein `EditForm` mit `OnValidSubmit`; `[SupplyParameterFromForm]` und `@formname` gehören zu statischen Seiten und sind in interaktiven Seiten verboten (Architekturtest).
- Anmeldung, Magic Link, Abmeldung und die Konto-Seiten bleiben statisch, weil sich Cookies nicht über eine Circuit setzen lassen (Architekturtest für alles unter `Components/Account`). Bekannte Ausnahme: die Wiederherstellung bei der Einrichtung (`RestoreSetup`); die Liste im Test darf nur schrumpfen.
- Die Query-Parameter liefern nur den Startwert; danach hält die Seite ihren Zustand. Live-Eingaben mit `DebounceInterval` und `OnDebounceIntervalElapsed`, nicht mit `ValueChanged` allein.
- Riskante Aktionen auch hier mit Rückfrage über den Dienst `Confirmations` (`AskAsync`), Zeilenaktionen als `MudButton` mit `aria-label`.
- Assistenten halten ihre Schritte als Zustand der Seite (Weiter/Zurück ohne Seitenwechsel); der Startschritt kommt aus dem Query-Parameter.
- Keine `return;` im Markup innerhalb von `<InteractiveShell>`: Lade-, Nicht-gefunden- und Inhaltszustand als `if / else if / else` (Architekturtest).
- Steuerelemente mit `ValueExpression` brauchen ein umgebendes `EditForm`; ohne Validierung genügt `Value`/`ValueChanged`.

**Eingabekomponenten** (`Components/Controls`): `AppTextField`, `AppSelect<T>`, `AppDateField`, `AppTimeField` (Uhrzeit mit Uhr-Auswahl, `TimeOnly?`), `AppNumberField<T>`, `AppMonthField` (Monat als `yyyy-MM`), `AppCheckBox`, `AppSwitch` (Ein/Aus mit Erklärung, zeigt weitere Einstellungen an), `AppRadioGroup<T>`, `AppFileUpload` (auch mehrere Dateien). Dazu `OptionSelect` (Auswahl aus `(Id, Name)`-Listen, z. B. Objekt, Zähler). Sie sind der Standard in interaktiven Formularen (einheitlicher Look, Beschriftung im Rahmen, deutsche Zahlen und Datumsangaben) und haben nur einfache Parameter (`@bind-Value`, `Label`, `Hint`, `Required`, `Suffix`), keine MudBlazor-Typen. Einträge eines Auswahlfelds: `Items` und `Text`. Sie validieren über das umgebende `EditForm`. Eine Komponente, die den Wert selbst hält und einen eigenen Handler für `ValueChanged` nutzt, darf den Parameter nicht blind neu lesen (siehe `AppDateField`).

**Formulare und Dialoge** (Muster: `People/PersonEditor`, `PersonDialog`, `Create`):
- Eingabefelder in einer Komponente (`*Editor`), die in einem `EditForm` mit `DataAnnotationsValidator` sitzt; `For="() => Modell.Eigenschaft"` zeigt die deutschen Validierungsmeldungen. Das `EditForm` bekommt `novalidate` (sonst erscheint die englische Browser-Meldung), die Mud-Felder `ShrinkLabel="true"` (Beschriftung im Rahmen wie bei statischen Feldern).
- Kleine Formulare ohne eigene Fachlogik (Betrag ab Stichtag, Staffel, Mietmodell, Zähler anlegen/tauschen, AfA-Kosten …) öffnet `IDialogService.ShowFormAsync(titel, modell, felder, speichern, …)` (`Common/FormDialog`): die Seite liefert Modell, die Felder als `RenderFragment` und den Speicheraufruf (`Result`); Fehler stehen im Dialog, `true` heißt gespeichert, die Seite lädt neu. Kein aufklappbares `<details>`-Formular mehr für Hinzufügen oder Ändern.
- **Dialog oder Seite beim Anlegen:** Eigenständige Datensätze (Objekt, Person, Bankkonto, Vertrag, Beleg, Dokument) werden auf einer eigenen Seite angelegt (Muster `Properties/Create`, `People/Create`), danach Weiterleitung mit `?meldung=angelegt`: Man landet auf dem Ergebnis, die Seite hat Platz für viele Felder, Zurück-Taste und Browser-Autofill. Teile eines geöffneten Objekts (Einheit, Fläche, Zimmer, Zählerstand, Bankverbindung, Staffel) sind Dialoge, damit der Kontext bleibt. Wiederholt Anzulegendes (Einheiten) bietet im Dialog „Speichern und weitere anlegen“. Ein Dialog darf auch einen eigenständigen Datensatz anlegen, wenn der Benutzer dafür einen Ablauf nicht verlassen soll (z. B. „Neue Person“ im Vertragsformular).
- **Listen legen nie inline an:** Auf Listenseiten (Darlehen, Wartung, Hausmeister-Aufgaben, CSV-Profile, Umlageschlüssel, Zählerliste) steht „… anlegen“ als Schaltfläche im Seitenkopf und öffnet einen Dialog (`ShowFormAsync`); darunter steht kein Formular.
- Anlegen und Bearbeiten zeigen dieselben Felder (dieselbe `*Editor`-Komponente); selten Gebrauchtes steht unter „Weitere Angaben“, nicht in einer abgespeckten Schnellanlage.
- Dialog: `IDialogService.ShowAsync<…Dialog>`, Inhalt im `EditForm class="dialog-form"` (hält Inhalt scrollbar und Aktionen sichtbar), Erfolg → `Notifier.Success`, `MudDialog.Close(DialogResult.Ok(id))`, die Liste lädt neu. Fehler aus Diensten über `ErrorList` im Dialog.
- Nach dem Speichern auf einer Seite weiterleiten: `Navigation.NavigateTo("…?meldung=gespeichert")`; die Zielseite zeigt den festen Text (interaktiv als Snackbar durch `InteractiveShell`, statisch durch `StatusMessage`; Schlüssel in `StatusMessage.Keys`: `gespeichert`, `angelegt`, `geloescht`, `importiert`, `verworfen`, `auszug-erfasst`, `objekt-angelegt`, `freigegeben`; neue Texte dort ergänzen). Freitext in der URL gibt es nicht.
- Testen: Anlegen und Bearbeiten im Browser-Test (Dialog, Validierung, Snackbar, Audit-Benutzer); HTTP-Tests legen Daten über Dienste an.
- Tests: Browser-Test auf `[data-interactive=true]` warten, dann bedienen; Debounce-Timer brauchen die vorgestellte Testuhr (`App.Clock`).

Die mechanisch prüfbaren Regeln dieses Abschnitts stehen in `tests/Elocaro.Architecture.Tests/InteractiveUiRulesTests.cs` (Ergänzung zu den Regeln für statische Seiten in `UiRulesTests`).

## Seitentypen

| Typ | Aufbau |
|---|---|
| Liste | `PageHeader` (Aktion „Neu“) → Filter → Tabelle (`table-wrapper`) → `Pagination`; leer → `EmptyState` |
| Detail | `PageHeader` (Zurück, Aktionen) → Kerndaten (`.summary-card` mit `.details`) → `MudTabs` mit Abschnitten, `ChangeHistory` im Tab „Verlauf“, riskante Aktionen im Menü oder im passenden Tab |
| Formular | `PageHeader` → `ErrorList` → `EditForm` mit `.form-grid` → Buttons: primär links, „Abbrechen“ daneben |
| Assistent | `PageHeader` → `Wizard` (Schritte, Fortschritt, Karte je Schritt, Zurück/Weiter eindeutig beschriftet) |
| Auswertung | `PageHeader` (Download-Aktionen) → Filter (Jahr, Objekt) → Hinweise → Tabelle |

## Texte

**Begriffe der Oberfläche:** das Dokument heißt „Vertrag“, die Beziehung „Mietverhältnis“; für den Benutzer „Forderung“ („Sollstellung“ nur in der Erklärung); „Umsatz“ ist die Bankbuchung, „Zahlung“ die zugeordnete Buchung im Mieterkonto; „Nebenkosten“ im Bedienen, „Betriebskosten“ nur, wo das Gesetz gemeint ist; „WG“ steht nur für Wohngemeinschaft, „Wirtschaftsgüter“ wird ausgeschrieben.

Deutsch, Sie-Form, sachlich. Buttons mit Verb („Speichern“, „Vertrag anlegen“). Fehlermeldungen sagen, was zu tun ist. Beträge `1.234,56 €`, Daten `20.10.2026`, fehlende Werte „–“ (alles über `Format`).

## Checkliste für neue Seiten

- [ ] Layout über `_Imports.razor` des Ordners, `PageHeader` vorhanden
- [ ] Nur Bausteine und Token-Farben, kein Hex, keine Inline-Styles
- [ ] Höchstens eine `button-primary` je Abschnitt
- [ ] Leerer Zustand und Fehlerfall gestaltet
- [ ] Riskante Aktionen mit `DangerAction`
- [ ] Formatierung über `Format`/`Labels`
- [ ] Bei 360 px ohne horizontales Scrollen, hell und dunkel geprüft
- [ ] Neuer Baustein oder neue Klasse? Dann hier dokumentieren

## Prüfen

- `UiRulesTests` (Architektur): Bausteine statt Rohmarkup.
- `UiAuditTests`/`PageSmokeTests` (Browser): Bedienelemente auf dem Handy (Touch-Ziel mindestens 44 px, Buttons als Pill, schwebende Hauptaktion ausgenommen), Textkontrast nach WCAG AA in Hell und Dunkel auf den Hauptseiten, alle Bereiche bei 360 px ohne horizontales Scrollen (nennt die überstehenden Elemente).
- Screenshot-Tour zum Ansehen: `ELOCARO_TOUR=1 ELOCARO_HEADED=1 ELOCARO_TOUR_DIR=/pfad dotnet test --filter Screenshot_tour` (sichtbarer Browser: `ELOCARO_HEADED=1`, braucht ein Display). Die Tour über alle Seiten mit Daten (`Screenshot_tour_of_all_pages`) lässt sich mit `ELOCARO_TOUR_ONLY=/personen,bank/regeln` auf Pfade beschränken, die einen der Teile enthalten.

Auf dem Handy wachsen Kartenlisten (`MudTable`) mit dem Inhalt und scrollen mit der Seite; der Info-Button steht in der Titelzeile, solange er die einzige Nebenaktion ist.
