# ADR 0007: MCP mit Schreibzugriff, Aktivitätsprotokoll und Rückgängig

- Status: angenommen (Schritte 1 bis 3 umgesetzt)
- Datum: 2026-11-13
- Ersetzt den Punkt „Nur lesend“ aus [ADR 0006](0006-mcp-schnittstelle.md); alles andere aus 0006 bleibt.

## Kontext

Ein KI-Assistent soll Elocaro nicht nur abfragen, sondern die Funktionen der Oberfläche nutzen: Objekte, Personen, Verträge und Zähler anlegen und ändern, Daten übernehmen. Das gilt auch für unstrukturierte Quellen (Excel-Listen), nicht nur für einen Importweg.
ADR 0006 hat Schreiben ausgeschlossen, aus zwei Gründen: Die Dienste prüfen Rechte nicht selbst, und es gibt keine Bestätigung je Aktion.
Der Wunsch der Produktverantwortung: **alles, was darüber läuft, wird protokolliert und kann, wo möglich, rückgängig gemacht werden.** Das soll für alle Aktionen gelten, nicht nur für MCP.

Der Haken: Mit direktem Schreiben ist „Rückgängig“ die einzige Verteidigung gegen Prompt-Injection. Mieter schreiben Texte (Tickets, Portal-Nachrichten, Namen), die der Assistent liest und dann als Anweisung missdeuten kann.

## Befund zum Ist-Stand

- Das Audit-Protokoll (`AuditEntry`, erzeugt in `EntitySaveInterceptor` über `AuditCapture`) ist **Anzeige**, keine Wiederherstellungsquelle: Werte stehen als Text, `[Confidential]`-Felder als `***`, Schattenfelder (oft Fremdschlüssel) fehlen, nur `Entity`-Typen im Zustand Added/Modified werden erfasst, `[NoAudit]`-Typen nicht, und `AuditCleanup` löscht Einträge nach der Aufbewahrungsfrist.
- Es gibt keine Verbindung zwischen Einträgen einer Aktion und keine Angabe, über welchen Weg (Oberfläche, MCP, Job, Import) geändert wurde.
- Löschen ist weich (`Entity.Deleted`); das macht Wiederherstellen von Stammdaten machbar.
- Etwa 100 Stellen nutzen `Remove`/`ExecuteUpdate`/`ExecuteDelete`/`FromSql` außerhalb der Migrationen; viele sind Listenoperationen, aber ein Teil umgeht die Erfassung. Das ist je Typ zu prüfen.
- Fachregeln stehen teils in Seiten (z. B. Prüfung in `PropertyMeters.razor`, bevor `IMeterService.CreateAsync` läuft). Ein Werkzeug, das den Dienst direkt ruft, übersieht sie.

## Entscheidung

1. **Schreibende Werkzeuge sind erlaubt**, unter diesen Bedingungen:
   - eigene **Schreib-Bereiche** am Token (z. B. `write:masterdata`), getrennt von `read:*`, standardmäßig nicht gesetzt; Policy je Werkzeug wie bisher; `ReadOnly`/`Destructive` am Werkzeug stimmen;
   - **Fachregeln gehören in die Dienste**, nicht in Seiten. Bevor ein Typ ein Werkzeug bekommt, wird die Prüfung aus der Seite in den Dienst verschoben; Seite und Werkzeug rufen dann dasselbe.
   - Jeder Werkzeugaufruf läuft in einem **Operationsbereich** (siehe 2) und trägt dessen Id im Audit-Eintrag des Aufrufs.
