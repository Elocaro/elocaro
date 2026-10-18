# Entwicklung

## Voraussetzungen

- .NET SDK 10.0.1xx (`global.json`)
- Docker (für die lokale Datenbank und die Integrationstests via Testcontainers)

## Lokale Datenbank

```bash
docker run -d --name miethahn-dev-db \
  -e POSTGRES_USER=miethahn -e POSTGRES_PASSWORD=miethahn -e POSTGRES_DB=miethahn \
  -p 5432:5432 postgres:18-alpine
```

Die Verbindung für die Entwicklung steht in `src/MietHahn.Web/appsettings.Development.json`. In Produktion kommt sie **nur** aus der Umgebung: `ConnectionStrings__MietHahn=...`.

Beim Start wendet die App ausstehende Migrationen automatisch an. Abschalten mit `Datenbank__AutomatischMigrieren=false`. Beim allerersten Start auf einer leeren DB protokolliert EF/Npgsql einmal einen fehlgeschlagenen `SELECT` auf die History-Tabelle `__ef_migrationen`. Das ist erwartet.

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

## Tests

| Projekt | Inhalt |
|---|---|
| `MietHahn.Domain.Tests` | Reine Unit-Tests der Fachlogik (Rechenkerne), ohne I/O |
| `MietHahn.Integration.Tests` | Gegen echtes PostgreSQL (Testcontainers, `postgres:18-alpine`). `PostgresFixture` startet einen Container je Testlauf, **jeder Test bekommt eine eigene Datenbank** |
| `MietHahn.Web.Tests` | `WebApplicationFactory` (`MietHahnWebFactory`), später bUnit/Playwright |
| `MietHahn.Architecture.Tests` | Schichtregeln (NetArchTest) |

Konventionen: Testnamen auf Deutsch mit Unterstrichen (`Loeschen_ist_logisch_…`), Zeit über `FakeTimeProvider`, kein `DateTime.Now` im Produktivcode (immer `TimeProvider`).
