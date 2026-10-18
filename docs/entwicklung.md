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

In `Down()` steht dann `migrationBuilder.EntferneUeberlappungsschutz(name, tabelle)`. Beide Grenzen sind inklusive. Logisch gelöschte Zeilen zählen nicht. Bei einem Verstoß wirft PostgreSQL `23P01` (`PostgresErrorCodes.ExclusionViolation`), EF meldet das als `DbUpdateException`.

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

Datum: das eingebaute `InputDate<DateOnly>` (nativer Datepicker). Optionale E-Mail-Felder mit `[OptionaleEmail]` statt `[EmailAddress]` validieren, denn leere Formularfelder kommen als `""` an. Formularmodelle in `OnInitialized(Async)` **vor** dem ersten `await` setzen (`Modell ??= new()`), weil Blazor dort schon einmal rendert. Tabellen stehen immer in `<div class="tabellen-rahmen">`, damit sie mobil horizontal scrollen statt der Seite.

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
- **Ressourcenbasiert:** Für einzelne Datensätze `IAuthorizationService.AuthorizeAsync(user, ressource, anforderung)` mit eigenem `AuthorizationHandler<TAnforderung, TRessource>`. Beispiel: `DateiZugriffHandler` für `/dateien/{id}`. Die Verwaltung darf alles; Fachmodule geben einzelne Dateien für andere Rollen über `IDateiFreigabe` frei (z. B. Dokumente eines Mieters).
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
- Abruf nur über `OeffnenAsync(id)`. Der autorisierte Download-Endpoint kommt mit dem Rollenmodell (#7).

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

Konventionen: Testnamen auf Deutsch mit Unterstrichen (`Loeschen_ist_logisch_…`), Zeit über `FakeTimeProvider`, kein `DateTime.Now` im Produktivcode (immer `TimeProvider`).

Testergebnisse als TRX und Abdeckung als Cobertura (wie in der CI):

```bash
dotnet test -- --report-trx --results-directory testergebnisse --coverage --coverage-output-format cobertura
```

## CI/CD (GitHub Actions)

| Workflow | Auslöser | Inhalt |
|---|---|---|
| `.github/workflows/ci.yml` | Push auf `main`/`ccr-*`, Pull Requests, manuell | Restore, `dotnet format --verify-no-changes`, Release-Build (Warnungen sind Fehler), Playwright-Chromium installieren, alle Tests inkl. Testcontainers und Browser-Tests. TRX und Cobertura als Artefakt `testergebnisse` (14 Tage). Zweiter Job baut das Docker-Image ohne Push (Layer-Cache über `type=gha`) |
| `.github/workflows/release.yml` | Tag `v*.*.*` | Multi-Arch-Image (`linux/amd64`, `linux/arm64`) nach `ghcr.io/ther00st3r/miethahn` mit Tags `1.2.3`, `1.2`, `sha-…` und `latest` |
| `.github/dependabot.yml` | wöchentlich | NuGet (gruppiert: .NET-Plattform, Testpakete; Quartz-Major-Updates ausgeschlossen, siehe Architektur), Docker-Basisimages, Compose-Images, Actions |

Vor dem Push lokal dasselbe prüfen: `dotnet format --verify-no-changes && dotnet build -c Release && dotnet test`. Workflows lassen sich mit [actionlint](https://github.com/rhysd/actionlint) prüfen. Release-Ablauf und Branch-Schutz stehen in [CONTRIBUTING.md](../CONTRIBUTING.md).