2. **Operation statt nur Audit-Zeile.** Neue Tabelle `operations` (Id, Zeitpunkt, Konto, Kanal `Ui|Mcp|Job|Import`, Bezeichnung z. B. Werkzeugname, Zustand, Verweis auf eine rückgängig machende Operation). `AuditEntry` bekommt `OperationId`. Standard ist **eine Operation je Speichern** (im `EntitySaveInterceptor`); ein ausdrücklicher Bereich (`IOperationScope`) fasst mehrere Speichervorgänge zusammen (ein MCP-Aufruf, ein Import). Kein Bereich über einen ganzen Blazor-Circuit.
3. **Wiederherstellung braucht ein typisiertes Vorher-Bild**, nicht das Anzeigeprotokoll. Je Operation wird für die erlaubten Typen der Zustand vor der Änderung als JSON mit den echten Feldtypen gespeichert (auch Fremdschlüssel, keine Maskierung für Felder, die technisch wiederhergestellt werden müssen; Zugriff darauf nur für den Rückgängig-Dienst). Die Aufbewahrung folgt der Operation, nicht dem Anzeigeprotokoll.
4. **Rückgängig ist selbst eine protokollierte Operation** und **bricht bei Konflikt ab**: Weicht der aktuelle Stand (`Entity.Version`) von dem ab, was die Operation geschrieben hat, wird nichts geändert und der Grund genannt.
5. **Jede Aktion wird einer von drei Klassen zugeordnet**, per Architekturtest erzwungen (jeder Entity-Typ steht in genau einer Klasse):
   - **Wiederherstellbar:** Stammdaten. Anlegen → weich löschen, Löschen → wiederherstellen, Ändern → Felder zurücksetzen.
   - **Ausgleichend:** Finanzbuchungen. Rückgängig heißt Gegenbuchung (Muster wie in der Bankzuordnung), nie Löschen.
   - **Nicht rückgängig zu machen:** versendete Mails, ausgelieferte PDFs, Bank/FinTS, Signaturen, Benutzer- und Token-Verwaltung.
6. **Die dritte Klasse ist in Version 1 für MCP gesperrt.** Werkzeuge gibt es zuerst nur für wiederherstellbare Typen (Objekt, Einheit, Person, Mietvertrag, Zähler). Finanzbuchungen folgen, wenn der Ausgleich getestet ist. Die Oberfläche bleibt unverändert nutzbar.
7. **Aktivitätsansicht:** Eine Seite listet Operationen mit Kanal, Konto, Betroffenem und, wo möglich, „Rückgängig“. Sie ersetzt für Anwender die Einzel-Historie nicht, ergänzt sie.
8. **Rechte:** Wie in 0006 wirkt der Principal mit den Rechten des Kontos ∩ Bereiche des Tokens. Für Schreibzugriff bleibt es bei Administrator/Verwalter mit Stammdatenrecht; objektbezogene Einschränkungen (`PropertyAssignments`) gelten in Version 1 nur für Hausmeister, die kein MCP nutzen dürfen. Gelten sie später für Tokens, prüfen die Dienste sie selbst.
9. **`McpRulesTests`** ändern sich von „keine schreibenden Aufrufe“ zu: schreibende Werkzeuge brauchen Policy, Schreib-Bereich, `ReadOnly = false`, `Destructive` wahrheitsgemäß, einen Operationsbereich und nur Dienste aus der erlaubten Liste.

## Umsetzung in Schritten

1. Dieses ADR, Issue, Hinweis an die Produktverantwortung (nur Entwurf, nichts Funktionales).
2. Operation mit Kanal, `AuditEntry.OperationId`, Aktivitätsansicht. Deckt „alle Aktionen“ ab, weil die Erfassung beim Speichern sitzt.
3. Rückgängig für eine kleine Liste von Stammdatentypen, mit Konfliktprüfung und Architekturtest zur Klassifizierung.
4. Schreibende MCP-Werkzeuge nur für die Typen aus Schritt 3. Erst danach, nie davor.
5. Importe (Excel, Listen) als Operation mit Batch: Der KI-gestützte Import nutzt Schritt 4 und 3, statt einer eigenen Entwurfstabelle. Eine Vorschau vor dem Schreiben bleibt in der Oberfläche; über MCP ist die Wirkung durch Rückgängig begrenzt.

## Alternativen

- **Entwurfstabelle mit Bestätigung** (frühere Planung für #180): sicherer gegen Prompt-Injection, aber nur für Importe, nicht für „Objekt anlegen, Vertrag ändern“. Bleibt als optionale Vorschau für große Importe.
- **Audit-Protokoll als Quelle für Rückgängig:** verworfen, siehe Befund (Maskierung, Text statt Typen, fehlende Fälle, Löschfristen).
- **Bestätigung je Aktion im Assistenten:** liegt beim Client, nicht in Elocaro; Werkzeuge tragen `Destructive`, damit Clients fragen können, mehr ist nicht durchsetzbar.
- **Ein Bereich je Blazor-Circuit:** verworfen, die Operation würde Stunden umfassen.

## Folgen

- Migration für `operations` und die Spalte an `audit_entries`; Konfiguration und Token-Bereiche erweitert.
- Aufwand wächst: jeder neue Typ muss klassifiziert, getestet und dokumentiert werden.
- Offen: Aufbewahrungsdauer der Vorher-Bilder; Umgang mit Typen, die über `ExecuteUpdate` ohne Erfassung schreiben (müssen vor Aufnahme in die Liste auf die Erfassung umgestellt werden).
