# Entwicklung

## Voraussetzungen

- .NET SDK 10.0.1xx (`global.json`)
- Docker (für die lokale Datenbank und die Integrationstests via Testcontainers)

## Lokale Dienste

```bash
docker compose -f deploy/docker-compose.dev.yml up -d
```

| Dienst | Adresse | Zweck |
|---|---|---|
| PostgreSQL 18 | `localhost:5432` (miethahn/miethahn) | Datenbank |
| Mailpit | SMTP `localhost:1025`, Web http://localhost:8025 | fängt alle ausgehenden Mails ab |
| Gotenberg 8 | http://localhost:3000 | HTML → PDF (Chromium) |

Die Verbindung für die Entwicklung steht in `src/MietHahn.Web/appsettings.Development.json`. In Produktion kommt sie **nur** aus der Umgebung: `ConnectionStrings__MietHahn=...`.

Beim Start wendet die App ausstehende Migrationen automatisch an. Abschalten mit `Datenbank__AutomatischMigrieren=false`. Beim allerersten Start auf einer leeren DB protokolliert EF/Npgsql einmal einen fehlgeschlagenen `SELECT` auf die History-Tabelle `__ef_migrationen`. Das ist erwartet.

Die Dateiablage liegt in der Entwicklung unter `src/MietHahn.Web/daten/dateien` (per `.gitignore` ausgeschlossen).

## Befehle

```bash
dotnet build                              # Warnungen gelten als Fehler
dotnet test                               # alle Tests (Integrationstests brauchen Docker)
dotnet test --project tests/MietHahn.Domain.Tests
dotnet format                             # Formatierung anwenden (CI prüft mit --verify-no-changes)
dotnet run --project src/MietHahn.Web
dotnet tool restore                       # lokale Tools (dotnet-ef)
```

## Migrationen

```bash
dotnet ef migrations add <Name> -p src/MietHahn.Infrastructure -s src/MietHahn.Infrastructure -o Persistenz/Migrationen
```

- Für jede Modelländerung wird eine Migration angelegt. Der Test `Modell_und_Migrationen_sind_synchron` schlägt sonst fehl.
- `Persistenz/Migrationen/` enthält nur generierten Code (von Analyzern und Formatierung ausgenommen). Hilfen liegen in `Persistenz/MigrationsHilfen/`.
- Kein Hand-Editieren generierter Migrationen außer für ergänzendes SQL, z. B. Überlappungsschutz (siehe unten).

## Persistenz-Konventionen

| Thema | Regel |
|---|---|
| Basisklasse | Jede Entität erbt von `Domain.Gemeinsam.Entitaet` |
| Schlüssel | `Guid` v7 (`Guid.CreateVersion7()`), wird im Code erzeugt, nicht in der DB |
| Audit | `ErstelltAm/Von`, `GeaendertAm/Von` setzt `EntitaetSpeichernInterceptor` (Zeit aus `TimeProvider`, Benutzer aus `IAktuellerBenutzer`) |
| Löschen | Immer logisch: `Remove()` setzt `Geloescht = true`. Ein globaler Filter (`SoftDelete`) blendet gelöschte Zeilen aus, `IgnoreQueryFilters()` zeigt sie |
| Nebenläufigkeit | `Version` ↔ PostgreSQL-Systemspalte `xmin`. Konflikte werfen `DbUpdateConcurrencyException` |
| Namen | Tabellen und Spalten in snake_case (`EFCore.NamingConventions`) |
| Geld | `decimal` → `numeric(18,2)`. Abweichende Präzision (Mengen, Faktoren) per `HasPrecision` je Property |
| Datum | `DateOnly` → `date`, Zeitpunkte `DateTimeOffset` → `timestamptz` (UTC) |
| Sortierung | Namensspalten mit `.UseCollation(MietHahnDbContext.DeutscheKollation)` (ICU `de-DE`) |
| Konfiguration | Je Entität eine `IEntityTypeConfiguration<T>` in `Infrastructure/Persistenz/Konfigurationen/`, sie wird automatisch gefunden |

### Zeiträume (`Zeitraum`)

`Domain.Gemeinsam.Zeitraum` ist das Value Object für alle Gültigkeiten: inklusive Grenzen, `Bis = null` heißt unbefristet. Es bietet `Enthaelt`, `UeberschneidetSich`, `Schnitt`, `Tage`, `TageIn(abrechnungsjahr)` (taggenaue Anteile bei Mieterwechsel), `Kalenderjahr`, `Monat` und `SindUeberlappungsfrei`.

In EF wird jede `Zeitraum`-Property automatisch als Complex Type abgebildet: `Gueltigkeit` → Spalten `gueltigkeit_von`, `gueltigkeit_bis`.

### Überlappungsfreie Zeiträume

Für zeitabhängige Daten (Verträge, Mietbestandteile, Flächen …) erzwingt die DB, dass sich Zeiträume innerhalb einer Gruppe nicht überschneiden. Dazu in der Migration nach dem Anlegen der Tabelle:

```csharp
migrationBuilder.ErstelleUeberlappungsschutz(
    name: "ex_mietvertraege_mietobjekt_zeitraum",
    tabelle: "mietvertraege",
    vonSpalte: "beginn",
    bisSpalte: "ende",            // NULL = unbefristet
    "mietobjekt_id");             // Gruppierung (Gleichheit)
```

In `Down()` steht dann `migrationBuilder.EntferneUeberlappungsschutz(name, tabelle)`. Beide Grenzen sind inklusive. Logisch gelöschte Zeilen zählen nicht. Geprüft wird erst beim Commit (`DEFERRABLE INITIALLY DEFERRED`), weil beim Fortschreiben einer Zeitreihe (alten Stand beenden, neuen einfügen) die Befehlsreihenfolge in `SaveChanges` nicht festgelegt ist. Ein Verstoß (`23P01`) kommt daher als `DbUpdateException` oder direkt als `PostgresException`: erkennen mit `Datenbankfehler.IstUeberlappung(ex)`.

