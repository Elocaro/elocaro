# Elocaro – Hinweise für Claude

Self-hosted Vermieterverwaltung: .NET 11, Blazor (Verwaltung Interactive Server mit MudBlazor, sonst Static SSR), PostgreSQL 18 (EF Core/Npgsql), Quartz, Gotenberg, MailKit.
Details: [docs/architektur.md](docs/architektur.md) (Schichten, Module, Regeln und ihre Tests), [docs/entwicklung.md](docs/entwicklung.md) (Konventionen, Glossar, Tests), [docs/ui.md](docs/ui.md) (UI-Leitfaden, verbindlich für jede Seite), [docs/betrieb.md](docs/betrieb.md), [CONTRIBUTING.md](CONTRIBUTING.md).

## Sprache

- **Englisch:** alle Bezeichner (Klassen, Methoden, Eigenschaften, Variablen, Enum-Werte), Datei- und Ordnernamen,
  Kommentare und XML-Doku, Testnamen, Datenbanknamen, Konfigurationsschlüssel, Umgebungsvariablen, CSS-Klassen,
  HTML-Ids, Formularnamen, Vorlagenvariablen, Job-Namen.
- **Deutsch:** Oberflächentexte und Meldungen für Benutzer, Mail-/PDF-Texte, URLs und Query-Parameter,
  Dokumentation (`docs/`, README), Commit-Nachrichten, Issue- und PR-Texte.
- Fachbegriffe nach dem Glossar in `docs/entwicklung.md#sprache-im-code` (z. B. Objekt → `Property`, Mietvertrag → `Lease`,
  Sollstellung → `Charge`, Beleg → `Receipt`).

## Arbeitsweise

- Default-Branch ist `main`; dort wird entwickelt und gepusht.
- Vor jedem Commit: `dotnet format --verify-no-changes && dotnet build && dotnet test` (Warnungen sind Fehler;
  Integrations- und Web-Tests brauchen Docker, Browser-Tests Playwright-Chromium).
- Commits mit `Closes #<Issue>` bzw. `Refs #<Issue>`; Funktionen immer mit Tests und Doku in `docs/` abschließen.
- Architektur: Anwendungsdienste liegen `internal` in `Infrastructure` hinter Schnittstellen aus `Application`; Seiten, Endpunkte und
  MCP-Tools prüfen den Zugriff selbst (die Dienste tun es nicht). Keine neuen Modulzyklen; Querverbindungen über Erweiterungspunkte.
  `dotnet test --project tests/Elocaro.Architecture.Tests` prüft das in Sekunden ohne Docker.
- Modelländerungen brauchen eine Migration (`dotnet ef migrations add <Name> -p src/Elocaro.Infrastructure
  -s src/Elocaro.Infrastructure -o Persistence/Migrations`), sonst schlägt `Model_and_migrations_are_in_sync` fehl.
