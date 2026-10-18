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

`ScribanRenderer` wird von Mails und (ab #34) PDFs genutzt:
- Jede Ausgabe wird **HTML-escaped**. Bewusst unescaped ist nur `RohesHtml`.
- Modell-Eigenschaften stehen in snake_case bereit (`MieterName` → `mieter_name`). Unbekannte Variablen sind ein Fehler.
- Hilfsfunktionen: `geld` (1.234,50 €), `datum` (TT.MM.JJJJ, Zeitpunkte in deutscher Zeit), `zahl wert stellen`, `text_zu_html` (Zeilenumbrüche).

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
| `MietHahn.Web.Tests` | `WebApplicationFactory` (`MietHahnWebFactory`), später bUnit/Playwright |
| `MietHahn.Architecture.Tests` | Schichtregeln (NetArchTest) |

Konventionen: Testnamen auf Deutsch mit Unterstrichen (`Loeschen_ist_logisch_…`), Zeit über `FakeTimeProvider`, kein `DateTime.Now` im Produktivcode (immer `TimeProvider`).