### Zeitabhängige Werte (`Zeitreihe`)

Flächen, Mietbestandteile, Personenanzahl usw. werden nie überschrieben. Ein Stand implementiert `IZeitabhaengig` (`Gueltigkeit`, `GueltigkeitBeenden`) und wird mit `Zeitreihe.Fortschreiben(staende, ab, g => new Stand(…, g, …))` gepflegt: Der jüngste Stand endet am Vortag, beginnt er am selben Tag, wird er korrigiert. Ein Stichtag vor dem jüngsten Stand wirft `RegelverletzungException`. Gelesen wird mit `Zeitreihe.Am(staende, tag)`.

Kinder eines Aggregats (z. B. `Einheit.Flaechen`, `Objekt.Einheiten`) sind mit `DeleteBehavior.Cascade` konfiguriert: Entfernen aus der Liste markiert sie als gelöscht, der Interceptor macht daraus ein logisches Löschen.

### Fachliche Fehler

Domänenregeln werfen `RegelverletzungException` mit einer für Benutzer formulierten Meldung. Anwendungsdienste fangen sie (ebenso Überlappungen und `DbUpdateConcurrencyException`) und liefern `Ergebnis` (`Erfolgreich`, `Fehler`, `Id`), das die Seite über `<FehlerListe>` anzeigt.

## Oberfläche

Statisches SSR mit Blazor (ADR 0001) und eigenem Design-System (ADR 0003, `wwwroot/app.css`, hell/dunkel).

**Layouts** (per `_Imports.razor` im Bereichsordner): `VerwaltungLayout` (Seitenleiste ab 900 px, darunter Menü als `<details>` ohne JavaScript), `ServiceLayout`, `PortalLayout`, `KontoLayout` (Anmeldeseiten) und `MainLayout` (Fehlerseiten). Neue Navigationspunkte der Verwaltung kommen in `VerwaltungNavigation.razor`, bei Bedarf in `<AuthorizeView Policy="…">`.

**Bausteine** (`Components/Gemeinsam`):

| Komponente | Zweck |
|---|---|
| `Seitenkopf` | `<h1>`, Seitentitel („X – MietHahn“), Zurück-Link, Aktionen |
| `FormularFeld` | Beschriftung + Eingabe + Hinweis + Validierungsmeldung |
| `InputBetrag` | Geldbeträge de-DE („1.234,56“, „1234.56“, „-12 €“), rundet auf Cent, Zahlentastatur |
| `SortierKopf`, `Seitenwahl` + `Tabellenabfrage` | Sortieren/Blättern über Query-Parameter (`?sortierung=…&absteigend=true&seite=2`), unbekannte Spalten fallen auf den Standard zurück |
| `GefahrenAktion` | Riskante Aktion mit Rückfrage ohne JavaScript (`<details>`) |
| `StatusMeldung`, `FehlerListe`, `Leerzustand` | Rückmeldungen (auch über Weiterleitungen hinweg per `Weiterleitung.ZuMitMeldung`) |
| `AenderungsHistorie` | Audit-Historie eines Datensatzes |

**Unterkomponenten, die selbst Daten laden** (`AenderungsHistorie`, `VertragsHistorie`), erben von `DatenKomponente` und laden über `LadenAsync<TDienst, T>(…)` in einem eigenen DI-Scope. Beim statischen Rendern laufen ihre Ladevorgänge parallel, ein gemeinsamer DbContext würde „A second operation was started on this context“ werfen. Seiten selbst nutzen die injizierten Dienste direkt.

**Mehrere Zeilenaktionen** (z. B. „Entfernen“ je Tabellenzeile): ein Formular um die Tabelle, je Zeile `<button type="submit" name="EntfernenId" value="@id">`. Formularnamen (`@formname`) müssen auf einer Seite eindeutig sein; Formulare in einer Schleife sind ein Fehler. Enums lassen sich nicht per `[SupplyParameterFromQuery]` binden: als `string` lesen und parsen.

Datum: das eingebaute `InputDate<DateOnly>` (nativer Datepicker). Monate mit `<input type="month">` als `string` binden („2026-01“) und selbst parsen (siehe `MietmodellFormular`), denn `DateOnly` lässt sich daraus nicht binden. Dezimalzahlen: `InputBetrag` (Euro) und `InputFlaeche` (m²) rendern `<input type="number" step="0.01">`. Blazor bindet Formularwerte bei statischem SSR **kulturinvariant**; ein Textfeld mit „30,5“ käme nicht als Zahl an. Ein Zahlenfeld zeigt die Zahl in der Browsersprache und sendet immer mit Punkt.

**Formularfelder in Unterkomponenten** (z. B. `ObjektFelder`, `FlaechenBereich`): Der Feldname entsteht aus dem Ausdruck in der Komponente (`() => Daten.Bezeichnung` → `Daten.Bezeichnung`). Heißt die Eigenschaft auf der Seite anders, den Präfix angeben: `[SupplyParameterFromForm(FormName = "einheit", Name = "Daten")] EinheitDaten? NeueEinheit`. Der `TestBrowser` lehnt Felder ab, die im Formular nicht existieren, und deckt solche Fehler damit auf. Optionale E-Mail-Felder mit `[OptionaleEmail]` statt `[EmailAddress]` validieren, denn leere Formularfelder kommen als `""` an. Formularmodelle in `OnInitialized(Async)` **vor** dem ersten `await` setzen (`Modell ??= new()`), weil Blazor dort schon einmal rendert. Das gilt für alles, was das Markup ohne Prüfung verwendet (z. B. Auswahllisten): vorbelegen oder mit `@if (liste is not null)` absichern. Der Zwischenstand entsteht nur, wenn eine Abfrage nicht sofort fertig ist, also vor allem unter Last; lokal fällt der Fehler leicht nicht auf. Mehrfach-Checkboxen (`name="Daten.ObjektIds"`) binden ohne Haken als `null`, nicht als leere Liste. Tabellen stehen immer in `<div class="tabellen-rahmen">`, damit sie mobil horizontal scrollen statt der Seite.

