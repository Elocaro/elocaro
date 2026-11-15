# Architektur

Dieses Dokument beschreibt, wie der Code heute aufgebaut ist, welche Regeln gelten und wo der Stand von den Regeln abweicht.
Regeln, die ein Test prüft, nennen den Test. Wo der Stand vom Ziel abweicht, steht es unter
[Bekannte Abweichungen und offene Entscheidungen](#bekannte-abweichungen-und-offene-entscheidungen).

## Leitentscheidungen

| Thema | Entscheidung | Begründung |
|---|---|---|
| Laufzeit | **.NET 11** (`net11.0`, SDK aus `global.json`), C# | umgestellt von .NET 10 mit #163; Pakete noch im Stand RC.3 (siehe [offene Entscheidungen](#bekannte-abweichungen-und-offene-entscheidungen)) |
| UI | **Blazor Web App**, kein React, kein WASM. Verwaltung: **Interactive Server** mit MudBlazor je Seite ([ADR 0005](adr/0005-interaktive-seiten-mit-mudblazor.md)). **Static SSR** für Anmeldung, Konto, Portale, Service und reine Anzeigeseiten ([ADR 0001](adr/0001-blazor-static-ssr.md)) | einfaches Hosting, wenig eigenes JS, gut für mobile Erfassung; interaktiv, wo Eingaben oder Live-Verhalten es brauchen |
| Datenbank | **PostgreSQL** via EF Core 11 + **Npgsql** (siehe [ADR 0002](adr/0002-ef-core-provider.md)) | Zeitraum-Constraints, transaktionale Migrations |
| Architekturstil | **Modularer Monolith**, ein Deployable (plus Backup-Dienst im eigenen Container) | Kleine Nutzerzahl, einfacher Betrieb |
| Auth | ASP.NET Core Identity + TOTP-2FA, Magic Link für Mieter/Handwerker, Zugriffstoken für MCP | Vorgabe |
| PDF | **Scriban**-HTML-Vorlagen → **Gotenberg** (Docker, Chromium) | Vorlagen ohne Code editierbar, ein Mechanismus für alle Dokumente |
| Jobs | **Quartz.NET 4.x** (persistenter Store in PostgreSQL, Laufprotokoll) | Fristen, Sollstellungen, FinTS-Abruf, Mails |
| Banking | Parser für CSV/CAMT.053/MT940; FinTS via **libfintx** (LGPL-3.0, als NuGet unverändert eingebunden) | Vorgabe: Import und Direktabruf |
| OCR/KI | Interface `IReceiptExtractor` mit Providern **Claude API**, **Ollama** (lokal), **Keiner** | Konfigurierbar, Datenhoheit wählbar |
| Mail | **MailKit** (SMTP) mit Outbox-Tabelle und Wiederholungen | Standard, Versandnachweis |
| Bilder | **SkiaSharp** (EXIF-Drehung, Verkleinern, Metadaten entfernen) | MIT-Lizenz |
| Dateien | Lokales Volume, Content-Addressed (SHA-256) | Einfach, backupbar |
| Schnittstelle für KI-Assistenten | **MCP** ([ADR 0006](adr/0006-mcp-schnittstelle.md), [ADR 0007](adr/0007-mcp-schreibzugriff-und-rueckgaengig.md)) | zweiter Weg zu denselben Diensten, mit Rückgängig |
| Hosting | **Docker Compose** auf VPS: `app`, `postgres`, `gotenberg`, `backup` (restic), `caddy` (HTTPS/Let's Encrypt) | Vorgabe |

## Lösungsstruktur

```
src/
  Elocaro.Domain/          Entitäten, Value Objects (Zeitraum, IBAN), Domänenregeln, reine Rechenkerne. Keine Projekt- und Framework-Abhängigkeit
  Elocaro.Application/     Verträge: Schnittstellen der Anwendungsdienste, Eingabemodelle, Ergebnis-Records, Rechte und Richtlinien, Modulkatalog
  Elocaro.Infrastructure/  Implementierung der Anwendungsdienste und alle Technik: EF Core, Migrations, Banking, FinTS, Belegauslesung, Gotenberg, MailKit, Quartz
  Elocaro.Web/             Blazor-Seiten (/verwaltung, /portal, /eigentuemer, /service), Minimal-API-Endpunkte, MCP-Tools, Zugriffskontrolle
  Elocaro.Backup/          Backup-Dienst (eigenes Image, eigene API mit Token); verweist auf kein anderes Projekt
tests/
  Elocaro.Domain.Tests/        Rechenkerne (NK, HK, CO₂, AfA, Index) mit Golden-Master-Fällen, ohne I/O
  Elocaro.Integration.Tests/   Anwendungsdienste gegen echtes PostgreSQL (Testcontainers), Gotenberg, Mailpit
  Elocaro.Web.Tests/           bUnit, HTTP-Tests über die echte App, Playwright
  Elocaro.Backup.Tests/        Backup-Dienst
  Elocaro.Architecture.Tests/  die Regeln dieses Dokuments
  Elocaro.TestHelpers/         gemeinsame Fixtures
deploy/
  docker-compose.yml, Caddyfile, backup/
```

Größenordnung (November 2026, ohne generierte Migrationen): Domain rund 15.000 Zeilen, Application 7.000, Infrastructure 31.000, Web 34.000;
105 Entitätstypen, 98 Dienst-Schnittstellen, 143 Seiten, 137 Migrationen.

### Was in welcher Schicht liegt

| Schicht | Enthält | Enthält nicht |
|---|---|---|
| **Domain** | Entitäten (Basis `Entity`), Zustandsänderungen als Methoden mit Regelprüfung (`RuleViolationException`), Value Objects, Rechenkerne als reine Funktionen mit Rechenprotokoll, Kataloge (Kostenarten, Rechtsstände) | EF, ASP.NET, I/O, Zeitquelle (das Datum kommt als Parameter) |
| **Application** | Schnittstellen (`ILeaseService` …), Eingabemodelle mit Validierungsattributen (`LeaseInput`), Ergebnis-Records für die Anzeige (`LeaseDetails`), `Result`, `Permissions`/`Policies`/`Roles`, `ModuleCatalog`, Erweiterungspunkte | Implementierungen, EF, Abfragen |
| **Infrastructure** | Die Klassen hinter den Schnittstellen (alle `internal`): Sie laden über den `ElocaroDbContext`, rufen die Domäne auf, speichern und bilden auf die Ergebnis-Records ab. Dazu Persistenz (Konfigurationen, Migrationen, Interceptor), Adapter (SMTP, Gotenberg, FinTS, KI) und Jobs | Zugriffsprüfung für die Verwaltung (siehe unten), Blazor |
| **Web** | Seiten und Komponenten, Endpunkte für Downloads und Uploads, MCP-Tools, Richtlinien und Handler, Anzeigehilfen (`Format`, `Labels`) | Datenbankzugriff, Fachregeln |

Die Anwendungsfälle liegen also **in der Infrastructure**, nicht in der Application: Ein Dienst wie `LeaseService` ist Use Case und Datenzugriff
in einem, ohne Repository dazwischen. Das hält den Weg von der Seite zur Datenbank kurz und ist der Grund, warum die Dienste mit
Integrationstests gegen PostgreSQL geprüft werden statt mit Unit-Tests gegen Attrappen. Die Application ist die Vertragsschicht, über die
das Web die Infrastructure sieht. Entscheidung und Alternativen: [ADR 0008](adr/0008-anwendungsdienste-in-der-infrastructure.md).

### Abhängigkeiten

```
Web ──► Application ──► Domain
 │           ▲
 └──► Infrastructure   (das Web kennt Infrastructure-Typen nur beim Start und für Konten)
```

## Weg einer Anfrage

1. **Einstieg** ist eine Seite, ein Endpunkt oder ein MCP-Tool. Dort wird der Zugriff geprüft: `[Authorize(Policy = …)]` an der Seite
   oder im Ordner, `RequireAuthorization(…)` am Endpunkt, Richtlinie an der Tool-Klasse.
2. Der Einstieg ruft eine **Application-Schnittstelle** mit einem Eingabemodell auf.
3. Der **Dienst** prüft die Eingabe (`InputRules.Check`), lädt die Entität, ruft ihre Methoden auf und speichert. Erwartete Fehler
   (Regelverletzung, Überlappung, Nebenläufigkeit, nicht gefunden) übersetzt `Command.ExecuteAsync` in ein `Result`; die Seite zeigt es an.
4. Beim **Speichern** setzt der `EntitySaveInterceptor` Audit-Felder, macht aus Löschen ein logisches Löschen, schreibt das
   Änderungsprotokoll und ordnet die Änderung einer Operation zu (`IOperationScope`, [ADR 0007](adr/0007-mcp-schreibzugriff-und-rueckgaengig.md)).

Regeln dazu:

- **Zugriff wird vor dem Dienst geprüft, nicht im Dienst.** Die Dienste der Verwaltung vertrauen ihrem Aufrufer. Jeder neue Weg zu einem
  Dienst (Seite, Endpunkt, Tool, Job) muss den Zugriff deshalb selbst festlegen; die Regeln unter *Zugriff* und *Endpunkte* erzwingen das.
  Ausnahme sind Dienste, deren Datenbereich vom Benutzer abhängt (`ITenantPortal`, `IOwnerPortal`, Tickets und Aushänge im
  Servicebereich, Benutzerverwaltung): Sie bekommen den `ClaimsPrincipal` und leiten daraus ab, was sichtbar ist; eine Id aus der
  Anfrage zählt dort nie allein.
- **Transaktionen:** Ein `SaveChangesAsync` ist eine Transaktion. Abläufe aus mehreren Speicherschritten oder mit Dateien (Mahnlauf,
  Versand, Import, Übergabe, Unterschrift) öffnen ausdrücklich eine.
- **Zeit** kommt aus `TimeProvider` (`BannedSymbols.txt` verbietet `DateTime.Now` und Verwandte); der Tag in deutscher Zeit aus `GermanTime`.
- **Lesen ohne Nachverfolgung:** Abfragen für die Anzeige nutzen `AsNoTracking()` und bilden auf Records ab. Entitäten verlassen die
  Infrastructure nicht (einzige Ausnahme: `StoredFile` der Dateiablage); aus der Domain sieht das Web Enums und Value Objects.

## Module

Ein Modul ist ein Ordner, der in Domain, Application und Infrastructure gleich heißt (`Leases`, `Bank`, `Metering` …). Module sind
Namensräume, keine Assemblies, und teilen sich einen `ElocaroDbContext`: Technisch kann jedes Modul jede Tabelle lesen, und das ist gewollt ([ADR 0009](adr/0009-modulgrenzen-bei-gemeinsamem-dbcontext.md)). Die Grenzen
halten deshalb nur die Regeln unten; verändert wird eine fremde Entität nur über die Schnittstelle ihres Moduls.

Nicht jeder Ordner hat in jeder Schicht ein Gegenstück: `Application/Todos` (Erweiterungspunkt des Dashboards) steht allein. Die Regeln werden deshalb **je Schicht** geprüft, nicht über die Schichten
hinweg.

| Art | Module | Regel |
|---|---|---|
| **Bausteine** | Common, Admin (Modulschalter, Gates), Accounts, Files, Communication, Templates, Notifications, Audit | verweisen auf kein Fachmodul. Keine eingefrorenen Ausnahmen mehr: Die Zuordnung der Mietmodelle zu Optionen liegt als `LeaseOptions` bei `Application/Leases` |
| **Fachmodule** | Properties, People, Leases, Finance, Bank, Receipts, Documents, Metering, Heating, OperatingCosts, Statements, Tax, Tickets, Notices, Deadlines, Portal, Privacy; dazu die technischen Dienste Operations, Pdf und Jobs | keine neuen wechselseitigen Abhängigkeiten |
| **Oberste Module** | Setup (Import, Beispieldaten, Einrichtung), Portfolio (Kennzahlen, Navigationskontext), Legal (Rechtsstände), Handover (Übergabeprotokoll), Correspondence (Serienbriefe, Dokumentbereitschaft), Emergency (Notfallmappe) | lesen aus vielen Modulen; kein anderes Modul verwendet sie |
| **Persistenz** | Persistence (DbContext, Konfigurationen, Migrationen) | gehört allen, zählt nicht als Modul |

Was wohin gehört, wenn es nicht offensichtlich ist:

- Das Absenderprofil (`SenderProfiles`, `LandlordProfileService`) liegt in `Properties`, weil dort die Verknüpfung Objekt → Profil gehört.
- Anrede und Adressblock (`LetterParts`) liegen in der Domain; `Common` enthält nur Querschnittshilfen ohne Fachbezug
  (auch CSV-Zerlegung und Zeichensatz-Erkennung, die nicht nur der Bankimport braucht).
- Die Kostengruppe (`CostGroup`, `CostGroups`) liegt bei den Kostenarten in `Domain.Receipts`, nicht bei den Umlageschlüsseln.
- Die Beschreibung eines Mietverhältnisses (`LeaseDescriptions`) gehört zu `Leases`, nicht zu `Finance`.
- Der Modulschalter kennt keine Fachmodule: Jedes meldet seine Nutzung über `IOptionUsageSource`.

### Erweiterungspunkte

Wo ein Baustein oder ein oberes Modul etwas von vielen Fachmodulen braucht, fragt es eine Liste von Schnittstellen ab, statt die Module
zu kennen. Jedes Modul liefert seine Implementierung und registriert sie in seiner Registrierungsdatei `<Modul>/<Modul>Registration.cs`.

| Schnittstelle | Wozu | Abnehmer |
|---|---|---|
| `ITodoSource` | Hinweise für „Was ist zu tun?“ | Dashboard |
| `IDeadlineSource` | Termine aus Verträgen, Zählern, Darlehen … | Fristenkalender |
| `IPersonReference` | In welcher Rolle eine Person vorkommt; verhindert Löschen | Personen |
| `IPersonalDataSource` | Daten zu einer Person für die DSGVO-Auskunft | Datenschutz |
| `IFileShare` | Gibt eine Datei für andere Rollen frei | Dateiablage |
| `IOptionUsageSource` | Zählt, ob eine abschaltbare Option noch verwendet wird | Modulschalter |
| `IMeterUsage` | Wer einen Zähler noch verwendet; verhindert das Entfernen | Zähler |
| `IUndoCompensation` | Gegenbuchung für das Anlegen eines Finanztyps (Sollstellung, Zahlung), statt zu löschen | Rückgängig (Aktivität) |

Neue Querverbindungen von unten nach oben gehen über einen solchen Punkt oder über eine schmale Schnittstelle des anderen Moduls
(Beispiel: `IDocumentLeaseSource`, über die die Dokumentenbibliothek Verträge kennt), nicht über `db.Set<FremdeEntität>()`.

### Bekannte Zyklen

Zwei Module, die sich gegenseitig verwenden, sind ein Zyklus. Die vorhandenen sind in `ModuleRulesTests` eingefroren: Ein neuer bricht
den Test, ein aufgelöster muss aus der Liste. Ziel ist eine leere Liste. Domain und Application sind frei von Zyklen.

Aufgelöst im November 2026: Metering ↔ OperatingCosts (Umrechnungsfaktor am Zähler, Erweiterungspunkt `IMeterUsage`) sowie in der
Domain Heating ↔ OperatingCosts und Heating ↔ Receipts (`HeatingCostKind` liegt bei den Kostengruppen in `Domain.Receipts`) sowie Leases ↔ Properties (der Belegungsplan liegt jetzt in `Leases`), Bank ↔ Tax (Schnittstelle `ILoanBookings` der Darlehen, umgesetzt in Bank) und Properties ↔ Receipts (die Regel für selbst bezahlte Kostenarten liegt als `SelfPaidCostTypes` in `Domain.Properties`).

Es gibt keine eingefrorenen Zyklen mehr; `KnownCycles` ist leer und bleibt es. People ↔ Leases und People ↔ Properties sind aufgelöst: `PersonRelation` trägt eine typisierte `PersonRelationKind` (Vertragspartei, Eigentümer), nach der `PersonService` die Rolle eines neuen Zugangs wählt, nicht nach dem Anzeigetext. Die Fälle hält `AccessRoleTests` fest.

Grenzen der Prüfung:

- **Nur Paare.** Ketten über mehrere Module (A → B → C → A) erkennt der Test nicht; heute hängen so Documents, Leases, People,
  Properties und Receipts zusammen.
- **Nur je Schicht.** Die Regeln gelten je Schicht. Schichtübergreifend bleiben Paare möglich (etwa Heating ↔ OperatingCosts über Domain und
  Infrastructure), solange keine Schicht für sich einen Zyklus bildet. Die zwei früher bekannten Paare sind aufgelöst: Datev liegt als eigenes
  Modul in Application und Infrastructure (der DATEV-Export gehört nicht mehr zu `Tax`), und `SharedFlatBilling` liegt in `Domain.Properties`
  statt bei den Betriebskosten.

## Querschnitt

- **Mandantenfähigkeit:** nicht nötig (eine Installation pro Vermieter). Alle Daten hängen aber an `PropertyId`; darauf bauen Eigentümer je Objekt und die Objektzuordnung der Hausmeister auf.
- **Historisierung:** Gültigkeitszeiträume statt Überschreiben (`Period`, `TimeSeries`), Audit-Log (wer, wann, was) für alle Änderungen, Rückgängig je Operation (bei Buchungen als Gegenbuchung).
- **Sicherheit:** HTTPS über Caddy, Antiforgery, Rate-Limiting für Login/Magic Link, Secrets über Umgebungsvariablen bzw. Docker Secrets, FinTS-PIN wird nie gespeichert, Upload-Validierung (Typ/Größe), CSP mit Nonce.
- **Backups:** nächtlich `pg_dump` + Datei-Volume, verschlüsselt (restic) im eigenen Container (`src/Elocaro.Backup`, ASP.NET-Core-API mit Token im internen Netz), Statusdatei → Dashboard/Warnmail, Restore-Test in der CI (`deploy/backup/`). Vollexport aller Daten als ZIP (JSON/CSV + Dateien) aus dem EF-Modell.
- **Lokalisierung:** nur Deutsch, Formate de-DE (`AppCulture`), Zeitzone Europe/Berlin.
- **Barrierearmut & Mobile:** responsive Layout, Erfassungsmasken (Zähler, Tickets) für Smartphones.
- **CI:** GitHub Actions: Build, Tests (inkl. Testcontainers und Browser), Format/Analyzer, Docker-Image nach GHCR.

Einzelheiten (Persistenz-Konventionen, Autorisierung, Tests, jedes Fachmodul) stehen in [entwicklung.md](entwicklung.md), die Oberfläche in [ui.md](ui.md).

## Architekturregeln (Tests)

`tests/Elocaro.Architecture.Tests` hält die Regeln dieses Dokuments fest; ein Verstoß bricht den Build. Die Tests lesen Assemblies und
Quelltext, brauchen weder Docker noch Datenbank und laufen in Sekunden.

| Regel | Test | Inhalt |
|---|---|---|
| Schichten | `LayerRulesTests` | Domain kennt nichts und nutzt keine Frameworks, Application nur Domain, Infrastructure nicht Web |
| Web und Infrastructure | `AccessRulesTests` | Seiten sprechen mit Application-Schnittstellen. Infrastructure-Typen kennt das Web nur in Start und Konto (feste Liste, darf schrumpfen, nicht wachsen). Jede Erwähnung zählt, auch ohne `using` |
| Module | `ModuleRulesTests` | Je Schicht (Domain, Application, Infrastructure): keine neuen Zyklen, Bausteine verweisen auf kein Fachmodul, oberste Module verwendet niemand. Bestehende Verstöße sind als Listen eingefroren, die nur schrumpfen dürfen. Gezählt wird jede Erwähnung eines Modul-Namensraums im Code: `using` (auch `static`, `global`, Alias) und ausgeschriebene Namen |
| Erweiterungspunkte | `ExtensionPointRulesTests` | Jede Implementierung eines Erweiterungspunkts ist registriert; eine neue als Liste abgefragte Schnittstelle muss in die Liste |
| Zugriff auf Seiten | `AccessRulesTests` | Jede Seite mit `@page` nennt ihren Zugriff ausdrücklich (`[Authorize(Policy = …)]` an der Seite oder in der `_Imports.razor` des Ordners, sonst `[AllowAnonymous]`). Anonym sind nur die aufgelisteten Seiten. Jeder Bereich verlangt seine Richtlinie (Verwaltung, Eigentümer, Portal, Service). Die Fallback-Richtlinie („angemeldet“) ist nur das Netz darunter |
| Zugriff auf Endpunkte | `EndpointRulesTests` | Jeder Minimal-API-Endpunkt nennt eine Richtlinie mit Namen oder steht in einer von zwei Listen: anonym (Links mit Token, Health, Wiederherstellung vor dem ersten Konto) oder „angemeldet genügt“ (Datei mit eigener Prüfung, Abmelden, Darstellung, Web-Push des eigenen Kontos). Ein `RequireAuthorization()` ohne Richtlinie zählt als „angemeldet“. Endpunkte unter `/verwaltung`, `/portal`, `/eigentuemer`, `/service` verlangen die Richtlinie ihres Bereichs |
| MCP | `McpRulesTests` | Jedes Tool hinter einer Richtlinie; lesende Tools rufen nichts Änderndes auf; schreibende nur Dienste mit wiederherstellbaren Typen; jeder Aufruf in einer Operation |
| Rückgängig | `UndoRulesTests` | Jeder Entitätstyp ist in `UndoPolicy` eingeordnet; Typen mit Geheimnissen oder Außenwirkung sind nicht wiederherstellbar |
| Änderungshistorie | `AuditRulesTests` | `<ChangeHistory>` fragt nach einem Entitätstyp, den es gibt (das Protokoll speichert den Klassennamen) |
| Oberfläche | `UiRulesTests`, `InteractiveUiRulesTests` | Bausteine statt Rohmarkup; interaktive Seiten in `InteractiveShell`, Rückmeldung über `Notifier`, Formulare mit `OnValidSubmit`, Anmeldung und Konto bleiben statisch, eigene Komponenten ohne MudBlazor-Typen in den Parametern. Siehe [ui.md](ui.md) |
| Größe der Seiten | `PageSizeRulesTests` | Ein `@code`-Block hat höchstens 200 Zeilen; was länger wird, geht in den Dienst, in eine Klasse neben der Seite oder in eine Komponente |
| Name | `NamingRulesTests` | Der frühere Produktname kommt nicht zurück |
| Modell und Migrationen | `Model_and_migrations_are_in_sync` (Integrationstests) | Jede Modelländerung hat eine Migration |

Dazu kommen die Analyzer im Build (`Directory.Build.props`): Warnungen sind Fehler, Meziantou.Analyzer, verbotene APIs
(`BannedSymbols.txt`), die Blazor-Analyzer (BL0012, BL0013, BL0016; JS-Interop läuft über `Components/Shell/JsInterop`), NuGet-Audit für alle Pakete.

Die Regeln auf Quelltext arbeiten mit Mustern, nicht mit dem Compiler. Sie sind so geschrieben, dass sie beim Umbau eher zu früh
anschlagen als etwas übersehen; wer ein Muster umgeht, hat die Regel nicht erfüllt.

## Bekannte Abweichungen und offene Entscheidungen

Stand der Prüfung vom 13. November 2026. Jeder Punkt ist eine Entscheidung, die noch aussteht, keine beschlossene Aufgabe.

1. ~~Application ist Vertragsschicht, die Anwendungsfälle liegen in der Infrastructure.~~ Entschieden: bleibt so,
   [ADR 0008](adr/0008-anwendungsdienste-in-der-infrastructure.md).
2. ~~Modulzyklen.~~ Erledigt: `KnownCycles` ist leer (#185).
3. ~~Ein DbContext für alle Module.~~ Entschieden: Lesen fremder Tabellen bleibt erlaubt (423 Zugriffe in 101 Modulpaaren), die
   Zyklen-Regeln bleiben die Grenze, fremde Entitäten werden nur über die Schnittstelle des Moduls verändert,
   [ADR 0009](adr/0009-modulgrenzen-bei-gemeinsamem-dbcontext.md).
4. ~~Regeln für interaktive Seiten fehlen.~~ Erledigt: `InteractiveUiRulesTests`, die Blazor-Analyzer sind wieder aktiv.
5. ~~Große Seiten.~~ Erledigt (#190): Die elf Seiten mit mehr als 200 Zeilen `@code` sind zerlegt (Rechenkerne und Texte in Klassen neben der Seite, Abschnitte in Komponenten); `PageSizeRulesTests` gilt ohne Ausnahmen.
6. ~~Eine Registrierungsdatei für alles.~~ Erledigt (#187): Jedes Modul registriert seine Dienste in `<Modul>/<Modul>Registration.cs` (`Add<Modul>Module`); `DependencyInjection.cs` ruft sie nur auf und hält Datenbank und Health-Checks. Die Reihenfolge der Aufrufe bestimmt die Reihenfolge der Erweiterungspunkt-Listen (z. B. Zählerverwendung). `ExtensionPointRulesTests` liest alle Registrierungsdateien.
7. **Laufzeit.** Die Umstellung auf .NET 11 ist erfolgt (#163). Stand 15. November 2026: Die EF-Core-Gruppe (EF Core, Identity-/DataProtection-Pakete,
   Npgsql-Provider) bleibt auf `11.0.0-rc.3`: Den Provider gibt es nur als `11.0.0-rc.3`, und `EFCore.NamingConventions` 10.0.1
   verlangt EF Core kleiner als 11.0.0. Die Gruppe wird gemeinsam angehoben, sobald beide freigegeben sind; alle übrigen
   Pakete stehen auf der aktuellen stabilen Version. Die frühere Begründung der Leitentscheidung („LTS“) gilt für .NET 11 nicht.
8. ~~Namen der Module über die Schichten.~~ Erledigt (#188): `Accounts` (vorher Infrastructure/Identity), `Communication` (vorher Infrastructure/Mail) und `Leases` (Deposits aus der Domain) heißen in allen Schichten gleich. Der dabei sichtbar gewordene Zyklus `Accounts` ↔ `Communication` ist aufgelöst: `Roles`, `Permissions`, `AccessRight` und `Policies` liegen jetzt in `Application/Common`.
