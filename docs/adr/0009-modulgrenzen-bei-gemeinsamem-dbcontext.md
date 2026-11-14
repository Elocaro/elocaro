# ADR 0009: Modulgrenzen bei gemeinsamem DbContext bleiben bei den Zyklen-Regeln

- Status: angenommen
- Datum: 2026-11-14
- Entscheidet die Frage aus #191 (Folge von [ADR 0008](0008-anwendungsdienste-in-der-infrastructure.md)).

## Kontext

Alle Module teilen sich den `ElocaroDbContext`. Ein Dienst kann jede Tabelle abfragen, auch die eines anderen Moduls (`db.Set<FremdeEntität>()`).
Die Modulgrenzen hält deshalb nur `ModuleRulesTests`: keine neuen Zyklen, Fundamente kennen keine Fachmodule, Top-Level-Module werden von
keinem anderen benutzt. Zu entscheiden war, ob zusätzlich der direkte Zugriff auf Entitäten fremder Module verboten wird.

## Befund

Eine Zählung der `Set<…>`-Zugriffe in der Infrastructure auf Entitäten eines anderen Moduls (Stand 14.11.2026):

- **423 Zugriffe** in **101 Modulpaaren**. Die häufigsten sind Lesezugriffe auf Stammdaten: Portal → People (16), Finance → Leases (14),
  Leases → People (13), Bank → Properties (13), Properties → Admin (11), Handover → Leases (11), Tax → Properties (11).
- Objekt, Einheit, Person und Mietvertrag sind der gemeinsame Kern. Fast jedes Modul liest sie, meist für Namen, Zuordnungen oder Filter.
- Die meisten Zugriffe sind Abfragen (`AsNoTracking`, Projektion, `Where`), kein Schreiben fremder Entitäten.

## Entscheidung

- **Der direkte Lesezugriff auf Entitäten fremder Module bleibt erlaubt.** Es gibt keine Regel, die `db.Set<FremdeEntität>()` verbietet.
- **Die Zyklen-Regeln bleiben die Grenze.** Sie verhindern, was die Änderbarkeit tatsächlich kostet: gegenseitige Abhängigkeit, in die man
  einzelne Module nicht mehr herauslösen kann. Die Listen der bekannten Zyklen bleiben eingefroren und schrumpfen.
- **Fremde Entitäten werden nicht verändert.** Ein Modul schreibt nur in seine eigenen Tabellen; was ein anderes Modul ändern soll,
  läuft über dessen Schnittstelle (`I…Service`) oder einen Erweiterungspunkt. Das ist Konvention (`docs/architektur.md`) und Gegenstand
  der Review. Ein Test dafür ist erst sinnvoll, wenn sich Verstöße zeigen.

## Begründung

- Ein Verbot verlangte für 423 Zugriffe in 101 Paaren eine Schnittstelle. Für Objekt, Person und Vertrag wären das Dutzende
  Lesemethoden, die nur durchreichen, was die Abfrage ohnehin liefert. Der Gewinn bliebe aus: Die Schnittstelle änderte nichts daran,
  dass die Module sich kennen.
- Ein Verbot widerspräche ADR 0008 (kein Repository, Abfragen stehen im Dienst, der sie braucht) und träfe dieselbe Stelle noch einmal.
- Verknüpfungen über Fremdschlüssel und Joins auf Stammdaten sind gewollt: Eine Abfrage über Modulgrenzen ist eine einzige
  SQL-Anweisung, über Schnittstellen würde sie zu mehreren Aufrufen mit N+1-Gefahr.
- Eine Aufteilung in mehrere Datenbanken oder Dienste ist nicht geplant. Der Preis der gemeinsamen Datenbank wird nicht bezahlt, solange
  niemand sie trennen will.

## Alternativen

1. **Verbot mit eingefrorener Liste.** Wie bei den Zyklen: Die heutigen Querabfragen werden gezählt und dürfen nur abnehmen. Verworfen, weil
   die Liste 423 Einträge hätte und kein Weg in Sicht ist, sie ohne Gegenwert zu leeren.
2. **Verbot nur für Schreibzugriffe.** Ein enger Test (`Add`, `Remove`, `ExecuteUpdate` auf Entitäten fremder Module). Verschoben:
   sinnvoll, sobald ein Verstoß auffällt. Bis dahin genügt die Review.
3. **Eigener `DbContext` je Modul.** Die saubere Lösung für getrennte Module, aber ein Umbau in der Größe von Alternative 1 aus ADR 0008,
   dazu Migrationen je Kontext. Kein Bedarf.

## Folgen

- `ModuleRulesTests` bleibt die einzige automatische Modulgrenze; `docs/architektur.md` verweist auf diese Entscheidung.
- Wer ein Modul herauslösen will, findet die Abhängigkeiten über die Zyklenlisten und mit der Suche nach `Set<…>` der Fremdentitäten;
  das ist dann ein eigenes Vorhaben mit eigener Entscheidung.