**Dashboard „Was ist zu tun?“:** Fachmodule registrieren eine `IAufgabenQuelle` und liefern `Aufgabe`n (Bereich, Titel, Link, Dringlichkeit, Fälligkeit). Die Startseite der Verwaltung sammelt sie mit Streaming Rendering ein.

**Barrierearmut:** Sprunglink „Zum Inhalt springen“, `aria-sort` an Sortierköpfen, `role="alert"/"status"` für Meldungen, sichtbarer Fokus, Touch-Ziele ≥ 44 px.

## Anmeldung & Konten

- **Sicherer Standard:** Jede Seite verlangt eine Anmeldung (Fallback-Policy). Öffentliche Seiten markiert man explizit mit `@attribute [AllowAnonymous]`; das tun z. B. alle Seiten unter `Components/Konto/Anmeldung/` per `_Imports.razor`.
- **Erster Start:** `/konto/einrichtung` legt den ersten Administrator an (nur solange kein Konto existiert). Danach ist die Zwei-Faktor-Einrichtung Pflicht.
- **Zwei-Faktor-Pflicht** für `Admin` und `Verwalter`: Solange 2FA fehlt, leitet `UseZweiFaktorPflicht()` jede Seite außerhalb von `/konto` auf `/konto/sicherheit/zwei-faktor` um. Grundlage ist der Claim `miethahn:2fa`, der nach dem Aktivieren per `RefreshSignInAsync` erneuert wird. Der QR-Code wird serverseitig als SVG erzeugt (QRCoder), es gibt 10 Wiederherstellungscodes.
- **Magic Link** (`IKontoverwaltung.AnmeldelinkAnfordernAsync`): Token mit 32 Byte Zufall, in der DB nur als SHA-256-Hash, 15 Minuten gültig, einmalig verwendbar. Eingelöst wird erst per POST-Klick auf der Seite, damit Mail-Scanner den Link nicht verbrauchen. Für Admin und Verwalter gesperrt (würde 2FA umgehen).
- **Einladungen** (`IKontoverwaltung.EinladenAsync`): Admin, Verwalter und Hausmeister erhalten einen Link zum Festlegen des Passworts, Mieter und Handwerker einen Anmeldelink. Beides ist 7 Tage gültig.
- **Passwörter:** mindestens 12 Zeichen, keine Zeichenklassen-Pflicht (NIST). Reset-Link 2 Stunden gültig. Nach 5 Fehlversuchen 15 Minuten Sperre. Konten mit `Gesperrt = true` können sich nicht anmelden (wirksam spätestens nach 10 Minuten durch die Security-Stamp-Prüfung).
- **Keine Kontenauskunft:** Anmeldung, Magic Link und Passwort-Reset antworten für bekannte und unbekannte Adressen gleich.
- **Rate-Limiting:** POST auf `/konto/*` höchstens `Anmeldung:AnfragenProMinute` (10) je IP.
- **Links in Mails** basieren auf `Anwendung:BasisUrl` (konfiguriert, nicht aus dem Host-Header).
- **Data-Protection-Schlüssel** liegen in PostgreSQL (`data_protection_keys`): Anmeldungen überstehen Neustarts und sind im DB-Backup enthalten.
- Identity rechnet Token-Laufzeiten und Sperren mit der Systemuhr (ohne `TimeProvider`). Tests prüfen daher die Konfiguration statt Zeit vorzuspulen; eigene Anmeldelinks nutzen `TimeProvider`.

## Autorisierung

| Richtlinie | Wer | Einsatz |
|---|---|---|
| `Richtlinien.Verwaltung` | Admin, Verwalter | alle Seiten unter `Components/Pages/Verwaltung/` (per `_Imports.razor`) |
| `Richtlinien.Service` | Hausmeister, Handwerker | `Components/Pages/Service/` |
| `Richtlinien.Portal` | Mieter | `Components/Pages/Portal/` |
| `Richtlinien.Fuer(Recht.X)` | Admin oder Verwalter mit Recht X | z. B. `Verwaltung/Benutzer` (Benutzerverwaltung), `Verwaltung/Betrieb` (System) |

- **Rechte** (`Recht`: Stammdaten, Finanzen, Benutzerverwaltung, System) liegen als Claims `miethahn:recht` am Konto. Admins haben immer alle, Verwalter standardmäßig Stammdaten und Finanzen. Gepflegt wird unter `/verwaltung/benutzer/{id}`.
- Unterordner-`_Imports.razor` **ergänzen** die Richtlinie des Elternordners (beide müssen erfüllt sein).
- **Wirksamkeit:** Der Security-Stamp wird bei jeder Anfrage geprüft und der Principal neu aus der DB aufgebaut. Rollen- und Rechteänderungen gelten damit ab der nächsten Anfrage, Sperren meldet sofort ab. Wer den Stamp ändert (z. B. Authenticator-Schlüssel neu erzeugen, 2FA aktivieren, Passwort ändern), muss danach `SignInManager.RefreshSignInAsync` aufrufen.
- **Startseite** `/` leitet je Rolle nach `/verwaltung`, `/service` oder `/portal`.
- **Ressourcenbasiert:** Für einzelne Datensätze `IAuthorizationService.AuthorizeAsync(user, ressource, anforderung)` mit eigenem `AuthorizationHandler<TAnforderung, TRessource>`. Beispiele: `DateiZugriffHandler` für `/dateien/{id}`; `ObjektZugriffHandler` (`ObjektRessource`, `ObjektLesen`): Recht Stammdaten sieht alle Objekte, Hausmeister nur die ihnen zugeordneten (`IObjektZuordnungen`, gepflegt unter `/verwaltung/benutzer/{id}`). Nicht erlaubte Objekte verhalten sich im Servicebereich wie unbekannte. Die Verwaltung darf alles; Fachmodule geben einzelne Dateien für andere Rollen über `IDateiFreigabe` frei (z. B. Dokumente eines Mieters).
- **Letzter Admin:** kann weder gesperrt noch herabgestuft werden. Das eigene Konto kann man nicht sperren.
- Änderungen an Konten (Anlage, Rolle, Rechte, Sperre) stehen im Audit-Log (`AuditSchreiber`, Typ `Benutzer`).

