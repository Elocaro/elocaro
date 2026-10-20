# UI-Leitfaden

Verbindlich für alle neuen und geänderten Seiten. Grundlage: ADR 0003 (eigenes CSS statt Framework) und ADR 0004 (Material 3 Expressive). Technische Details zu Blazor-Formularen stehen in [entwicklung.md](entwicklung.md#oberfläche).

## Prinzipien

1. **Zwei Arten von Seiten.** Statische Seiten (Standard, ohne eigenes JavaScript: Links, GET-/POST-Formulare, `<details>`) und interaktive Seiten (`@rendermode InteractiveServer`, MudBlazor, siehe [Interaktive Seiten](#interaktive-seiten)). Neue Seiten mit Live-Suche, Dialogen oder Assistenten werden interaktiv gebaut, alles andere bleibt statisch, bis es umgestellt wird.
2. **Mobil zuerst.** Jede Seite funktioniert bei 360 px ohne horizontales Scrollen; Touch-Ziele ≥ 44 px.
3. **Bausteine statt Rohmarkup.** Gibt es für etwas einen Baustein (siehe unten), wird er benutzt. Fehlt einer, wird er im Design-System ergänzt und hier dokumentiert, nicht in der Seite gebaut.
4. **Farben nur über Rollen.** Keine Hex-Werte in Seiten oder Komponenten. Hell und Dunkel müssen beide lesbar sein.
5. **Barrierearm.** Kontrast nach AA, sichtbarer Fokus, `role="alert"` bzw. `"status"` bei Meldungen, Beschriftung für jedes Feld.

## Design-Tokens (`wwwroot/app.css`)

| Gruppe | Tokens | Verwendung |
|---|---|---|
| Farbe (Markenfarbe) | `--md-primary`, `--md-on-primary`, `--md-primary-container`, `--md-on-primary-container` | Hauptaktion, aktive Navigation |
| Farbe (ergänzend) | `--md-secondary*`, `--md-tertiary*` | Tonal-Buttons, Akzente |
| Farbe (Status) | `--md-info*`, `--md-success*`, `--md-warning*`, `--md-error*` | Meldungen, Status-Chips |
| Flächen | `--md-surface`, `--md-surface-container-lowest … -highest`, `--md-on-surface`, `--md-on-surface-variant`, `--md-outline`, `--md-outline-variant` | Hintergründe, Text, Linien |
| Form | `--shape-xs … -xl`, `--shape-full` | Rundungen: Felder `md`, Karten/Tabellen `lg`, Dialoge `xl`, Buttons und Navigation `full` (Pill) |
| Schrift | `--type-display`, `-headline`, `-headline-small`, `-title`, `-body`, `-label` | Überschriften, Fließtext, Beschriftungen |
| Bewegung | `--motion-spatial`, `--motion-effects`, `--motion-duration` | Übergänge; bei „reduzierte Bewegung“ wird die Dauer 0 |

Die älteren Namen `--color-*` und `--radius` sind Aliase und nur für vorhandenen Code gedacht.

Hell/Dunkel: Jede Farbrolle ist mit `light-dark(hell, dunkel)` definiert. Es gilt die Systemeinstellung; der Benutzer kann sie im Benutzermenü auf *Hell* oder *Dunkel* festlegen. Das speichert das Cookie `miethahn.theme` (`ThemePreference`) und setzt `data-theme` am `<html>`-Element.

## Schrift, Icons, Felder

- **Schrift:** Roboto Flex (Variable Font, OFL), lokal in `wwwroot/fonts/` mit Lizenztext; kein CDN.
- **Icons:** Inline-SVG über `Icon`, Pfade in `AppIcons` (aktuell die Material-Icons aus MudBlazor).
- **Felder:** Outlined, Beschriftung im Rahmen (`FormField` und `div.field`). Der Hintergrund der Beschriftung kommt aus `--field-surface`; Container mit anderer Fläche setzen sie (siehe `.account-card`).

## Bausteine

| Bedarf | Baustein | Regel |
|---|---|---|
| Seitenkopf | `PageHeader` | Jede Seite außer Anmeldung und Fehlerseiten; genau ein `<h1>`; Aktionen im `Actions`-Slot |
| Formularfeld | `FormField` + `Input*` | Beschriftung immer sichtbar; Pflicht und Hinweis über Parameter |
| Geld, Fläche | `InputAmount`, `InputArea` | Nie `type="text"` für Zahlen |
| Tabelle | `.table-wrapper > .data-table` | Immer im Wrapper; Zahlen in `td.number`; Sortieren mit `SortHeader`, Blättern mit `Pagination` |
| Hinweis | `Notice` (Level Info/Success/Warning/Error), `StatusMessage` (nach Weiterleitung), `ErrorList` (Fehler aus Diensten) | Kein eigenes `class="message …"`; `role` setzt `Notice` selbst |
| Leerer Zustand | `EmptyState` | Unterscheiden: „Keine Treffer für …“ (Filter aktiv) und „Noch keine …“ |
| Riskante Aktion | `DangerAction` (eigenes Formular), `RowConfirm` (Zeile in Tabelle/Liste, im umgebenden Formular) | Siehe „Riskante Aktionen“ |
| Filter, Suche | `FilterBar` | Siehe „Filter“ |
| Status in Tabelle/Detail | `StatusChip` (`StatusLevel`: Neutral, Success, Warning, Error) | Kein eigenes `class="status …"`; Zuordnung Enum → Stufe in `Labels.Level(…)` |
| Icon | `Icon` mit `AppIcons.*` | Nur Icons aus `AppIcons` (eine Stelle kennt die Quelle); dekorativ, außer mit `Label` |
| Marke, Sprunglink | `BrandMark`, `SkipLink` | In jedem Layout |
| Verlauf | `ChangeHistory` | Auf Detailseiten von Stammdaten |
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

- `@rendermode InteractiveServer`, `@using MudBlazor` in der Seite (nicht global), Inhalt in `<InteractiveShell>` (Provider, Hell/Dunkel, Marker `data-interactive`).
- MudBlazor-Komponenten dürfen direkt verwendet werden. Look über `M3Theme` und die Tokens, keine eigenen Farben an der Komponente.
- Eigene Komponenten haben **keine MudBlazor-Typen in der Schnittstelle**, damit sie ersetzbar bleiben.
- Rückmeldung nach einer Aktion über `Notifier` (`Success`, `Info`, `Warning`, `Error`), nicht über `StatusMessage`/Redirect-Cookie.
- Die Query-Parameter liefern nur den Startwert; danach hält die Seite ihren Zustand. Live-Eingaben mit `DebounceInterval` und `OnDebounceIntervalElapsed`, nicht mit `ValueChanged` allein.
- Riskante Aktionen auch hier mit Rückfrage (MudBlazor-Dialog).

**Formulare und Dialoge** (Muster: `People/PersonEditor`, `PersonDialog`, `Create`, `Edit`):
- Eingabefelder in einer Komponente (`*Editor`), die in einem `EditForm` mit `DataAnnotationsValidator` sitzt; `For="() => Modell.Eigenschaft"` zeigt die deutschen Validierungsmeldungen. Das `EditForm` bekommt `novalidate` (sonst erscheint die englische Browser-Meldung), die Mud-Felder `ShrinkLabel="true"` (Beschriftung im Rahmen wie bei statischen Feldern).
- Dialog: `IDialogService.ShowAsync<…Dialog>`, Inhalt im `EditForm class="dialog-form"` (hält Inhalt scrollbar und Aktionen sichtbar), Erfolg → `Notifier.Success`, `MudDialog.Close(DialogResult.Ok(id))`, die Liste lädt neu. Fehler aus Diensten über `ErrorList` im Dialog.
- Nach dem Speichern auf einer Seite weiterleiten: `Navigation.NavigateTo("…?meldung=gespeichert")`; `StatusMessage` der Zielseite zeigt den festen Text (`StatusMessage.Keys`: `gespeichert`, `angelegt`, `geloescht`). Freitext in der URL gibt es nicht.
- Testen: Anlegen und Bearbeiten im Browser-Test (Dialog, Validierung, Snackbar, Audit-Benutzer); HTTP-Tests legen Daten über Dienste an.
- Tests: Browser-Test auf `[data-interactive=true]` warten, dann bedienen; Debounce-Timer brauchen die vorgestellte Testuhr (`App.Clock`).

## Seitentypen

| Typ | Aufbau |
|---|---|
| Liste | `PageHeader` (Aktion „Neu“) → Filter → Tabelle (`table-wrapper`) → `Pagination`; leer → `EmptyState` |
| Detail | `PageHeader` (Zurück, Aktionen) → Kerndaten (`.details`) → Abschnitte (`<section>` mit `<h2>`) → `ChangeHistory` → `DangerAction` ganz unten |
| Formular | `PageHeader` → `ErrorList` → `EditForm` mit `.form-grid` → Buttons: primär links, „Abbrechen“ daneben |
| Assistent | `PageHeader` → `.steps` → je Schritt ein Formular; Zurück/Weiter eindeutig beschriftet |
| Auswertung | `PageHeader` (Download-Aktionen) → Filter (Jahr, Objekt) → Hinweise → Tabelle |

## Texte

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
- `UiAuditTests` (Browser): Textkontrast nach WCAG AA in Hell und Dunkel auf den Hauptseiten, alle Bereiche bei 360 px ohne horizontales Scrollen (nennt die überstehenden Elemente).
- Screenshot-Tour zum Ansehen: `MIETHAHN_TOUR=1 MIETHAHN_HEADED=1 MIETHAHN_TOUR_DIR=/pfad dotnet test --filter Screenshot_tour` (sichtbarer Browser: `MIETHAHN_HEADED=1`, braucht ein Display).
