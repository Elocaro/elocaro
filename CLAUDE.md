# Elocaro – Hinweise für Claude

Self-hosted Vermieterverwaltung: .NET 10, Blazor Static SSR, PostgreSQL 18 (EF Core/Npgsql), Quartz, Gotenberg, MailKit.
Details: [docs/entwicklung.md](docs/entwicklung.md) (Konventionen, Glossar, Tests), [docs/ui.md](docs/ui.md) (UI-Leitfaden, verbindlich für jede Seite), [docs/betrieb.md](docs/betrieb.md), [CONTRIBUTING.md](CONTRIBUTING.md).

## Sprache

- **Englisch:** alle Bezeichner (Klassen, Methoden, Eigenschaften, Variablen, Enum-Werte), Datei- und Ordnernamen,
  Kommentare und XML-Doku, Testnamen, Datenbanknamen, Konfigurationsschlüssel, Umgebungsvariablen, CSS-Klassen,
  HTML-Ids, Formularnamen, Vorlagenvariablen, Job-Namen.
- **Deutsch:** Oberflächentexte und Meldungen für Benutzer, Mail-/PDF-Texte, URLs und Query-Parameter,
  Dokumentation (`docs/`, README), Commit-Nachrichten, Issue- und PR-Texte.
- Fachbegriffe nach dem Glossar in `docs/entwicklung.md#sprache-im-code` (z. B. Objekt → `Property`, Mietvertrag → `Lease`,
  Sollstellung → `Charge`, Beleg → `Receipt`).

## Arbeitsweise

- Default-Branch ist `ccr-9b8ed253-urio73` (der Name lässt sich nicht ändern); dort wird entwickelt und gepusht.
- Vor jedem Commit: `dotnet format --verify-no-changes && dotnet build && dotnet test` (Warnungen sind Fehler;
  Integrations- und Web-Tests brauchen Docker, Browser-Tests Playwright-Chromium).
- Commits mit `Closes #<Issue>` bzw. `Refs #<Issue>`; Funktionen immer mit Tests und Doku in `docs/` abschließen.
- Modelländerungen brauchen eine Migration (`dotnet ef migrations add <Name> -p src/Elocaro.Infrastructure
  -s src/Elocaro.Infrastructure -o Persistence/Migrations`), sonst schlägt `Model_and_migrations_are_in_sync` fehl.