## Audit-Log

- Jede Speicherung einer `Entitaet` schreibt in **derselben Transaktion** einen Eintrag in `audit_eintraege`: Zeitpunkt, Benutzer (`IAktuellerBenutzer`), Typ, Id, Aktion (`Angelegt`, `Geaendert`, `Geloescht`) und die geänderten Felder alt → neu als `jsonb`. Speichern ohne echte Änderung erzeugt keinen Eintrag.
- Technische Felder (Id, Audit-Felder, Version) werden ausgelassen. Properties mit `[Vertraulich]` (IBAN, Zugangsdaten) erscheinen nur als `***`. Klassen mit `[OhneAudit]` (Outbox, Job-Protokoll) werden nicht protokolliert.
- **Lesende Zugriffe** auf personenbezogene Daten protokolliert man explizit: `IZugriffsprotokoll.ProtokollierenAsync("Person", id, "Mieterakte geöffnet")`. Abschaltbar mit `Audit:ZugriffeProtokollieren=false`.
- Anzeige: `<AenderungsHistorie EntitaetTyp="@nameof(Mietvertrag)" EntitaetId="@id" />`, Daten über `IAenderungsHistorie`.
- Aufbewahrung: Änderungen `Audit:AufbewahrungJahre` (10, angelehnt an steuerliche Fristen), Zugriffe `Audit:ZugriffeAufbewahrungMonate` (12). Bereinigt wird täglich durch den Job `audit-bereinigung`.

## Infrastrukturdienste

### E-Mail (`IMailVersand`)

```csharp
await mailVersand.EinreihenAsync(new MailAuftrag(["mieter@example.de"], "test", new { GesendetAm = heute, AusgeloestVon = "admin" }));
```

- Mails werden **beim Einreihen gerendert** und in `ausgehende_mails` gespeichert. Das ist zugleich der Versandnachweis. Optional gibt es einen fachlichen `Bezug`, z. B. `Abrechnung:<id>`.
- Der Job `mail-outbox` stellt alle 30 s per SMTP (MailKit) zu. Bei Fehlern wird mit Backoff wiederholt (1, 2, 4 … Minuten, höchstens 6 h), nach 8 Versuchen ist die Mail „fehlgeschlagen“. Unter `/verwaltung/system/mails` sieht man den Status, kann erneut senden und eine Testnachricht verschicken.
- Vorlagen liegen unter `src/MietHahn.Infrastructure/Mail/Vorlagen/<name>.sbn` (Scriban, als Ressource eingebettet). Der Name besteht nur aus `a-z0-9-`. Jede Vorlage setzt `{{ capture betreff }}…{{ end }}`, optional eine Textfassung per `capture text`, sonst wird sie aus dem HTML abgeleitet. Der Rahmen kommt aus `_layout.sbn`.
- Konfiguration: Abschnitt `Mail` (`AbsenderAdresse`, `AbsenderName`, `AntwortAn`, `Smtp:Host/Port/Sicherheit/Benutzer/Passwort`). `Sicherheit` ist `StartTls` (587), `SslOnConnect` (465) oder `None` (nur lokal).

### Vorlagen (Scriban)

`ScribanRenderer` wird von Mails und PDFs genutzt:
- Jede Ausgabe wird **HTML-escaped** (`& < > " '`, Umlaute bleiben lesbar). Bewusst unescaped ist nur `RohesHtml`.
- Modell-Eigenschaften stehen in snake_case bereit (`MieterName` → `mieter_name`). Unbekannte Variablen sind ein Fehler.
- Hilfsfunktionen: `geld` (1.234,50 €), `datum` (TT.MM.JJJJ, Zeitpunkte in deutscher Zeit), `zahl wert stellen`, `text_zu_html` (Zeilenumbrüche).

### PDF-Erzeugung (`IPdfErzeugung`)

```csharp
var pdf = await pdfErzeugung.ErzeugenAsync(new PdfAuftrag(
    Vorlage: "test", Titel: "Betriebskostenabrechnung 2025", Modell: daten,
    Empfaenger: new Briefanschrift(["Erika Mustermann", "Hauptstraße 5", "12345 Musterstadt"])));
var datei = await pdfErzeugung.ErzeugenUndAblegenAsync(auftrag, "Abrechnung 2025.pdf"); // unveränderlich in der Dateiablage
```

- Inhaltsvorlagen liegen unter `Infrastructure/Pdf/Vorlagen/<name>.sbn` (oder kommen als `VorlagenText` aus der Vorlagenverwaltung) und werden in `_layout.sbn` eingebettet. Die Fußzeile ist `_fusszeile.sbn` (Absender, Bank, „Seite x von y“).
- **Mit Empfänger** entsteht ein Brief nach **DIN 5008 Form B**: Briefkopf, Anschriftfeld 45 mm von oben und 20 mm von links (Rücksendezeile, Vermerkzone, 27,3 mm Empfängerzone, höchstens 6 Zeilen), Infoblock mit Datum, Falzmarken bei 105/210 mm und Lochmarke bei 148,5 mm. **Ohne Empfänger** entsteht ein schlichtes Dokument (z. B. Anlagen).
- Absenderdaten kommen aus dem `Vermieterprofil`. Ohne Profil schlägt die Erzeugung mit einer klaren Meldung fehl.
- Tabellen: `<thead>` wiederholt sich auf jeder Seite, `<tfoot>` (Summen) steht nur am Ende. Rechtsbündige Beträge mit `class="zahl"`.
- Ausgabe als **PDF/A-2b** (`Pdf:PdfA`) mit Titel und Autor in den Metadaten. Gotenberg ist nur intern erreichbar (`Pdf:GotenbergUrl`).

