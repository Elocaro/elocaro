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
- Commit-Betreff = Eintrag in den Release-Notes (es gibt kein Changelog): `<Bereich>: <verständlicher deutscher Satz> (Refs #<Issue>)` bzw. `(Closes #<Issue>)`, 15–130 Zeichen, Großbuchstabe, kein Punkt; Details in den Text darunter. Nur Änderungen mit direkter Auswirkung für Anwender gehören in die Release-Notes; alles andere (Refactoring, Tests, CI, Werkzeuge) bekommt den Bereich `Intern`, `Werkzeuge`, `Architektur`, `Tests` oder `CI` und wird herausgefiltert. Prüft `.githooks/commit-msg` (einmalig `git config core.hooksPath .githooks`) und die CI. Funktionen immer mit Tests und Doku in `docs/` abschließen.
- Architektur: Anwendungsdienste liegen `internal` in `Infrastructure` hinter Schnittstellen aus `Application`; Seiten, Endpunkte und
  MCP-Tools prüfen den Zugriff selbst (die Dienste tun es nicht). Keine neuen Modulzyklen; Querverbindungen über Erweiterungspunkte.
  `dotnet test --project tests/Elocaro.Architecture.Tests` prüft das in Sekunden ohne Docker.
- Modelländerungen brauchen eine Migration (`dotnet ef migrations add <Name> -p src/Elocaro.Infrastructure
  -s src/Elocaro.Infrastructure -o Persistence/Migrations`), sonst schlägt `Model_and_migrations_are_in_sync` fehl.

## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

Rules:
- When graphify-out/graph.json exists, run `graphify query "<question>"` before your first grep, search or source-file read for a codebase question, and put this rule in every subagent prompt that explores code. Read raw files only for lines the graph or an earlier read already located. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).
- A committed graphify-out/graph.json is rewritten whole on every build, so a large diff to it in a pull request is expected. Do not flag it as churn or suggest skipping the graph rebuild or its hooks.
