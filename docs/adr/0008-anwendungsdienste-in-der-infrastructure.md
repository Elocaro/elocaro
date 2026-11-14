# ADR 0008: Anwendungsdienste liegen in der Infrastructure, Application ist die Vertragsschicht

- Status: angenommen (hält den gewachsenen Stand fest)
- Datum: 2026-11-14

## Kontext

Die erste Fassung von `docs/architektur.md` beschrieb `Elocaro.Application` als Schicht der Anwendungsfälle (Commands und Queries) und
`Elocaro.Infrastructure` als Technik dahinter. Gebaut wurde es anders: Die Application enthält Schnittstellen, Eingabemodelle und
Ergebnis-Records, die Klassen dahinter liegen in der Infrastructure und arbeiten direkt mit dem `ElocaroDbContext`. Ein Dienst wie
`LeaseService` prüft die Eingabe, lädt die Entität, ruft ihre Methoden auf, speichert und bildet auf die Records für die Anzeige ab.

Die Prüfung der Architektur im November 2026 (#184) hat das als Abweichung zwischen Doku und Code gefunden. Zu entscheiden war, ob der
Code der Doku folgen soll oder die Doku dem Code.

## Entscheidung

- **Die Anwendungsdienste bleiben in der Infrastructure.** Sie sind `internal` und nur über ihre Schnittstelle aus der Application
  erreichbar; registriert werden sie in `DependencyInjection.cs`.
- **Die Application ist die Vertragsschicht** zwischen Web und Infrastructure: Schnittstellen, Eingabemodelle mit Validierungsattributen,
  Ergebnis-Records, `Result`, Rechte und Richtlinien, Modulkatalog, Erweiterungspunkte. Sie enthält keine Implementierung und kennt
  kein EF.
- **Kein Repository** zwischen Dienst und `DbContext`. Abfragen stehen im Dienst, der sie braucht.
- **Fachregeln gehören in die Domain**, nicht in den Dienst: Zustandsänderungen sind Methoden der Entität, Rechnungen reine Funktionen.
  Der Dienst orchestriert.
- **Entitäten verlassen die Infrastructure nicht.** Das Web bekommt Records und sieht aus der Domain nur Enums und Value Objects
  (Ausnahme: `StoredFile`).

## Begründung

- Die Anwendung ist datenlastig: Fast jeder Anwendungsfall ist Laden, Regel prüfen, Speichern, Abbilden. Eine Schicht, die das von den
  Abfragen trennt, bestünde überwiegend aus Durchreichen.
- EF Core ist bereits die Abstraktion über die Datenbank. Ein Wechsel weg von PostgreSQL ist nicht geplant (ADR 0002); Zeitraum-Constraints
  und `xmin` binden ohnehin daran.
- Die Dienste werden gegen eine echte PostgreSQL-Datenbank geprüft (Testcontainers, eine Datenbank je Test). Das prüft Abfragen,
  Constraints und Nebenläufigkeit mit, die ein Test gegen Attrappen nicht sieht. Die Rechenkerne, bei denen es auf viele Fälle ankommt,
  liegen in der Domain und brauchen keine Datenbank.
- Das Web bleibt trotzdem von der Technik getrennt: Es sieht nur die Schnittstellen (Architekturtest `AccessRulesTests`).

## Alternativen

1. **Dienste in die Application verschieben, mit Repositories.** Der klassische Schnitt. Rund 30.000 Zeilen Umbau, jede Abfrage bekäme
   eine Schnittstelle; die Tests bräuchten weiterhin die Datenbank, weil die Fehler dort in den Abfragen liegen.
2. **Dienste in die Application verschieben, `DbContext` hinter einer Schnittstelle.** Weniger Umbau, aber die Application hinge dann an
   EF Core (`DbSet`, `Include`, `AsNoTracking`); gewonnen wäre nur der Ordnername.
3. **Application auflösen** und die Verträge in die Infrastructure legen. Dann sähe das Web alle Typen der Infrastructure; die Trennung
   hinge nur noch an `internal`.

## Folgen

- Anwendungslogik lässt sich nicht ohne Datenbank testen. Was viele Fälle braucht, gehört deshalb in die Domain; ein Dienst, der selbst
  rechnet oder verzweigt, ist ein Hinweis, dass etwas in die Entität gehört.
- Der Name „Infrastructure“ beschreibt nicht, was drin ist. `docs/architektur.md` sagt es ausdrücklich; umbenannt wird nicht.
- Modulgrenzen hängen nicht an Assemblies, sondern an den Regeln in `ModuleRulesTests`: Alle Dienste teilen sich einen `DbContext` und
  können jede Tabelle lesen. Querverbindungen laufen über Erweiterungspunkte oder eine schmale Schnittstelle des anderen Moduls.
- Die Dienste der Verwaltung prüfen keinen Zugriff. Jeder Einstieg (Seite, Endpunkt, MCP-Tool) legt ihn fest; `AccessRulesTests`,
  `EndpointRulesTests` und `McpRulesTests` erzwingen das.