### Dateiablage (`IDateiablage`)

- Erlaubt sind **PDF, JPG, PNG und HEIC**. Erkannt wird am Inhalt (Magic Bytes), nicht an der Endung. Maximalgröße: `Dateiablage:MaxGroesseMb` (Standard 25).
- Fotos (JPG/PNG) werden gemäß EXIF gedreht, auf `MaxBildKante` (Standard 2560 px) verkleinert und neu kodiert. Dabei fallen **alle Metadaten inklusive GPS** weg. HEIC wird unverändert abgelegt, weil SkiaSharp es unter Linux nicht dekodiert.
- Ablage inhaltsadressiert: `{Pfad}/{sha[0..2]}/{sha256}`. Gleiche Inhalte liegen nur einmal vor. Metadaten in `dateien`. Der Dateiname wird bereinigt (keine Pfadanteile), die Endung folgt dem erkannten Typ.
- Abruf nur über `OeffnenAsync(id)`; Auslieferung über `/dateien/{id}` (`?download=true` als Anhang) mit `DateiZugriffHandler`.
- Header von `/dateien`: `nosniff`, CSP `default-src 'none'` (keine Skripte), `frame-ancestors 'self'` bzw. `X-Frame-Options: SAMEORIGIN` (Vorschau nur in der App). Alles außer PDF zusätzlich mit `sandbox`; bei PDFs fehlt sie, weil Browser PDFs in einer CSP-Sandbox nicht anzeigen.

### Datensicherung & Vollexport

- `IBackupStatus` liest die Statusdatei des Backup-Containers (`Backup:StatusDatei`, Format siehe `deploy/backup/miethahn-backup.sh`). Daraus: Dashboard-Aufgabe, Health-Check `backup` (Degraded) und der Job `backup-warnung` (täglich 8:05, Mail an aktive Admins).
- `IDatenexport` erzeugt den Vollexport generisch aus dem relationalen EF-Modell: neue Tabellen sind automatisch enthalten. Geheimnisse mit `[OhneExport]` (Klasse oder Eigenschaft) ausnehmen und die Liste in `DatenexportTests.Jede_Tabelle_ist_exportiert_oder_bewusst_ausgenommen` ergänzen. `[Vertraulich]` (z. B. IBAN) bleibt im Export, denn es sind fachliche Daten.
- Der ZIP-Export wird zuerst in eine temporäre Datei geschrieben: Die Eintrags-Streams von `ZipArchive` schreiben beim Schließen synchron, und Kestrel erlaubt kein synchrones Schreiben.

### Mietverträge

- `Mietvertrag` über ein Mietobjekt (`MietobjektId` = Einheit ohne Zimmer oder WG-Zimmer; `EinheitId` immer gesetzt, `ZimmerId` bei Zimmern). Der DB-Überlappungsschutz auf (`mietobjekt_id`, `laufzeit`) verhindert zwei Verträge zum selben Tag.
- Zeitabhängig (Zeitreihen mit Überlappungsschutz): `Mietbestandteil` je `Bestandteilart` (Kaltmiete, Vorauszahlungen, Pauschale, Zuschläge) und `Personenstand`. Eine Mieterhöhung ist ein neuer Bestandteil ab Stichtag.
- `Laufzeit.Bis` ist das tatsächliche Ende (Befristung oder Kündigung). `Kuendigen` beendet auch alle Bestandteile; `KuendigungAufheben` öffnet sie wieder.
- Kündigungstermine nach § 573c BGB (`KuendigungsterminFuerMieter`, `KuendigungsfristVermieterMonate`), Fälligkeit am n-ten Werktag: `Domain.Gemeinsam.Werktage` (Mo–Fr ohne bundesweite Feiertage, Ostern nach Gauß).
- `Abrechnungsart`: nur `Vorauszahlung` erhält eine Nebenkostenabrechnung (`ErhaeltNebenkostenabrechnung`); Heizkosten zusätzlich bei `HeizkostenVerbrauchsabhaengig`.
- **Mietmodelle** (`Mietvertrag.Mietmodell`): *Staffel* mit `Mietstaffel`n (≥ 12 Monate Abstand, § 557a). Der Job `staffelmiete` (täglich 0:30) übernimmt fällige Staffeln als Kaltmiete und mailt den Admins; das Dashboard zeigt Staffeln der nächsten 60 Tage. *Index* mit Ausgangsindex (Monat, Wert); `Indexmiete` rechnet neue Miete = Miete × VPI neu / VPI alt (auf Cent kaufmännisch), Sperrfrist 12 Monate, wirksam ab dem übernächsten Monat nach Zugang der Erklärung. Das Dashboard meldet mögliche Anpassungen. *Standard*: letzte Erhöhung und Kaltmiete vor 3 Jahren (Kappungsgrenze).
- **VPI** (`VpiWert`, `/verwaltung/vpi`): manuelle Pflege oder CSV-Import (GENESIS 61111-0002: „2026;Januar;121,2“, auch „2026-01;121,2“ oder „01.2026;121,2“; Platzhalter „...“ werden übersprungen). Ein Abruf über die GENESIS-API braucht Zugangsdaten und ist noch nicht angebunden.
- Das Vertragsmodul implementiert `IBelegungsauskunft` (Objektübersicht, Leerstand), `IPersonenbezug` (Mieter/Bürge) und `IPersonendatenQuelle` (DSGVO-Abschnitt `mietvertraege`).

### Zähler

- `Zaehler` (Namespace `Zaehlerwesen`, damit er nicht mit der Klasse kollidiert) mit Typ, Messeinheit, Zuordnung (Haus, Einheit oder Zimmer), optionalem Hauptzähler und Eichfrist (Standard nach MessEV: Kaltwasser 6, Warmwasser/Wärme 5, Gas/Strom 8 Jahre bis Jahresende). Einbau legt den Anfangsstand an.
- **Zählertausch** (`Tauschen`): Endstand am alten Zähler, neuer Zähler mit `VorgaengerId` und Anfangsstand. Alle Zähler einer Kette bilden eine Messstelle; Unterzähler wandern zum neuen Hauptzähler.
- **Verbrauch** (`Verbrauchsrechner`, rein funktional): Ablesungen gelten als Stand am Ende des Tages; Zwischenwerte werden linear interpoliert und gekennzeichnet; über die letzte Ablesung hinaus wird nicht hochgerechnet (`Verbrauch.Fehlt`). Grundlage für die Abrechnungen in M3.
- **Plausibilität** (`Zaehler.Pruefen`): Rückwärtslauf und Abweichung des Tagesverbrauchs > 50 % vom bisherigen Durchschnitt. Ohne `Bestaetigt` speichert `StandErfassenAsync` nicht, sondern liefert die Warnungen.
- **Ablese-Runde** `/verwaltung/objekte/{id}/ablesung?einheit=&datum=&art=`: ein Zähler je Schritt, Kamera-Upload (`capture="environment"`), Überspringen, Zusammenfassung. Der Mieterwechsel verlinkt die Zwischenablesung der Einheit.
- Dashboard: Eichfrist abgelaufen (dringend) bzw. in 90 Tagen (bald).

### Dokumente

- `Dokument` verweist auf eine Datei der Dateiablage und hat Titel, Kategorie, Datum, „gültig bis“, Schlagworte (`text[]`), Notiz und **mindestens einen Bezug** (Objekt, Einheit, Vertrag, Person).
- Suche (`IDokumentablage.SucheAsync`) über Titel, Schlagworte, Notiz und Dateiname (`ILIKE`). Bezugsfilter schließen Untergeordnetes ein: Zum Objekt gehören die Dokumente seiner Einheiten und Verträge, zur Person die ihrer Verträge.
- **Freigaben** (`DokumentFreigaben.SichtbareAsync`, gilt für Portal, Servicebereich und `/dateien` über `DokumentDateiFreigabe : IDateiFreigabe`):
  - *Für Mieter:* Mieter (Person mit `BenutzerId`, Vertragsrolle Mieter) sehen Dokumente ihrer Person und ihrer Verträge (auch nach dem Auszug) sowie der Einheit und des Objekts, in denen sie **aktuell** wohnen.
  - *Für Hausmeister:* Dokumente zu zugeordneten Objekten, deren Einheiten und Verträgen.
- Oberfläche: `/verwaltung/dokumente` (Suche, Kategorie), Upload `/verwaltung/dokumente/neu?objekt=|einheit=|vertrag=|person=` (vorbelegter Bezug, ohne Titel gilt der Dateiname), Detailseite mit Vorschau (Bild bzw. PDF im `iframe`). `DokumenteBereich` zeigt die Dokumente auf Objekt-, Einheit-, Vertrags- und Personenseite; der Servicebereich zeigt Hausmeister-Freigaben.
- Dashboard: Dokumente mit „gültig bis“ in 60 Tagen bzw. abgelaufen. DSGVO-Auskunft: Abschnitt `dokumente`.

### Finanzen: Sollstellungen & Mieterkonto

- `Sollstellung` = Forderung an den Mieter (negativ = Gutschrift, z. B. `Erstattung`). Monatsmieten (`Postenart.Miete`) entstehen **je Bestandteil und Leistungsmonat** automatisch, alles andere (Nachzahlung, Mahngebühr, Zinsen, Kaution, Saldovortrag …) von Hand. `Zahlung` = Eingang (positiv) bzw. Auszahlung an den Mieter (negativ), optional mit Tilgungsbestimmung (`SollstellungId`, § 366 Abs. 1 BGB).
- **Buchhaltungsbeginn** (`Finanzeinstellungen.BuchhaltungAb`, `/verwaltung/finanzen/einstellungen`): Monatsmieten erst ab diesem Monat, damit übernommene Altverträge keine Jahre an „Rückständen“ erzeugen. Ohne Beginn entstehen keine Monatsmieten (Dashboard-Hinweis). Ältere Rückstände als `Saldovortrag` erfassen.
- `Sollberechnung.Monat` (rein funktional): Bestandteile × Tage/Tage im Monat, ungerundet summiert, auf Cent kaufmännisch gerundet (Einzug 15.3. → 17/31). Fällig am n-ten Werktag, bei Einzug im Monat frühestens am Einzugstag.
- `IMieterkonten.SollstellungenErzeugenAsync` ist idempotent: legt fehlende Monatsmieten bis zum laufenden Monat an, gleicht geänderte an (rückwirkende Mieterhöhung, Auszug) und entfernt überzählige. Job `sollstellungen` täglich 0:40 (nach der Staffelmiete um 0:30), dazu Knopf im Mieterkonto. Ein eindeutiger Teilindex (`art = 'Miete'`, Vertrag, Monat, Bestandteil) verhindert Duplikate auch bei parallelen Läufen.
- **Verrechnung** (`Verrechnung.Verteilen`, rein funktional, nicht gespeichert, sondern bei jedem Lesen berechnet): Guthaben chronologisch; zuerst die bestimmte Forderung, dann die fälligen nach `Verrechnungsreihenfolge` (Standard §§ 366, 367 BGB: Kosten → Zinsen → älteste). Gleicher Rang und gleiche Fälligkeit (Bestandteile einer Monatsmiete) werden anteilig getilgt, Cent-Reste nach größtem Rest. Vorauszahlungen tilgen erst bei Fälligkeit, der Zufluss bleibt das Zahlungsdatum.
- Mieterkonto `/verwaltung/vertraege/{id}/konto`: Buchungen mit Saldo und offenem Betrag, Zahlung/Forderung erfassen, Löschen nur für manuelle Buchungen, **Mieten je Jahr** (Soll nach Leistungsmonat, davon bezahlt → Nebenkostenabrechnung; Zufluss nach Zahlungsjahr → Anlage V).
- Offene Posten `/verwaltung/finanzen/offene-posten` und als CSV (`…/offene-posten.csv`: Semikolon, deutsche Zahlen, UTF-8 mit BOM, CRLF, Formel-Präfixe entschärft). Recht **Finanzen**.

### Bank: Konten & Datei-Import

- `Bankkonto` (IBAN geprüft und eindeutig, Typ Mietkonto/Darlehen/Rücklage, Objekte als `uuid[]`), `Umsatz` (Betrag positiv = Eingang, Gegenseite, Verwendungszweck, EREF/MREF/CRED, Status offen/zugeordnet/ignoriert), `Bankimport` (Vorschau → importiert, Importprotokoll; die Datei liegt nur bis zum Import in der Datenbank).
- Parser in `Infrastructure/Bank/Import` (`Bankdateileser` erkennt das Format):
  - **camt.053** (`Camt053Parser`): Elemente über den lokalen Namen, daher .001.02 bis .08; vorgemerkte Einträge (PDNG) übersprungen, Sammler je `TxDtls` zerlegt; XML ohne DTD/externe Entitäten.
  - **MT940** (`Mt940Parser`): Feld 61 (Valuta, Buchungsdatum über den Jahreswechsel), Feld 86 strukturiert (?00, ?20–?29/?60–?63, ?30–?33).
  - **CSV** (`CsvParser`, `CsvProfil`): Spalten über Überschriften (Alternativen mit „|“), Vorspann wird übersprungen, Soll/Haben oder Betrag, Name je Richtung (DKB), Zerlegung per Regex (comdirect), Zeichensatz auto (UTF-8, sonst Windows-1252). Mitgeliefert: Sparkasse (CSV-CAMT), Volksbank, DKB, ING, comdirect (`CsvVorgaben`). Eigene Profile unter `/verwaltung/bank/csv-profile`. Passen mehrere Profile, gewinnt das mit den meisten erkannten Spalten.
  - SEPA-Kennungen im Verwendungszweck (`SepaZweck`: EREF+, MREF+, CRED+, SVWZ+, ABWA+).
- **Duplikate** (`Umsatzfingerabdruck`): SHA-256 aus Konto-IBAN, Buchungsdatum, Betrag, Verwendungszweck (ohne Leerraum, Großschreibung), Gegen-IBAN und End-to-End-ID; gleiche Umsätze innerhalb einer Datei werden durchnummeriert. Eindeutiger Index (Konto, Hash) auch über gelöschte Umsätze. Die IBAN eines Kontos mit Umsätzen ist daher nicht änderbar.
- Testdateien (anonymisiert) für alle Formate liegen unter `tests/MietHahn.Integration.Tests/Bank/Testdateien`; neue Bankformate dort mit einem Beispiel ergänzen.

### Belege & Kostenpositionen

- `Beleg` (Lieferant als Kontakt oder Name, Rechnungsnr./-datum, Leistungszeitraum als zwei Spalten, Brutto/Netto/USt mit Summenprüfung, fällig/bezahlt, Brennstoffmenge, -einheit und CO₂ in kg für das CO2KostAufG) mit Seiten (`BelegDatei`, Dateiablage, SHA-256, unveränderlich) und `Kostenposition`en.
- **Kostenarten** (`Domain.Belege.Kostenarten`): fester Katalog mit stabilen Schlüsseln, `betrkv-01` … `betrkv-17` nach § 2 BetrKV (mit Hinweis zu Kabel-TV seit 1.7.2024) und nicht umlagefähige Arten (Instandhaltung, Modernisierung, Verwaltung, Bankgebühren, Schuldzinsen, Beratung, Leerstand, Sonstiges). Eigene Unterarten und Umlageschlüssel kommen mit #28.
- `Kostenposition`: Kostenart, Betrag, Objekt (optional Einheit), Abrechnungszeitraum (Standard: Leistungszeitraum, sonst Kalenderjahr), umlagefähig (Standard laut Kostenart), § 35a EStG (haushaltsnahe Dienstleistung/Handwerkerleistung mit Arbeitskostenanteil). Die Summe der Positionen darf den Rechnungsbetrag nicht übersteigen; ein Beleg ist „aufgeteilt“, wenn sie ihm genau entspricht. Der Bruttobetrag lässt sich nicht unter die Summe senken.
- Korrekturen: Datenänderungen stehen im Änderungsprotokoll (Versionen der Werte), Dateien werden nie überschrieben, Löschen ist logisch.
- Oberfläche `/verwaltung/belege` (Filter Jahr, Objekt, Kostenart, ohne Zahlung, nicht aufgeteilt), Erfassen mit mehreren Dateien (`IFormFileCollection`) und optional gleich einer Kostenart. Dashboard: fällige unbezahlte Rechnungen (7 Tage), nicht aufgeteilte Belege. „Zahlungen ohne Beleg“ folgt mit der Zahlungszuordnung (#23).

### Personen & Datenschutz

- `Person` (Mieter, Bürgen, Kontakte) mit Anschriften als Zeitreihe (Voranschrift → aktuelle → neue Anschrift nach Auszug) und beliebig vielen `Bankverbindung`en (IBAN mit Prüfsumme, `Domain.Gemeinsam.Iban`). Mieter und Bürgen ergeben sich aus Verträgen, nicht aus einem Feld.
- **Erweiterungspunkte** für Fachmodule: `IPersonenbezug` (Rolle, Beschreibung, aktiv, Ende) steuert Rollenanzeige, Löschsperre und Aufbewahrungsfrist; `IPersonendatenQuelle` liefert einen Abschnitt für die DSGVO-Auskunft. Ein neues Modul, das Personendaten speichert, muss beide registrieren.
- **Auskunft** (`IDatenschutz.AuskunftAsync`, `/verwaltung/personen/{id}/auskunft`): JSON mit den serialisierten Entitäten (neue Felder sind automatisch enthalten), logisch gelöschten Einträgen, Konto, Bezügen, Änderungsprotokoll im Klartext und den Abschnitten der Module. Der Abruf steht im Audit-Log.
- **Anonymisieren**: entfernt Name, Kontaktdaten, Anschriften, IBANs (physisch, nicht nur logisch) und die Werte im Änderungsprotokoll. Der Datensatz bleibt für Buchungen erhalten. Vorschläge nach `Datenschutz:AufbewahrungJahre` (10) ab Ende des letzten Bezugs bzw. `Datenschutz:OhneBezugMonate` (12) ohne Bezug; Geschäftskontakte werden nicht vorgeschlagen.
- **Zugriffsprotokoll**: Öffnen einer Personenakte wird protokolliert, derselbe Zugriff (Benutzer, Datensatz, Zweck) höchstens einmal je 30 Minuten.

### Hintergrundjobs (Quartz.NET 3)

- Persistenter Store in PostgreSQL (Tabellen `qrtz_*`, angelegt per Migration aus dem offiziellen Skript `Persistenz/Sql/quartz_3_22_6_postgres.sql`). Geplante Jobs überstehen Neustarts.
- Neue Jobs registriert man in `DependencyInjection.JobsRegistrieren` mit `q.PlaneIntervall<TJob>(…)` oder `q.PlaneTaeglich<TJob>(…)` (deutsche Uhrzeit). Die Logik gehört in einen testbaren Dienst, der Job ruft ihn nur auf (Beispiel `MailOutboxJob` → `MailOutbox`).
- Jeder Lauf wird in `job_laeufe` protokolliert (`JobLaufProtokoll`). Die Einträge werden nach `Jobs:ProtokollAufbewahrungTage` (30) gelöscht. Übersicht unter `/verwaltung/system/jobs`.
- `Jobs:Aktiv=false` schaltet den Scheduler ab (z. B. in Web-Tests).
- Hinweis: Quartz 4.x ist erst seit September 2026 verfügbar und hat eine stark geänderte API. Bewusst wird die ausgereifte 3.x-Linie genutzt.

## Tests

| Projekt | Inhalt |
|---|---|
| `MietHahn.Domain.Tests` | Reine Unit-Tests der Fachlogik (Rechenkerne), ohne I/O |
| `MietHahn.Integration.Tests` | Gegen echtes PostgreSQL (Testcontainers, `postgres:18-alpine`). `PostgresFixture` startet einen Container je Testlauf, **jeder Test bekommt eine eigene Datenbank**. `InfrastrukturUmgebung` liefert die komplett registrierte Infrastruktur (migrierte DB, temporäre Dateiablage, `FakeTimeProvider`). Mailtests laufen gegen einen echten SMTP-Server (`MailpitFixture`) |
| `MietHahn.Web.Tests` | Komponenten mit bUnit (`BunitContext`). Ende-zu-Ende über HTTP mit `MietHahnWebFactory` (echte App, eigene DB je Test) und `TestBrowser`: lädt Seiten, folgt Weiterleitungen und schickt Blazor-Formulare inklusive Antiforgery-Token ab (Formular über den `FormName`). TOTP-Codes liefert `TestHilfen.Totp` |
| `MietHahn.Web.Tests/Browser` | Echte Browser-Tests mit Playwright (Chromium) gegen die per Kestrel gestartete App: 360-px-Breite ohne horizontales Scrollen, Navigation, Formulare mit `blazor.web.js`. Ohne installierten Browser werden sie übersprungen (Trait `Kategorie=Browser`). Browser installieren: `pwsh tests/MietHahn.Web.Tests/bin/Debug/net10.0/playwright.ps1 install chromium` |
| `MietHahn.TestHilfen` | Gemeinsame Fixtures (PostgreSQL, Mailpit, Gotenberg) und Hilfen; selbst kein Testprojekt |
| `MietHahn.Architecture.Tests` | Schichtregeln (NetArchTest) |

Testdatenbanken: Der PostgreSQL-Container läuft mit `max_connections=300`, die Verbindungszeichenfolgen schließen ungenutzte Pool-Verbindungen nach 2 s (sonst „too many clients“ bei vielen parallelen Tests mit je eigener Datenbank).

Konventionen: Testnamen auf Deutsch mit Unterstrichen (`Loeschen_ist_logisch_…`), Zeit über `FakeTimeProvider`, kein `DateTime.Now` im Produktivcode (immer `TimeProvider`).

Testergebnisse als TRX und Abdeckung als Cobertura (wie in der CI):

```bash
dotnet test -- --report-trx --results-directory testergebnisse --coverage --coverage-output-format cobertura
```

## CI/CD (GitHub Actions)

| Workflow | Auslöser | Inhalt |
|---|---|---|
| `.github/workflows/ci.yml` | Push auf den Default-Branch (`ccr-*`, vorsorglich auch `main`), Pull Requests, manuell | Restore, `dotnet format --verify-no-changes`, Release-Build (Warnungen sind Fehler), Playwright-Chromium installieren, alle Tests inkl. Testcontainers und Browser-Tests. TRX und Cobertura als Artefakt `testergebnisse` (14 Tage). Zweiter Job baut App- und Backup-Image (Layer-Cache über `type=gha`) und führt `deploy/backup/restore-test.sh` aus |
| `.github/workflows/release.yml` | Tag `v*.*.*` | Multi-Arch-Images (`linux/amd64`, `linux/arm64`) `ghcr.io/ther00st3r/miethahn` und `…/miethahn-backup` mit Tags `1.2.3`, `1.2`, `sha-…` und `latest` |
| `.github/dependabot.yml` | wöchentlich | NuGet (gruppiert: .NET-Plattform, Testpakete; Quartz-Major-Updates ausgeschlossen, siehe Architektur), Docker-Basisimages, Compose-Images, Actions |

Vor dem Push lokal dasselbe prüfen: `dotnet format --verify-no-changes && dotnet build -c Release && dotnet test`. Workflows lassen sich mit [actionlint](https://github.com/rhysd/actionlint) prüfen. Release-Ablauf und Branch-Schutz stehen in [CONTRIBUTING.md](../CONTRIBUTING.md).
