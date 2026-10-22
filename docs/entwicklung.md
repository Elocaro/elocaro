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

Beim Start wendet die App ausstehende Migrationen automatisch an. Abschalten mit `Database__AutoMigrate=false`. Beim allerersten Start auf einer leeren DB protokolliert EF/Npgsql einmal einen fehlgeschlagenen `SELECT` auf die History-Tabelle `__ef_migrations`. Das ist erwartet.

Die Dateiablage liegt in der Entwicklung unter `src/MietHahn.Web/data/files` (per `.gitignore` ausgeschlossen).

## Sprache im Code

| Englisch | Deutsch |
|---|---|
| Bezeichner (Klassen, Methoden, Eigenschaften, Variablen, Enum-Werte), Datei- und Ordnernamen | Oberflächentexte, Fehlermeldungen für Benutzer, Mail- und PDF-Texte |
| Kommentare und XML-Doku im Code, Testnamen | Dokumentation (`docs/`, README), Commit-Nachrichten, Issues |
| Datenbank (Tabellen, Spalten, Indizes), Konfigurationsschlüssel, Umgebungsvariablen | URLs und Routen (`/verwaltung/objekte/…`), Query-Parameter (`?seite=2`) |
| CSS-Klassen, HTML-Ids, Formularnamen, Vorlagenvariablen, Job-Namen | |

Fachbegriffe folgen einem festen Glossar, damit derselbe Begriff überall gleich heißt:

| Deutsch | Code |
|---|---|
| Objekt / Einheit / Zimmer / Mietobjekt | `Property` / `Unit` / `Room` / `RentalUnit` |
| Mietvertrag / Mieter / Bürge / Vertragspartei | `Lease` / `Tenant` / `Guarantor` / `LeaseParty` |
| Mietbestandteil / Kaltmiete / Staffel / Indexmiete | `RentComponent` / `BaseRent` / `RentStep` / `IndexedRent` |
| Sollstellung / Zahlung / Verrechnung / Mieterkonto | `Charge` / `Payment` / `Allocation` / `TenantAccount` |
| Beleg / Kostenposition / Kostenart | `Receipt` / `CostItem` / `CostType` |
| Umlageschlüssel / Vorwegabzug / Rechenprotokoll | `AllocationKey` / `PreDeduction` / `ProtocolEntry` (Rechenprotokoll) |
| Bankkonto / Umsatz / Bankimport | `BankAccount` / `Transaction` (Tabelle `bank_transactions`) / `BankImport` |
| Zähler / Zählerstand / Ablesung | `Meter` / `MeterReading` / `Reading` |
| Zeitraum (von/bis) / Gültigkeit | `Period` (`From`/`To`) / `Validity` |
| Vermieter / Verwalter / Hausmeister / Handwerker | `Landlord` / `Manager` / `Caretaker` / `Tradesperson` |
| Verwaltung (Bereich) / Recht / Richtlinie | `Admin` / `AccessRight` / `Policies` |
| Dateiablage / Dokument / Aufgabe (Dashboard) | `FileStore` / `Document` / `TodoItem` |
| Ergebnis / Regelverletzung | `Result` / `RuleViolationException` |

Rein deutsche Rechtsbegriffe ohne gängige Übersetzung behalten ihren Kern (z. B. `Section35a`, `BetrKvNumber`).

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
dotnet ef migrations add <Name> -p src/MietHahn.Infrastructure -s src/MietHahn.Infrastructure -o Persistence/Migrations
```

- Für jede Modelländerung wird eine Migration angelegt. Der Test `Model_and_migrations_are_in_sync` schlägt sonst fehl.
- `Persistence/Migrations/` enthält nur generierten Code (von Analyzern und Formatierung ausgenommen). Hilfen liegen in `Persistence/MigrationHelpers/`.
- Kein Hand-Editieren generierter Migrationen außer für ergänzendes SQL, z. B. Überlappungsschutz (siehe unten).

## Persistenz-Konventionen

| Thema | Regel |
|---|---|
| Basisklasse | Jede Entität erbt von `Domain.Common.Entity` |
| Schlüssel | `Guid` v7 (`Guid.CreateVersion7()`), wird im Code erzeugt, nicht in der DB |
| Audit | `CreatedAt/By`, `ModifiedAt/By` setzt `EntitySaveInterceptor` (Zeit aus `TimeProvider`, Benutzer aus `ICurrentUser`) |
| Löschen | Immer logisch: `Remove()` setzt `Deleted = true`. Ein globaler Filter (`SoftDelete`) blendet gelöschte Zeilen aus, `IgnoreQueryFilters()` zeigt sie |
| Nebenläufigkeit | `Version` ↔ PostgreSQL-Systemspalte `xmin`. Konflikte werfen `DbUpdateConcurrencyException` |
| Namen | Tabellen und Spalten in snake_case (`EFCore.NamingConventions`) |
| Geld | `decimal` → `numeric(18,2)`. Abweichende Präzision (Mengen, Faktoren) per `HasPrecision` je Property |
| Datum | `DateOnly` → `date`, Zeitpunkte `DateTimeOffset` → `timestamptz` (UTC) |
| Sortierung | Namensspalten mit `.UseCollation(MietHahnDbContext.GermanCollation)` (ICU `de-DE`) |
| Konfiguration | Je Entität eine `IEntityTypeConfiguration<T>` in `Infrastructure/Persistence/Configurations/`, sie wird automatisch gefunden |

### Zeiträume (`Period`)

`Domain.Common.Period` ist das Value Object für alle Gültigkeiten: inklusive Grenzen, `To = null` heißt unbefristet. Es bietet `Contains`, `Overlaps`, `Intersect`, `Days`, `DaysIn(billingYear)` (taggenaue Anteile bei Mieterwechsel), `CalendarYear`, `Month` und `AreNonOverlapping`.

In EF wird jede `Period`-Property automatisch als Complex Type abgebildet: `Validity` → Spalten `validity_from`, `validity_to`.

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

In `Down()` steht dann `migrationBuilder.DropOverlapProtection(name, table)`. Beide Grenzen sind inklusive. Logisch gelöschte Zeilen zählen nicht. Geprüft wird erst beim Commit (`DEFERRABLE INITIALLY DEFERRED`), weil beim Fortschreiben einer Zeitreihe (alten Stand beenden, neuen einfügen) die Befehlsreihenfolge in `SaveChanges` nicht festgelegt ist. Ein Verstoß (`23P01`) kommt daher als `DbUpdateException` oder direkt als `PostgresException`: erkennen mit `DatabaseErrors.IsOverlap(ex)`.

### Zeitabhängige Werte (`TimeSeries`)

Flächen, Mietbestandteile, Personenanzahl usw. werden nie überschrieben. Ein Stand implementiert `ITimeDependent` (`Validity`, `EndValidity`) und wird mit `TimeSeries.CarryForward(readings, from, g => new Entry(…, g, …))` gepflegt: Der jüngste Stand endet am Vortag, beginnt er am selben Tag, wird er korrigiert. Ein Stichtag vor dem jüngsten Stand wirft `RuleViolationException`. Gelesen wird mit `TimeSeries.At(readings, day)`.

Kinder eines Aggregats (z. B. `Unit.Areas`, `Property.Units`) sind mit `DeleteBehavior.Cascade` konfiguriert: Entfernen aus der Liste markiert sie als gelöscht, der Interceptor macht daraus ein logisches Löschen.

### Fachliche Fehler

Domänenregeln werfen `RuleViolationException` mit einer für Benutzer formulierten Meldung. Anwendungsdienste fangen sie (ebenso Überlappungen und `DbUpdateConcurrencyException`) und liefern `Result` (`Succeeded`, `Errors`, `Id`), das die Seite über `<ErrorList>` anzeigt.

## Oberfläche

Statisches SSR mit Blazor (ADR 0001) und eigenem Design-System (ADR 0003, `wwwroot/app.css`, hell/dunkel). **Regeln, Tokens und Seitentypen stehen verbindlich in [ui.md](ui.md)**; dieser Abschnitt behandelt die Blazor-Technik.

**Layouts** (per `_Imports.razor` im Bereichsordner): `AdminLayout` (feststehendes Gerüst: Seitenleiste ab 900 px, darunter untere Leiste mit Menü-Blatt; siehe [ui.md](ui.md#app-gerüst)), `ServiceLayout`, `PortalLayout`, `AccountLayout` (Anmeldeseiten) und `MainLayout` (Fehlerseiten). Neue Navigationspunkte der Verwaltung kommen in `Layout/AdminNavModel.cs` (Bereich, Tab oder Tab im Objekt-/Vertrags-Arbeitsbereich) mit der passenden Berechtigung.

**Bausteine** (`Components/Common`):

| Komponente | Zweck |
|---|---|
| `PageHeader` | `<h1>`, Seitentitel („X – MietHahn“), Zurück-Link, Aktionen |
| `FormField` | Beschriftung + Eingabe + Hinweis + Validierungsmeldung |
| `InputAmount` | Geldbeträge de-DE („1.234,56“, „1234.56“, „-12 €“), rundet auf Cent, Zahlentastatur |
| `SortHeader`, `Pagination` + `TableQuery` | Sortieren/Blättern über Query-Parameter (`?sortierung=…&absteigend=true&seite=2`), unbekannte Spalten fallen auf den Standard zurück |
| `DangerAction` | Riskante Aktion mit Rückfrage ohne JavaScript (`<details>`) |
| `StatusMessage`, `ErrorList`, `EmptyState` | Rückmeldungen (auch über Weiterleitungen hinweg per `Redirector.RedirectWithMessage`) |
| `ChangeHistory` | Audit-Historie eines Datensatzes |

**Unterkomponenten, die selbst Daten laden** (`ChangeHistory`, `LeaseHistory`), erben von `DataComponent` und laden über `GetAsync<TService, T>(…)` in einem eigenen DI-Scope. Beim statischen Rendern laufen ihre Ladevorgänge parallel, ein gemeinsamer DbContext würde „A second operation was started on this context“ werfen. Seiten selbst nutzen die injizierten Dienste direkt.

**Mehrere Zeilenaktionen** (z. B. „Entfernen“ je Tabellenzeile): ein Formular um die Tabelle, je Zeile `<button type="submit" name="RemoveId" value="@id">`. Formularnamen (`@formname`) müssen auf einer Seite eindeutig sein; Formulare in einer Schleife sind ein Fehler. Enums lassen sich nicht per `[SupplyParameterFromQuery]` binden: als `string` lesen und parsen.

Datum: das eingebaute `InputDate<DateOnly>` (nativer Datepicker). Monate mit `<input type="month">` als `string` binden („2026-01“) und selbst parsen (siehe `RentModelForm`), denn `DateOnly` lässt sich daraus nicht binden. Dezimalzahlen: `InputAmount` (Euro) und `InputArea` (m²) rendern `<input type="number" step="0.01">`. Blazor bindet Formularwerte bei statischem SSR **kulturinvariant**; ein Textfeld mit „30,5“ käme nicht als Zahl an. Ein Zahlenfeld zeigt die Zahl in der Browsersprache und sendet immer mit Punkt.

**Formularfelder in Unterkomponenten** (z. B. `PropertyFields`, `AreaSection`): Der Feldname entsteht aus dem Ausdruck in der Komponente (`() => Data.Name` → `Data.Name`). Heißt die Eigenschaft auf der Seite anders, den Präfix angeben: `[SupplyParameterFromForm(FormName = "unit", Name = "Data")] UnitInput? NewUnit`. Der `TestBrowser` lehnt Felder ab, die im Formular nicht existieren, und deckt solche Fehler damit auf. Optionale E-Mail-Felder mit `[OptionalEmail]` statt `[EmailAddress]` validieren, denn leere Formularfelder kommen als `""` an. Formularmodelle in `OnInitialized(Async)` **vor** dem ersten `await` setzen (`Model ??= new()`), weil Blazor dort schon einmal rendert. Das gilt für alles, was das Markup ohne Prüfung verwendet (z. B. Auswahllisten): vorbelegen oder mit `@if (list is not null)` absichern. Der Zwischenstand entsteht nur, wenn eine Abfrage nicht sofort fertig ist, also vor allem unter Last; lokal fällt der Fehler leicht nicht auf. Mehrfach-Checkboxen (`name="Data.PropertyIds"`) binden ohne Haken als `null`, nicht als leere Liste. Tabellen stehen immer in `<div class="table-wrapper">`, damit sie mobil horizontal scrollen statt der Seite.

**Dashboard „Was ist zu tun?“:** Fachmodule registrieren eine `ITodoSource` und liefern `TodoItem`n (Bereich, Titel, Link, Dringlichkeit, Fälligkeit). Die Startseite der Verwaltung sammelt sie mit Streaming Rendering ein.

**Barrierearmut:** Sprunglink „Zum Inhalt springen“, `aria-sort` an Sortierköpfen, `role="alert"/"status"` für Meldungen, sichtbarer Fokus, Touch-Ziele ≥ 44 px.

## Anmeldung & Konten

- **Sicherer Standard:** Jede Seite verlangt eine Anmeldung (Fallback-Policy). Öffentliche Seiten markiert man explizit mit `@attribute [AllowAnonymous]`; das tun z. B. alle Seiten unter `Components/Account/Login/` per `_Imports.razor`.
- **Erster Start:** `/konto/einrichtung` legt den ersten Administrator an (nur solange kein Konto existiert). Danach ist die Zwei-Faktor-Einrichtung Pflicht.
- **Zwei-Faktor-Pflicht** für `Admin` und `Manager`: Solange 2FA fehlt, leitet `UseTwoFactorRequirement()` jede Seite außerhalb von `/konto` auf `/konto/sicherheit/zwei-faktor` um. Grundlage ist der Claim `miethahn:2fa`, der nach dem Aktivieren per `RefreshSignInAsync` erneuert wird. Der QR-Code wird serverseitig als SVG erzeugt (QRCoder), es gibt 10 Wiederherstellungscodes.
- **Magic Link** (`IAccountService.RequestMagicLinkAsync`): Token mit 32 Byte Zufall, in der DB nur als SHA-256-Hash, 15 Minuten gültig, einmalig verwendbar. Eingelöst wird erst per POST-Klick auf der Seite, damit Mail-Scanner den Link nicht verbrauchen. Für Admin und Verwalter gesperrt (würde 2FA umgehen).
- **Einladungen** (`IAccountService.InviteAsync`): Admin, Verwalter und Hausmeister erhalten einen Link zum Festlegen des Passworts, Mieter und Handwerker einen Anmeldelink. Beides ist 7 Tage gültig.
- **Passwörter:** mindestens 12 Zeichen, keine Zeichenklassen-Pflicht (NIST). Reset-Link 2 Stunden gültig. Nach 5 Fehlversuchen 15 Minuten Sperre. Konten mit `Locked = true` können sich nicht anmelden (wirksam spätestens nach 10 Minuten durch die Security-Stamp-Prüfung).
- **Keine Kontenauskunft:** Anmeldung, Magic Link und Passwort-Reset antworten für bekannte und unbekannte Adressen gleich.
- **Rate-Limiting:** POST auf `/konto/*` höchstens `SignIn:RequestsPerMinute` (10) je IP.
- **Links in Mails** basieren auf `Application:BaseUrl` (konfiguriert, nicht aus dem Host-Header).
- **Data-Protection-Schlüssel** liegen in PostgreSQL (`data_protection_keys`): Anmeldungen überstehen Neustarts und sind im DB-Backup enthalten.
- Identity rechnet Token-Laufzeiten und Sperren mit der Systemuhr (ohne `TimeProvider`). Tests prüfen daher die Konfiguration statt Zeit vorzuspulen; eigene Anmeldelinks nutzen `TimeProvider`.

## Autorisierung

| Richtlinie | Wer | Einsatz |
|---|---|---|
| `Policies.Admin` | Admin, Verwalter | alle Seiten unter `Components/Pages/Admin/` (per `_Imports.razor`) |
| `Policies.Service` | Hausmeister, Handwerker | `Components/Pages/Service/` |
| `Policies.Portal` | Mieter | `Components/Pages/Portal/` |
| `Policies.For(AccessRight.X)` | Admin oder Verwalter mit Recht X | z. B. `Admin/Users` (Benutzerverwaltung), `Admin/Operations` (System) |

- **Rechte** (`AccessRight`: Stammdaten, Finanzen, Benutzerverwaltung, System) liegen als Claims `miethahn:permission` am Konto. Admins haben immer alle, Verwalter standardmäßig Stammdaten und Finanzen. Gepflegt wird unter `/verwaltung/benutzer/{id}`.
- Unterordner-`_Imports.razor` **ergänzen** die Richtlinie des Elternordners (beide müssen erfüllt sein).
- **Wirksamkeit:** Der Security-Stamp wird bei jeder Anfrage geprüft und der Principal neu aus der DB aufgebaut. Rollen- und Rechteänderungen gelten damit ab der nächsten Anfrage, Sperren meldet sofort ab. Wer den Stamp ändert (z. B. Authenticator-Schlüssel neu erzeugen, 2FA aktivieren, Passwort ändern), muss danach `SignInManager.RefreshSignInAsync` aufrufen.
- **Startseite** `/` leitet je Rolle nach `/verwaltung`, `/service` oder `/portal`.
- **Ressourcenbasiert:** Für einzelne Datensätze `IAuthorizationService.AuthorizeAsync(user, ressource, anforderung)` mit eigenem `AuthorizationHandler<TAnforderung, TRessource>`. Beispiele: `FileAccessHandler` für `/dateien/{id}`; `PropertyAccessHandler` (`PropertyResource`, `PropertyReadRequirement`): Recht Stammdaten sieht alle Objekte, Hausmeister nur die ihnen zugeordneten (`IPropertyAssignments`, gepflegt unter `/verwaltung/benutzer/{id}`). Nicht erlaubte Objekte verhalten sich im Servicebereich wie unbekannte. Die Verwaltung darf alles; Fachmodule geben einzelne Dateien für andere Rollen über `IFileShare` frei (z. B. Dokumente eines Mieters).
- **Letzter Admin:** kann weder gesperrt noch herabgestuft werden. Das eigene Konto kann man nicht sperren.
- Änderungen an Konten (Anlage, Rolle, Rechte, Sperre) stehen im Audit-Log (`AuditWriter`, Typ `ApplicationUser`).

## Audit-Log

- Jede Speicherung einer `Entity` schreibt in **derselben Transaktion** einen Eintrag in `audit_entries`: Zeitpunkt, Benutzer (`ICurrentUser`), Typ, Id, Aktion (`Created`, `Modified`, `Deleted`) und die geänderten Felder alt → neu als `jsonb`. Speichern ohne echte Änderung erzeugt keinen Eintrag.
- Technische Felder (Id, Audit-Felder, Version) werden ausgelassen. Properties mit `[Confidential]` (IBAN, Zugangsdaten) erscheinen nur als `***`. Klassen mit `[NoAudit]` (Outbox, Job-Protokoll) werden nicht protokolliert.
- **Lesende Zugriffe** auf personenbezogene Daten protokolliert man explizit: `IAccessLog.LogAsync("Person", id, "Mieterakte geöffnet")`. Abschaltbar mit `Audit:LogAccess=false`.
- Anzeige: `<ChangeHistory EntityType="@nameof(Lease)" EntityId="@id" />`, Daten über `IChangeHistory`.
- Aufbewahrung: Änderungen `Audit:RetentionYears` (10, angelehnt an steuerliche Fristen), Zugriffe `Audit:AccessLogRetentionMonths` (12). Bereinigt wird täglich durch den Job `audit-cleanup`.

## Infrastrukturdienste

### E-Mail (`IMailSender`)

```csharp
await mailVersand.EinreihenAsync(new MailAuftrag(["mieter@example.de"], "test", new { GesendetAm = heute, AusgeloestVon = "admin" }));
```

- Mails werden **beim Einreihen gerendert** und in `outgoing_mails` gespeichert. Das ist zugleich der Versandnachweis. Optional gibt es einen fachlichen `Reference`, z. B. `statement:<id>`.
- Der Job `mail-outbox` stellt alle 30 s per SMTP (MailKit) zu. Bei Fehlern wird mit Backoff wiederholt (1, 2, 4 … Minuten, höchstens 6 h), nach 8 Versuchen ist die Mail „fehlgeschlagen“. Unter `/verwaltung/system/mails` sieht man den Status, kann erneut senden und eine Testnachricht verschicken.
- Vorlagen liegen unter `src/MietHahn.Infrastructure/Mail/Templates/<name>.sbn` (Scriban, als Ressource eingebettet). Der Name besteht nur aus `a-z0-9-`. Jede Vorlage setzt `{{ capture subject }}…{{ end }}`, optional eine Textfassung per `capture text`, sonst wird sie aus dem HTML abgeleitet. Der Rahmen kommt aus `_layout.sbn`.
- Konfiguration: Abschnitt `Mail` (`SenderAddress`, `SenderName`, `ReplyTo`, `Smtp:Host/Port/Security/UserName/Password`). `Security` ist `StartTls` (587), `SslOnConnect` (465) oder `None` (nur lokal).

### Vorlagen (Scriban)

`ScribanRenderer` wird von Mails und PDFs genutzt:
- Jede Ausgabe wird **HTML-escaped** (`& < > " '`, Umlaute bleiben lesbar). Bewusst unescaped ist nur `RawHtml`.
- Modell-Eigenschaften stehen in snake_case bereit (`TenantName` → `tenant_name`). Unbekannte Variablen sind ein Fehler.
- Hilfsfunktionen: `money` (1.234,50 €), `date` (TT.MM.JJJJ, Zeitpunkte in deutscher Zeit), `number value decimals`, `text_to_html` (Zeilenumbrüche).

### PDF-Erzeugung (`IPdfGenerator`)

```csharp
var pdf = await pdfErzeugung.ErzeugenAsync(new PdfAuftrag(
    Vorlage: "test", Titel: "Betriebskostenabrechnung 2025", Modell: daten,
    Empfaenger: new Briefanschrift(["Erika Mustermann", "Hauptstraße 5", "12345 Musterstadt"])));
var datei = await pdfErzeugung.ErzeugenUndAblegenAsync(auftrag, "Abrechnung 2025.pdf"); // unveränderlich in der Dateiablage
```

- Inhaltsvorlagen liegen unter `Infrastructure/Pdf/Templates/<name>.sbn` (oder kommen als `TemplateText` aus der Vorlagenverwaltung) und werden in `_layout.sbn` eingebettet. Die Fußzeile ist `_fusszeile.sbn` (Absender, Bank, „Seite x von y“).
- **Mit Empfänger** entsteht ein Brief nach **DIN 5008 Form B**: Briefkopf, Anschriftfeld 45 mm von oben und 20 mm von links (Rücksendezeile, Vermerkzone, 27,3 mm Empfängerzone, höchstens 6 Zeilen), Infoblock mit Datum, Falzmarken bei 105/210 mm und Lochmarke bei 148,5 mm. **Ohne Empfänger** entsteht ein schlichtes Dokument (z. B. Anlagen).
- Absenderdaten kommen aus dem `LandlordProfile`. Ohne Profil schlägt die Erzeugung mit einer klaren Meldung fehl.
- Tabellen: `<thead>` wiederholt sich auf jeder Seite, `<tfoot>` (Summen) steht nur am Ende. Rechtsbündige Beträge mit `class="zahl"`.
- Ausgabe als **PDF/A-2b** (`Pdf:PdfA`) mit Titel und Autor in den Metadaten. Gotenberg ist nur intern erreichbar (`Pdf:GotenbergUrl`).

### Dateiablage (`IFileStore`)

- Erlaubt sind **PDF, JPG, PNG und HEIC**. Erkannt wird am Inhalt (Magic Bytes), nicht an der Endung. Maximalgröße: `FileStore:MaxSizeMb` (Standard 25).
- Fotos (JPG/PNG) werden gemäß EXIF gedreht, auf `MaxImageEdge` (Standard 2560 px) verkleinert und neu kodiert. Dabei fallen **alle Metadaten inklusive GPS** weg. HEIC wird unverändert abgelegt, weil SkiaSharp es unter Linux nicht dekodiert.
- Ablage inhaltsadressiert: `{Path}/{sha[0..2]}/{sha256}`. Gleiche Inhalte liegen nur einmal vor. Metadaten in `stored_files`. Der Dateiname wird bereinigt (keine Pfadanteile), die Endung folgt dem erkannten Typ.
- Abruf nur über `OpenAsync(id)`; Auslieferung über `/dateien/{id}` (`?download=true` als Anhang) mit `FileAccessHandler`.
- Header von `/dateien`: `nosniff`, CSP `default-src 'none'` (keine Skripte), `frame-ancestors 'self'` bzw. `X-Frame-Options: SAMEORIGIN` (Vorschau nur in der App). Alles außer PDF zusätzlich mit `sandbox`; bei PDFs fehlt sie, weil Browser PDFs in einer CSP-Sandbox nicht anzeigen.

### Datensicherung & Vollexport

- `IBackupStatus` liest die Statusdatei des Backup-Containers (`Backup:StatusFile`, Format siehe `deploy/backup/miethahn-backup.sh`). Daraus: Dashboard-Aufgabe, Health-Check `backup` (Degraded) und der Job `backup-warning` (täglich 8:05, Mail an aktive Admins).
- `IDataExport` erzeugt den Vollexport generisch aus dem relationalen EF-Modell: neue Tabellen sind automatisch enthalten. Geheimnisse mit `[NoExport]` (Klasse oder Eigenschaft) ausnehmen und die Liste in `DataExportTests.Every_table_is_exported_or_deliberately_excluded` ergänzen. `[Confidential]` (z. B. IBAN) bleibt im Export, denn es sind fachliche Daten.
- Der ZIP-Export wird zuerst in eine temporäre Datei geschrieben: Die Eintrags-Streams von `ZipArchive` schreiben beim Schließen synchron, und Kestrel erlaubt kein synchrones Schreiben.

### Mietverträge

- `Lease` über ein Mietobjekt (`RentalUnitId` = Einheit ohne Zimmer oder WG-Zimmer; `UnitId` immer gesetzt, `RoomId` bei Zimmern). Der DB-Überlappungsschutz auf (`rental_unit_id`, `term_from`/`term_to`) verhindert zwei Verträge zum selben Tag.
- Zeitabhängig (Zeitreihen mit Überlappungsschutz): `RentComponent` je `RentComponentType` (Kaltmiete, Vorauszahlungen, Pauschale, Zuschläge) und `OccupantCount`. Eine Mieterhöhung ist ein neuer Bestandteil ab Stichtag.
- `Term.To` ist das tatsächliche Ende (Befristung oder Kündigung). `Terminate` beendet auch alle Bestandteile; `RevokeTermination` öffnet sie wieder.
- Kündigungstermine nach § 573c BGB (`TerminationDateForTenant`, `LandlordNoticePeriodMonths`), Fälligkeit am n-ten Werktag: `Domain.Common.WorkingDays` (Mo–Fr ohne bundesweite Feiertage, Ostern nach Gauß).
- `BillingType`: nur `Prepayment` erhält eine Nebenkostenabrechnung (`ReceivesOperatingCostStatement`); Heizkosten zusätzlich bei `HeatingCostsConsumptionBased`.
- **Mietmodelle** (`Lease.RentModel`): *Staffel* mit `RentStep`n (≥ 12 Monate Abstand, § 557a). Der Job `graduated-rent` (täglich 0:30) übernimmt fällige Staffeln als Kaltmiete und mailt den Admins; das Dashboard zeigt Staffeln der nächsten 60 Tage. *Index* mit Ausgangsindex (Monat, Wert); `IndexedRent` rechnet neue Miete = Miete × VPI neu / VPI alt (auf Cent kaufmännisch), Sperrfrist 12 Monate, wirksam ab dem übernächsten Monat nach Zugang der Erklärung. Das Dashboard meldet mögliche Anpassungen. *Standard*: letzte Erhöhung und Kaltmiete vor 3 Jahren (Kappungsgrenze).
- **VPI** (`CpiValue`, `/verwaltung/vpi`): manuelle Pflege oder CSV-Import (GENESIS 61111-0002: „2026;Januar;121,2“, auch „2026-01;121,2“ oder „01.2026;121,2“; Platzhalter „...“ werden übersprungen). Ein Abruf über die GENESIS-API braucht Zugangsdaten und ist noch nicht angebunden.
- Das Vertragsmodul implementiert `IOccupancyLookup` (Objektübersicht, Leerstand), `IPersonReference` (Mieter/Bürge) und `IPersonalDataSource` (DSGVO-Abschnitt `leases`).

### Zähler

- `Meter` (Namespace `Metering`, damit er nicht mit der Klasse kollidiert) mit Typ, Messeinheit, Zuordnung (Haus, Einheit oder Zimmer), optionalem Hauptzähler und Eichfrist (Standard nach MessEV: Kaltwasser 6, Warmwasser/Wärme 5, Gas/Strom 8 Jahre bis Jahresende). Einbau legt den Anfangsstand an.
- **Zählertausch** (`Replace`): Endstand am alten Zähler, neuer Zähler mit `PredecessorId` und Anfangsstand. Alle Zähler einer Kette bilden eine Messstelle; Unterzähler wandern zum neuen Hauptzähler.
- **Verbrauch** (`ConsumptionCalculator`, rein funktional): Ablesungen gelten als Stand am Ende des Tages; Zwischenwerte werden linear interpoliert und gekennzeichnet; über die letzte Ablesung hinaus wird nicht hochgerechnet (`Consumption.MissingReason`). Grundlage für die Abrechnungen in M3.
- **Plausibilität** (`Meter.Validate`): Rückwärtslauf und Abweichung des Tagesverbrauchs > 50 % vom bisherigen Durchschnitt. Ohne `Confirmed` speichert `RecordReadingAsync` nicht, sondern liefert die Warnungen.
- **Ablese-Runde** `/verwaltung/objekte/{id}/ablesung?einheit=&datum=&art=`: ein Zähler je Schritt, Kamera-Upload (`capture="environment"`), Überspringen, Zusammenfassung. Der Mieterwechsel verlinkt die Zwischenablesung der Einheit.
- Dashboard: Eichfrist abgelaufen (dringend) bzw. in 90 Tagen (bald).

### Dokumente

- `Document` verweist auf eine Datei der Dateiablage und hat Titel, Kategorie, Datum, „gültig bis“, Schlagworte (`text[]`), Notiz und **mindestens einen Bezug** (Objekt, Einheit, Vertrag, Person).
- Suche (`IDocumentLibrary.SearchAsync`) über Titel, Schlagworte, Notiz und Dateiname (`ILIKE`). Bezugsfilter schließen Untergeordnetes ein: Zum Objekt gehören die Dokumente seiner Einheiten und Verträge, zur Person die ihrer Verträge.
- **Freigaben** (`DocumentShares.VisibleAsync`, gilt für Portal, Servicebereich und `/dateien` über `DocumentFileShare : IFileShare`):
  - *Für Mieter:* Mieter (Person mit `UserId`, Vertragsrolle Mieter) sehen Dokumente ihrer Person und ihrer Verträge (auch nach dem Auszug) sowie der Einheit und des Objekts, in denen sie **aktuell** wohnen.
  - *Für Hausmeister:* Dokumente zu zugeordneten Objekten, deren Einheiten und Verträgen.
- Oberfläche: `/verwaltung/dokumente` (Suche, Kategorie), Upload `/verwaltung/dokumente/neu?objekt=|einheit=|vertrag=|person=` (vorbelegter Bezug, ohne Titel gilt der Dateiname), Detailseite mit Vorschau (Bild bzw. PDF im `iframe`). `DocumentsSection` zeigt die Dokumente auf Objekt-, Einheit-, Vertrags- und Personenseite; der Servicebereich zeigt Hausmeister-Freigaben.
- Dashboard: Dokumente mit „gültig bis“ in 60 Tagen bzw. abgelaufen. DSGVO-Auskunft: Abschnitt `documents`.

### Finanzen: Sollstellungen & Mieterkonto

- `Charge` = Forderung an den Mieter (negativ = Gutschrift, z. B. `Refund`). Monatsmieten (`ChargeType.Rent`) entstehen **je Bestandteil und Leistungsmonat** automatisch, alles andere (Nachzahlung, Mahngebühr, Zinsen, Kaution, Saldovortrag …) von Hand. `Payment` = Eingang (positiv) bzw. Auszahlung an den Mieter (negativ), optional mit Tilgungsbestimmung (`ChargeId`, § 366 Abs. 1 BGB).
- **Buchhaltungsbeginn** (`FinanceSettings.AccountingStart`, `/verwaltung/finanzen/einstellungen`): Monatsmieten erst ab diesem Monat, damit übernommene Altverträge keine Jahre an „Rückständen“ erzeugen. Ohne Beginn entstehen keine Monatsmieten (Dashboard-Hinweis). Ältere Rückstände als `BalanceCarriedForward` erfassen.
- `ChargeCalculation.Month` (rein funktional): Bestandteile × Tage/Tage im Monat, ungerundet summiert, auf Cent kaufmännisch gerundet (Einzug 15.3. → 17/31). Fällig am n-ten Werktag, bei Einzug im Monat frühestens am Einzugstag.
- `ITenantAccounts.GenerateChargesAsync` ist idempotent: legt fehlende Monatsmieten bis zum laufenden Monat an, gleicht geänderte an (rückwirkende Mieterhöhung, Auszug) und entfernt überzählige. Job `charges` täglich 0:40 (nach der Staffelmiete um 0:30), dazu Knopf im Mieterkonto. Mahnungen: siehe *Mahnwesen*. Ein eindeutiger Teilindex (`kind = 'Rent'`, Vertrag, Monat, Bestandteil) verhindert Duplikate auch bei parallelen Läufen.
- **Verrechnung** (`Allocation.Distribute`, rein funktional, nicht gespeichert, sondern bei jedem Lesen berechnet): Guthaben chronologisch; zuerst die bestimmte Forderung, dann die fälligen nach `AllocationOrder` (Standard §§ 366, 367 BGB: Kosten → Zinsen → älteste). Gleicher Rang und gleiche Fälligkeit (Bestandteile einer Monatsmiete) werden anteilig getilgt, Cent-Reste nach größtem Rest. Vorauszahlungen tilgen erst bei Fälligkeit, der Zufluss bleibt das Zahlungsdatum.
- Mieterkonto `/verwaltung/vertraege/{id}/konto`: Buchungen mit Saldo und offenem Betrag, Zahlung/Forderung erfassen, Löschen nur für manuelle Buchungen, **Mieten je Jahr** (Soll nach Leistungsmonat, davon bezahlt → Nebenkostenabrechnung; Zufluss nach Zahlungsjahr → Anlage V).
- Offene Posten `/verwaltung/finanzen/offene-posten` und als CSV (`…/offene-posten.csv`: Semikolon, deutsche Zahlen, UTF-8 mit BOM, CRLF, Formel-Präfixe entschärft). Recht **Finanzen**.

### Bank: Konten & Datei-Import

- `BankAccount` (IBAN geprüft und eindeutig, Typ Mietkonto/Darlehen/Rücklage, Objekte als `uuid[]`), `Transaction` (Betrag positiv = Eingang, Gegenseite, Verwendungszweck, EREF/MREF/CRED, Status offen/zugeordnet/ignoriert), `BankImport` (Vorschau → importiert, Importprotokoll; die Datei liegt nur bis zum Import in der Datenbank).
- Parser in `Infrastructure/Bank/Import` (`BankFileReader` erkennt das Format):
  - **camt.053** (`Camt053Parser`): Elemente über den lokalen Namen, daher .001.02 bis .08; vorgemerkte Einträge (PDNG) übersprungen, Sammler je `TxDtls` zerlegt; XML ohne DTD/externe Entitäten.
  - **MT940** (`Mt940Parser`): Feld 61 (Valuta, Buchungsdatum über den Jahreswechsel), Feld 86 strukturiert (?00, ?20–?29/?60–?63, ?30–?33).
  - **CSV** (`CsvParser`, `CsvProfile`): Spalten über Überschriften (Alternativen mit „|“), Vorspann wird übersprungen, Soll/Haben oder Betrag, Name je Richtung (DKB), Zerlegung per Regex (comdirect), Zeichensatz auto (UTF-8, sonst Windows-1252). Mitgeliefert: Sparkasse (CSV-CAMT), Volksbank, DKB, ING, comdirect (`CsvDefaults`). Eigene Profile unter `/verwaltung/bank/csv-profile`. Passen mehrere Profile, gewinnt das mit den meisten erkannten Spalten.
  - SEPA-Kennungen im Verwendungszweck (`SepaPurpose`: EREF+, MREF+, CRED+, SVWZ+, ABWA+).
- **Duplikate** (`TransactionFingerprint`): SHA-256 aus Konto-IBAN, Buchungsdatum, Betrag, Verwendungszweck (ohne Leerraum, Großschreibung), Gegen-IBAN und End-to-End-ID; gleiche Umsätze innerhalb einer Datei werden durchnummeriert. Eindeutiger Index (Konto, Hash) auch über gelöschte Umsätze. Die IBAN eines Kontos mit Umsätzen ist daher nicht änderbar.
- Testdateien (anonymisiert) für alle Formate liegen unter `tests/MietHahn.Integration.Tests/Bank/TestFiles`; neue Bankformate dort mit einem Beispiel ergänzen.

### Bank: FinTS-Direktabruf

Bedienung und Registrierung: [banking-fints.md](banking-fints.md). Technik:

- **Konfiguration:** Abschnitt `FinTs` (`FinTsOptions`: `ProductId`, `ProductVersion`, `TanTimeoutSeconds`), in Docker optional über `FINTS_PRODUCT_ID` und `FINTS_PRODUCT_VERSION`. Die Registrierungsnummer von MietHahn ist Standardwert (`FinTsOptions.DefaultProductId`); `ProductId` überschreibt sie für Forks. libfintx kennt die Nummer nur global (`FinTsGlobals.ProductId`/`Version`); sie wird vor jedem Abruf gesetzt.
- **Konto:** `BankAccount.FinTsUrl` (nur https), `FinTsBankCode` (8 Ziffern), `FinTsUserId` (mit DataProtection, Zweck `MietHahn.FinTs.UserId`, verschlüsselt; ver- und entschlüsselt wird in `BankService`). `SetFinTs` verlangt alle drei Werte oder keinen. Die PIN wird nie gespeichert.
- **Ablauf** (`Infrastructure/Bank/FinTs`): `FinTsService.StartAsync` prüft Konto und PIN und startet den Dialog im Hintergrund (`FinTsSession` in `FinTsSessions`, nur im Speicher). Der Hintergrundlauf spricht nur mit der Bank und berührt die Datenbank nicht: Synchronisation → `Accounts` (Konto per IBAN wählen) → `Transactions_camt` (camt.053). Die TAN-Rückfrage von libfintx (`TANDialog`) wartet in `FinTsSession.AskAsync`, bis die Oberfläche `ProvideTan` bzw. `ConfirmApproval` ruft; Phase `TanRequired` oder, bei `IsDecoupled`, `ApprovalRequired`. Die Oberfläche (`FinTsFetch.razor`) fragt `StateAsync` jede Sekunde ab.
- **Ergebnis:** `StateAsync` legt im Aufrufkontext (also mit dem angemeldeten Benutzer im Änderungsprotokoll) ein `BankImport` mit `BankFormat.FinTs` an. Der Inhalt ist eine JSON-Liste (`FinTsPayload`), `BankFileReader` erkennt sie; Vorschau, Duplikaterkennung und Import laufen wie bei Dateien. `FinTsMapping` übersetzt `CamtTransaction` (vorgemerkte Einträge entfallen).
- **Sicherheit:** libfintx erhält `NullLoggerFactory`; bei Fehlern wird nur der Ausnahmetyp protokolliert; die PIN wird nach dem Dialog überschrieben; ein Fehlversuch bricht ab, Bankmeldungen 9930/9931/9942 bekommen einen Hinweis.
- **Offen:** Verfahren mit Bild/Flickercode (photoTAN, chipTAN), MT940-Rückfall für Banken ohne camt (HKCAZ), automatischer Abruf per Job (wegen der Pflicht zur starken Kundenauthentifizierung bewusst nicht), Prüfung gegen echte Bankverbindungen.

### Vorlagen-Verwaltung & Schreiben (#42)

- **Entitäten:** `DocumentTemplate` (Name eindeutig, `TemplateKind` *Schreiben* oder *Baustein*, Betreff, Text, `Revision`) und `DocumentTemplateRevision` (unveränderlicher Schnappschuss je Fassung). `Edit` zählt `Revision` nur hoch, wenn sich Betreff oder Text ändern; `DocumentTemplates.SaveAsync` legt dann die Fassung an. Umbenennen erzeugt keine Fassung. Bausteine haben keinen Betreff.
- **Prüfung beim Speichern:** Syntax (`ScribanRenderer.Validate`) und ein Probe-Rendern mit den Beispieldaten des Katalogs und allen vorhandenen Bausteinen. Das Rendern läuft mit `StrictVariables`, unbekannte Platzhalter und unbekannte Bausteine werden also schon beim Speichern gemeldet.
- **Katalog:** `TemplateCatalog` (Application) beschreibt je Art die Platzhalter, die Hilfsfunktionen (`money`, `date`, `number`, `text_to_html`, `include`) und liefert die Beispieldaten (`LetterModel`). Neue Arten: Enum-Wert, Modell, `Placeholders` und `Sample` ergänzen.
- **Bausteine:** `{{ include 'Name' }}` im Text. `ScribanRenderer.RenderText(…, snippets)` löst `include` ausschließlich gegen die übergebenen Bausteine auf (`SnippetLoader`), ein Zugriff auf Dateien oder Datenbank aus der Vorlage ist nicht möglich. `TemplateSnippets.LoadAsync` liest sie; `PdfGenerator` tut das bei `PdfRequest.TemplateText` selbst. Ein Baustein, der noch in einem anderen Text eingebunden ist, lässt sich weder löschen noch umbenennen.
- **Standardvorlagen:** `TemplateDefaults` (eingebettete `Documents/Templates/*.sbn`): Bausteine *Grußformel* und *Bankverbindung* (nur mit IBAN im Vermieterprofil), Schreiben *Ankündigung von Arbeiten* und *Allgemeines Schreiben*. `InstallDefaultsAsync` legt nur fehlende an (nach Namen) und fasst vorhandene nie an. Die Texte sind Ausgangspunkte mit Hinweis auf die rechtliche Prüfung.
- **Import/Export:** JSON `{format: "miethahn-template", version: 1, name, kind, description, subject, body}` (`/verwaltung/einstellungen/vorlagen/{id}/export.json`, Import auf der Listenseite, höchstens 300 KB). Beim Import wird wie beim Speichern geprüft; ein vorhandener Name bekommt „(Import)“, „(Import 2)“ …
- **Vorschau:** `PreviewAsync` rendert den ungespeicherten Text über `IPdfGenerator` (`PdfRequest.TemplateText`) mit Beispielanschrift. Die Seite legt das PDF kurz im Speicher ab (`TemplatePreviews`, 10 Minuten) und zeigt es über `/verwaltung/einstellungen/vorlagen/vorschau/{id}.pdf` im Rahmen.
- **Schreiben an Mieter (Serienbrief)** `/verwaltung/dokumente/schreiben` (Recht **Stammdaten**), `ILetters`/`Letters`: Empfänger sind die laufenden Mietverträge eines Objekts. Je Mietvertrag entstehen ein PDF (Anschrift und Anrede wie bei Mahnungen über `LetterParts`, Modell `LetterModel` mit den echten Daten), ein `Document` (Kategorie Schriftverkehr, am Mietvertrag, optional im Portal freigegeben) und optional eine E-Mail (Mailvorlage `letter`, PDF im Anhang). Jedes Schreiben läuft in einer eigenen Transaktion: ein Fehler (z. B. fehlende Adresse, Vorlagenfehler) stoppt die anderen nicht und steht im Ergebnis. Mieter ohne E-Mail-Adresse bekommen das PDF trotzdem, mit Hinweis.
- **Unveränderlichkeit:** `Document.TemplateRevisionId` verweist auf die Fassung, aus der das Dokument entstand. Erzeugte Dokumente sind gespeicherte Dateien; eine spätere Änderung oder das Löschen der Vorlage berührt sie nicht (Löschen ist logisch, die Fassungen bleiben).
- **Seiten:** `/verwaltung/einstellungen/vorlagen` (Liste mit Standardvorlagen und Import) und `…/neu` bzw. `…/{id}` (Editor mit Platzhaltern, Bausteinen, Fassungen, Export und Löschen); Recht **System**.

### Mietvertrag & WG-Zimmervertrag (#43)

- **Vorlagenart `Contract`:** wird mit dem `ContractModel` (Application) gerendert. Es bündelt die Daten eines Mietvertrags zum Mietbeginn: Mieter und Bürgen (Name, Geburtsdatum, Anschrift), Objekt, Einheit (Fläche, Ausstattung), bei einem WG-Zimmer Zimmer, Fläche, Anteil der Gemeinschaftsflächen, Möblierung (`Room.FurnishingList`, eine Zeile je Gegenstand) und die übrigen Zimmer, Mietzeit, Abrechnungsart (Vorauszahlung/Pauschale/Inklusiv), Mietmodell (Staffel mit Stufen, Index mit Ausgangsindex), die Mietbestandteile zum Mietbeginn, Kaution samt Obergrenze (3 Grundmieten, § 551 BGB) und die vereinbarten bzw. ausgeschlossenen Betriebskostenarten nach § 2 BetrKV. Die Platzhalter stehen im Editor unter „Platzhalter“.
- **Prüfung mit mehreren Beispielen:** `TemplateCatalog.Samples` liefert für Verträge zwei Fälle (Wohnung mit Vorauszahlung, unbefristet, Standardmiete; WG-Zimmer mit Pauschale, befristet, Staffel und Index). `DocumentTemplates.Check` rendert jeden Text mit allen Beispielen, damit auch Zweige hinter `if` geprüft sind. Bausteine müssen nur für eine Art Dokument funktionieren (Schreiben oder Vertrag).
- **Mitgelieferte Texte** (`TemplateDefaults`): *Mietvertrag Wohnraum*, *Zimmervertrag WG* und die Anlagen als Bausteine (*Anlage Hausordnung*, *Anlage Energieausweis*, *Anlage Datenschutzinformation*, *Anlage Übergabeprotokoll*). Schönheitsreparaturen bleiben in der Basisvorlage bewusst beim Vermieter (BGH-Rechtsprechung zu starren Fristen und unrenovierter Übergabe, mit Hinweis im Text); die Texte sind Basisvorlagen und müssen rechtlich geprüft werden.
- **Ablauf** (`ILeaseContracts`/`LeaseContracts`, Seite `/verwaltung/vertraege/{id}/vertragsdokument`): *Entwurf* (`CreateDraftAsync`: PDF im Dateispeicher, `LeaseContract` mit Verweis auf die Vorlagenfassung; ein früherer offener Entwurf wird verworfen, ein Teilindex erlaubt höchstens einen je Mietvertrag) → *unterschrieben hochladen* (`ConcludeAsync`: Datei als `Document` der Kategorie Mietvertrag am Mietvertrag, optional im Portal freigegeben, mit der Fassung) → Status `Concluded`. Verwerfen ist nur für offene Entwürfe möglich. Der Mietvertrag selbst (Sollstellungen, Mieterkonto) ist unabhängig davon angelegt; der Status steht nur am Vertragsdokument.
- **Tests:** `LeaseContractsTests` prüft mit echten PDFs, dass ein Zimmervertrag Pauschale, Gemeinschaftsflächen und Möblierung aus den Stammdaten enthält, ein Wohnungsvertrag Fläche, Miete, Kaution, Kostenarten und Anlagen, und den Ablauf Entwurf → Unterschrift.

### Mieterhöhungs-Assistent (#44)

- **Reine Regeln** (`Domain.Leases.ComparativeRent`, `IndexedRent`): Wirkung eines Verlangens nach § 558 ab dem Beginn des dritten Kalendermonats nach Zugang (`EffectiveFrom`), Zustimmungsfrist bis zum Ende des zweiten Kalendermonats (`ConsentDeadline`), frühester Zugang (`EarliestDeclaration`: Miete 15 Monate unverändert, zusätzlich ein Jahr nach einer früheren Erhöhung), Kappungsgrenze (`CapLimit`: Miete vor drei Jahren plus 20 % bzw. 15 %, auf den Cent abgerundet), Höchstmiete = niedrigere von Kappungsgrenze und Vergleichsmiete. `Assess` liefert Fehler, Hinweise (Mietpreisbremse bei Neuvermietung) und die Termine. Die Kappungsgrenze (20 oder 15 %) stellt man am Objekt ein (`Property.RentCapPercent`, Gemeinde mit Verordnung).
- **Entität** `RentIncrease` (Tabelle `rent_increases`) mit Art (`Comparative`, `Index`, `Graduated`, `FlatRate`) und Stand: *Entwurf* (Schreiben erzeugt) → *zugegangen* (Zugangsdatum erfasst, Termine daraus berechnet) → *zugestimmt* (Vergleichsmiete: neuer Mietbestandteil) oder *abgelehnt*; Index und Pauschale werden mit dem Zugang *wirksam* (ohne Zustimmung); Staffel-Information ändert nichts am Vertrag. *Verworfen* ist für Entwürfe und offene Verlangen möglich.
- **Service** `IRentIncreases`/`RentIncreases`: `OverviewAsync` (Daten für die Seite), `CheckAsync` (Prüfung ohne Speichern), `PreviewAsync` (PDF), `CreateDraftAsync` (Prüfung, Schreiben als PDF aus der Vorlage, `Document` am Mietvertrag mit der Vorlagenfassung, Entwurf), `DeclareAsync`, `AgreeAsync`, `RefuseAsync`, `WithdrawAsync`. Je Art:
  - **Vergleichsmiete:** nur bei Standardmiete (bei Staffel/Index ausgeschlossen, § 557a Abs. 2, § 557b Abs. 2). Begründung nach Wahl: Mietspiegel (Wert in €/m² × Wohnfläche, Spanne wird geprüft), Vergleichswohnungen (mindestens drei; maßgeblich ist der niedrigste Preis je m²) oder Gutachten (Betrag vom Sachverständigen). Die neue Miete ist leer = Höchstbetrag. Beim Zugang werden die Termine neu aus dem tatsächlichen Datum berechnet und das Verlangen mit diesem Datum noch einmal geprüft.
  - **Indexmiete:** neueste VPI-Werte, neue Miete = Miete × neuer Index ÷ Ausgangsindex (auf den Cent), wirksam ab dem übernächsten Monat nach Zugang, Sperrfrist ein Jahr. Mit dem Zugang setzt `Lease.AdjustIndex` die Miete und den neuen Ausgangsindex.
  - **Staffel:** freiwillige Information über die nächste Stufe.
  - **Pauschale (§ 560 Abs. 1):** nur mit bestätigtem Vorbehalt im Vertrag und erläutertem Grund; wirksam ab dem übernächsten Monat nach Zugang als neuer Betrag des Bestandteils *Betriebskostenpauschale*. Hinweis auf den Vorrang der HeizkostenV.
- **Vorlagenart `RentIncrease`** mit dem `RentIncreaseModel`; mitgeliefert: *Mieterhöhung Vergleichsmiete (§ 558 BGB)* (Begründung je Mittel, Wartefrist und Kappungsgrenze, Zustimmungsfrist, Hinweis auf § 561, Zustimmungserklärung), *Mieterhöhung Indexmiete (§ 557b BGB)* (Indexstände, Änderung in %, neue Miete, Wirksamkeit), *Information Staffelmiete* und *Anpassung Betriebskostenpauschale (§ 560 BGB)*. Basisvorlagen, rechtlich zu prüfen.
- **Seite** `/verwaltung/vertraege/{id}/mieterhoehung` (am Vertrag: „Mieterhöhung“): Kennzahlen, Formular je Art mit „Prüfen“ (Fehler, Hinweise, Termine, Höchstmiete), „Vorschau“ und „Schreiben erzeugen“, darunter die Schreiben mit Aktionen je Stand.
- **Tests:** `ComparativeRentTests` (Domain: Kappungsgrenze 20/15 %, Sperrfristen, Wirksamkeits- und Fristdaten), `RentIncreasesTests` (mit echten PDFs: je Art das Schreiben mit den Pflichtangaben und die Übernahme der Miete).

### Übergabeprotokoll Ein-/Auszug (#45)

- **Entität** `HandoverProtocol` (Tabelle `handover_protocols`, eines je Mietvertrag und Art `MoveIn`/`MoveOut`). Der ausgefüllte Teil (`HandoverContent`: Räume mit Bauteilen und Zustand *in Ordnung / Gebrauchsspuren / Mangel*, Mängel mit Fotos, Schlüssel, Zählerstände, Bemerkungen, beim Auszug die neue Anschrift) liegt als JSON (`jsonb`) in einer Spalte, damit sich die Struktur ohne Migration erweitern lässt. Stand *Entwurf* → *abgeschlossen* (unterschrieben); danach ist nichts mehr änderbar, Löschen nur im Entwurf.
- **Vorbelegung** (`Handovers.CreateAsync`): Räume aus den Stammdaten (Wohnung: Flur, Küche, Bad, Zimmer 1…n nach Zimmeranzahl, Balkon; WG-Zimmer: das Zimmer plus Gemeinschaftsräume), Bauteile je Raum, Schlüsselarten, die Zähler der Einheit (`IMeterService.RoundAsync`). Beim Auszug dient das Einzugsprotokoll als Ausgangspunkt: Räume, Zustand, Schlüsselzahlen und Mängel werden übernommen (Mängel mit `SourceId`).
- **Vergleich** (`Domain.Leases.HandoverComparison`): verschlechterter Zustand, Mängel ohne Verweis auf das Einzugsprotokoll (*neu*), schon beim Einzug festgehaltene Mängel, fehlende Schlüssel.
- **Unterschreiben** (`SignAsync`): Unterschrift auf dem Gerät (Canvas, `wwwroot/js/signature.js`, Zeigerereignisse für Finger, Stift und Maus, Ausgabe als PNG) oder `SignWithScanAsync` für den unterschriebenen Ausdruck. Alles in einer Transaktion: 1. **Zählerstände als Zwischenablesung** (`ReadingType.InterimReading`, Datum = Übergabetag, Foto) über `IMeterService.RecordReadingAsync`; Warnungen (rückwärts laufender Zähler, Ausreißer) müssen einmal bestätigt werden, bis dahin wird nichts abgelegt. 2. PDF (`Pdf/Templates/handover.sbn`, mit Fotos und Unterschriften als Data-URI) bzw. der Scan, als `Document` (Kategorie Protokoll, im Portal sichtbar). 3. Beim Auszug die neue Anschrift ab dem Tag nach Mietende bei den Mietern. 4. Status *abgeschlossen*. Die Zwischenablesungen gehen in `ConsumptionCalculator` ein (Verbrauch zwischen Ein- und Auszug).
- **Seite** `/verwaltung/vertraege/{id}/uebergabe/einzug|auszug`: für das Smartphone gebaut (einspaltig, Fotos mit `capture="environment"`), Räume als aufklappbare Abschnitte, Vorschau als PDF, Unterschrift oder Scan, beim Auszug der Vergleich und die Hinweise zu Nebenkostenabrechnung und Kaution.
- **Offen aus dem Issue:** Die Mahn-/Zahlungserinnerungsvorlage ist weiterhin die eingebettete Basisvorlage (`Pdf/Templates/dunning.sbn`); sie in die Vorlagenverwaltung zu überführen war nicht Teil dieses Schnitts.

### Belege & Kostenpositionen

- `Receipt` (Lieferant als Kontakt oder Name, Rechnungsnr./-datum, Leistungszeitraum als zwei Spalten, Brutto/Netto/USt mit Summenprüfung, fällig/bezahlt, Brennstoffmenge, -einheit und CO₂ in kg für das CO2KostAufG) mit Seiten (`ReceiptFile`, Dateiablage, SHA-256, unveränderlich) und `CostItem`en.
- **Kostenarten** (`Domain.Receipts.CostTypes`): fester Katalog mit stabilen Schlüsseln, `betrkv-01` … `betrkv-17` nach § 2 BetrKV (mit Hinweis zu Kabel-TV seit 1.7.2024) und nicht umlagefähige Arten (Instandhaltung, Modernisierung, Verwaltung, Bankgebühren, Schuldzinsen, Beratung, Leerstand, Sonstiges). Eigene Unterarten (`CostSubtype`) und Umlageschlüssel siehe *Betriebskosten: Umlageschlüssel & Rechenkern*.
- `CostItem`: Kostenart, Betrag, Objekt (optional Einheit), Abrechnungszeitraum (Standard: Leistungszeitraum, sonst Kalenderjahr), umlagefähig (Standard laut Kostenart), § 35a EStG (haushaltsnahe Dienstleistung/Handwerkerleistung mit Arbeitskostenanteil). Bei den Kostenarten 4 bis 6 (Heizung) ordnet die „Art der Heizkosten“ (`HeatingKind`) die Position zu, siehe *Heizkosten & CO₂-Aufteilung*. Die Summe der Positionen darf den Rechnungsbetrag nicht übersteigen; ein Beleg ist „aufgeteilt“, wenn sie ihm genau entspricht. Der Bruttobetrag lässt sich nicht unter die Summe senken.
- Korrekturen: Datenänderungen stehen im Änderungsprotokoll (Versionen der Werte), Dateien werden nie überschrieben, Löschen ist logisch.
- Oberfläche `/verwaltung/belege` (Filter Jahr, Objekt, Kostenart, ohne Zahlung, nicht aufgeteilt), Erfassen mit mehreren Dateien (`IFormFileCollection`) und optional gleich einer Kostenart, auf Wunsch mit „Vorschlag aus Beleg auslesen“ (siehe *Belegauslesung*). Dashboard: fällige unbezahlte Rechnungen (7 Tage), nicht aufgeteilte Belege. „Zahlungen ohne Beleg“ steht bei den Umsätzen (siehe Zahlungszuordnung).

### Belegauslesung (KI, optional)

- **Schnittstellen** (`Application/Receipts`): `IReceiptExtractor` (Anbieter: `Info`, `ExtractAsync(Dokumente) → ExtractorResult`) und `IReceiptExtraction` (Anwendungsfall `SuggestAsync(fileIds) → ExtractionOutcome`: Dateien laden, Anbieter fragen, prüfen, gelernte Regeln anwenden, Zugriff protokollieren; speichert nichts). Ergebnis ist ein `ReceiptSuggestion` (`Domain/Receipts`) mit `Suggested<T>(Value, Confidence)` je Feld; `Confidence < 0,75` gilt als unsicher (`UncertainFields()`).
- **Prüfungen** (`Domain.Receipts.SuggestionChecks`, rein): Beträge ≤ 1 Mio. und auf Cent gerundet, Netto + USt = Brutto (sonst Warnung, Konfidenz gedeckelt; fehlender dritter Wert wird abgeleitet), übliche Steuersätze 0/7/19 %, Daten ab 2000 und nicht weit in der Zukunft, Leistungszeitraum und Fälligkeit stimmig, IBAN-Prüfsumme (`Iban`), bekannte Kostenart, Arbeitskosten ≤ Rechnungsbetrag, Summe der Positionen. Unmögliches wird verworfen, Zweifelhaftes markiert.
- **Anbieter** (`Infrastructure/Receipts/Extraction`, Konfigurationsabschnitt `ReceiptExtraction`, Auswahl in `DependencyInjection.AddReceiptExtraction`): `NoReceiptExtractor` (Standard), `ClaudeReceiptExtractor` (Messages API `POST {BaseUrl}/v1/messages`, PDF als `document`-, Fotos als `image`-Block, strukturierte Antwort über erzwungenen Tool-Aufruf `record_receipt` mit JSON-Schema, Modell und Adresse konfigurierbar), `OllamaReceiptExtractor` (`POST {BaseUrl}/api/chat` mit Schema als `format`, nur Bilder). Gemeinsam: `ExtractionSchema` (Prompt und Schema), `ExtractionResponseParser` (tolerant gegenüber Codeblöcken, Text um das JSON, Beträgen/Daten als deutscher Text und Konfidenz in Prozent; alles andere ist eine `ExtractionException` mit Benutzermeldung), `ExtractionHttp` (Fehlerabbildung ohne Schlüssel/Inhalt). Der API-Schlüssel kommt nur aus der Konfiguration. Ein unbekannter Anbietername schaltet nichts ein.
- **Datenschutz im Code:** Standard `None`; `Claude` braucht `AcceptExternalProcessing=true` **und** Schlüssel (sonst wird nichts gesendet); jede Übergabe geht über `IAccessLog` (`StoredFile`, „Beleg zur Auslesung an … übergeben“); Logs über `ExtractionLog` ohne Inhalte. Die Dateikennungen zwischen den Formularen reisen verschlüsselt (`UploadToken`, Data Protection), damit ein gefälschtes Formular keine fremden Dateien auslesen oder anhängen kann.
- **Lernen:** `SupplierCostTypeRule` (Tabelle `supplier_cost_type_rules`, Schlüssel `contact:<id>` bzw. normalisierter Name `name:…`, eindeutig). `ReceiptService` schreibt sie beim Speichern einer Kostenposition (`CreateAsync`, `AddItemAsync`); `ReceiptExtraction` wendet sie an und ordnet den Lieferanten einem Kontakt zu, wenn Firma oder Name genau einem entspricht.
- **Oberfläche:** `Receipts/Create.razor` (Formular `extract` mit Upload; danach ist das Belegformular vorbelegt und trägt die hochgeladenen Dateien im verschlüsselten Feld `StoredFileIds`), `Receipts/Details.razor` (Auslesen aus den vorhandenen Seiten, Werte nur im Formular), `ExtractionPanel` (Vorschlag, Warnungen, Tokens), `FormField Uncertain` (gelbe Markierung), `Operations/ReceiptExtraction.razor` (`/verwaltung/system/belegauslesung`: Anbieter, Status, Datenschutzhinweis). Die Auslesung läuft **synchron** im Request (Zeitlimit `ReceiptExtraction:TimeoutSeconds`) und nicht als Quartz-Job, weil die Seite das Ergebnis sofort zum Prüfen braucht.
- **Tests:** Domain (`SuggestionChecksTests`, `SupplierCostTypeRuleTests`), Integration ohne Netz (`Receipts/Extraction`: `ExtractionResponseParserTests` inkl. fehlerhafter Modellausgabe, `ProviderTests` gegen `FakeHttpHandler`, `ReceiptExtractionTests` mit DB und Anbieter-Wechsel nur per Konfiguration, Zugriffsprotokoll, Lernregeln, `EvaluationSetTests`), Web (`ReceiptExtractionPagesTests` mit Platzhalter-Anbieter, `ReceiptExtractionClaudeFlowTests` mit echtem Claude-Anbieter und gefälschtem HTTP-Handler). Es wird **nie** das echte Internet angesprochen.
- **Evaluationsset:** `EvaluationSetTests` hat 20 erfundene, anonymisierte Belege mit erwarteten Werten (Lieferant, Rechnungsnummer, Datum, Brutto) und Modellantworten in den üblichen Schreibweisen; eine Antwort ist absichtlich falsch (erfundener Betrag). Je Anbieter (Claude, Ollama) wird die Trefferquote für Betrag und Datum ermittelt und muss ≥ 95 % sein (aktuell 95 %); die Auswertung steht in der Testausgabe. Das Set prüft die Verarbeitungskette gegen Regressionen, **nicht die Güte eines echten Modells**. Um ein echtes Modell zu messen, dieselben Fälle (Belegbild + Erwartung) gegen den Live-Anbieter laufen lassen.

### Zahlungszuordnung (Umsatz ↔ Mieterkonto / Beleg)

- **Matching-Engine** (`Domain.Bank.Matching.PaymentMatcher`, reine Logik ohne Persistenz): `Suggest(MatchInput, MatchContext)` liefert gerankte `MatchSuggestion`en mit Score (0–100), Begründungen und Teilbeträgen. Reihenfolge: 1. erste passende Benutzerregel (nach `Priority`, Score 100), 2. Heuristik. Eingänge werden je Mietverhältnis bewertet: Gegen-IBAN aus den `BankDetails` des Mieters (+45), Name der Gegenseite (Nachname + Vorname 32, nur Nachname 24, Firma 30; Umlaute/Reihenfolge/ein Tippfehler bei Wörtern ab 6 Buchstaben egal), Betrag (= älteste offene Forderung 25, = andere offene 22, = n älteste Monate = Sammelzahlung 22, = Monatsmiete 18), Verwendungszweck (Einheit mit „Whg“/„WE“-Varianten +15, Monat/Zeitraum wie „03/26“, „März 2026“, „Jan–Mär 2026“ +10, „Miete“ +5, Mieter im Zweck +8), Abzüge vor Mietbeginn/lange nach Mietende. Ab `MinimumScore` (35) wird vorgeschlagen; ab 70 gilt die Sicherheit als „hoch“. Textfunktionen in `TextMatching` (`Normalize`, `Months`, `ContainsUnit`, `TokenMatches`).
- **Ausgaben:** Regel (Kostenart, Objekt, Einheit, umlagefähig), sonst früherer Umsatz mit derselben Gegen-IBAN (75) bzw. demselben Namen (60) aus den bisherigen Zuordnungen; das Objekt kommt andernfalls vom Bankkonto, wenn ihm genau eines zugeordnet ist.
- **Regeln** (`MatchRule`, Tabelle `match_rules`, Verwaltung unter `/verwaltung/bank/regeln`): alle gesetzten Bedingungen müssen zutreffen (Gegen-IBAN, Name enthält, Zweck enthält, Richtung; Alternativen mit „;“). Aktion: *Ignorieren*, *Ausgabe* (Kostenart + Objekt) oder *Miete* eines Mietverhältnisses. Die Seite eines Umsatzes legt eine Regel mit vorbelegter IBAN/Name an.
- **Zuordnung** (`IPaymentAssignment`, `PaymentAssignment`; Tabelle `transaction_assignments`): `TransactionAssignment` verbindet einen Umsatz mit den erzeugten Datensätzen, je Teil entweder eine `Payment` (Herkunft `Bank`, Datum = Valuta, optional Zahlungsbestimmung auf eine `Charge`) oder eine `CostItem` samt `Receipt`. Alles geschieht in **einer** Transaktion; bei einem Fehler bleibt der Umsatz unverändert.
  - **Split:** `TransactionSplit.Resolve` – Teilbeträge positiv, höchstens zwei Nachkommastellen, höchstens ein leerer Teil (erhält den Rest), Summe = Umsatzbetrag auf den Cent, sonst Fehler mit Differenz. Teil- und Überzahlung bleiben dabei Sache der Verrechnung (`Allocation`): eine Teilzahlung lässt den Posten offen, eine Überzahlung wird Guthaben. Sammelzahlung: ein Teil je Forderung (Vorschlag bei „Miete Januar bis März“ bzw. Betrag = n älteste Monate) oder ein freier Teil, der nach Reihenfolge verrechnet wird.
  - **Ausgaben:** Kostenart, Objekt, optional Einheit, Leistungszeitraum (Standard: Leistungszeitraum des Belegs, sonst Kalenderjahr der Buchung), umlagefähig (Standard laut Kostenart). Ohne gewählten Beleg entsteht ein Beleg für den Umsatz (Lieferant = Gegenseite, `PaidOn` = Buchungsdatum, Datei später ergänzen); ein vorhandener Beleg nimmt die Positionen auf (Summenprüfung des Belegs gilt). Eine Gutschrift eines Lieferanten (Eingang) wird als negative Position gebucht, eine Auszahlung an Mieter (Ausgang) als negative `Payment`.
  - **Rückgängig** (`UnassignAsync`): entfernt (logisch) die Zahlungen, Positionen und Zuordnungen, nimmt `PaidOn` zurück, wenn die Zuordnung es gesetzt hat, löscht einen eigens angelegten Beleg, solange er leer und ohne Datei ist, und setzt den Umsatz auf *offen*. Ein ignorierter Umsatz muss erst wieder geöffnet werden (`ReopenAsync`).
  - **Audit:** kein eigener Mechanismus – `Transaction` (Statuswechsel), `TransactionAssignment`, `Payment`, `Receipt`, `CostItem` und `MatchRule` landen über den `EntitySaveInterceptor` im Änderungsprotokoll; die Umsatzseite zeigt den Verlauf.
- **Oberfläche:** `/verwaltung/bank/zuordnung` (Eingang mit bestem Vorschlag: Bestätigen / Ändern-Aufteilen / Ignorieren), `/verwaltung/bank/umsaetze/{id}/zuordnen` (Vorschläge, manuelles Formular mit beliebig vielen Teilen, Rückgängig, Verlauf), `/verwaltung/bank/regeln`. Filter „Zahlungen ohne Beleg“ bei den Umsätzen (`?ohneBeleg=true`): zugeordnete Ausgaben, deren Beleg noch keine Datei hat. Hinweis zur Formularbindung: Die Teile heißen `Parts[i].Feld`; Ids und Datumsfelder sind dort als Text modelliert (`AssignmentPartForm`), weil der Formular-Binder leere Werte dieser Typen in Listen ablehnt.
- **Tests:** Domain (`TextMatchingTests`, `PaymentMatcherTests` mit der **Abnahmemenge**: ein Jahr generierter Mietumsätze mit Namens-/IBAN-/Zweckvarianten, Teil- und Sammelzahlung, muss zu ≥ 90 % richtig vorgeschlagen werden, aktuell 96,8 %; `TransactionSplitTests`, `MatchRuleTests`), Integration (`PaymentAssignmentTests`), Web (`PaymentAssignmentPagesTests`).

### Mahnwesen (halbautomatisch)

- **Grundsatz:** Vorschlagen ja, versenden nur nach Freigabe. Der Job `dunning` (`DunningJob`, täglich 6:15) und die Startseite rufen nur `IDunning.RefreshAsync` auf; der Brief, die Datei, der Dokumenteintrag, die Buchungen und die Mail entstehen ausschließlich in `IDunning.IssueAsync`. Ein Test prüft, dass nach Job/Refresh weder Mail noch Datei noch Dokument existieren.
- **Reine Logik** (`Domain.Finance.Dunning`): `DunningPlanner.Plan(rules, today, claims, history)` liefert die fällige Stufe oder `null`. Mahnbar sind nur **Hauptforderungen** (`Charge.RankOf == PrincipalClaim`; Gebühren und Zinsen lösen keine Mahnung aus, Zins auf Zins entfällt), die mehr als `DunningGraceDays` überfällig sind und zusammen mindestens `DunningMinimumAmount` ergeben. Stufe: ohne frühere Freigabe die Zahlungserinnerung; sonst die nächste Stufe, wenn der älteste Rückstand schon bei der letzten Mahnung bestand und die Wartezeit (`DaysUntilFirstNotice` / `DaysUntilSecondNotice`) abgelaufen ist; nach der 2. Mahnung kein weiterer Vorschlag; neue Rückstände nach vollständiger Zahlung beginnen wieder mit der Erinnerung. Ein verworfener Vorschlag (`Dismissed`) pausiert 14 Tage (`SnoozeDays`).
- **Gebühr und Zinsen:** Gebühr je Stufe aus den Einstellungen (Standard 0, nur tatsächliche Kosten). Verzugszinsen ab der 1. Mahnung, wenn `ChargeDefaultInterest` und `BaseRate` gesetzt sind: einfache Zinsen, tagesgenau actual/365, `max(0, Basiszins + Aufschlag)` auf den **offenen** Betrag je Posten vom Tag nach der Fälligkeit bis zum Briefdatum (`DefaultInterest.Calculate/Total`). Bereits mit früheren Mahnungen berechnete Zinsen werden abgezogen. Der Basiszinssatz gilt als einzelner Wert für den ganzen Zeitraum (Näherung, kein Zinssatzverlauf).
- **Kündigungsrelevanz** (`TerminationRelevance.Check`): Hinweis, wenn für zwei aufeinanderfolgende Fälligkeitsmonate zusammen mehr als eine Monatsmiete offen ist oder der Mietrückstand zwei Monatsmieten erreicht (§ 543 Abs. 2 Nr. 3 BGB; Monatsmiete = Summe der Bestandteile einschließlich Vorauszahlungen). Nur Anzeige (Mahnwesen, Mieterkonto, Dashboard-Aufgabe, am Vorschlag gespeichert), keine Automatik.
- **Persistenz:** `Dunning` (Tabelle `dunnings`; Status `Proposed` → `Issued` / `Dismissed`; Stufe, Datum, Zahlungsfrist, Beträge, Datei, Dokument, Mail, gebuchte Forderungen) mit eindeutigem Teilindex „höchstens ein offener Vorschlag je Mietverhältnis“; `DunningHold` (Tabelle `dunning_holds`, Mahnsperre je Mietverhältnis mit Grund und optionalem Ende). Einstellungen liegen in `FinanceSettings` (Spalten `dunning_*`, `*_fee`, `base_rate*`, …), `FinanceSettings.DunningRules` bündelt sie für die Logik. Alles läuft über den `EntitySaveInterceptor`, ist also im Änderungsprotokoll; `DunningPersonalData` liefert den Abschnitt `dunnings` der DSGVO-Auskunft; der Vollexport nimmt die Tabellen automatisch auf.
- **Aktualisieren** (`RefreshAsync`): legt je Mietverhältnis mit Rückstand einen Vorschlag an, gleicht ihn an und entfernt ihn, wenn kein Rückstand mehr besteht (Zahlung), eine Mahnsperre gilt oder die Stufe nicht mehr fällig ist. Das Öffnen eines Vorschlags und `IssueAsync` prüfen den Rückstand erneut: ein bezahlter Vorschlag wird entfernt, es wird nichts versendet. `PeekAsync` liest ohne Neuprüfung (für den Formular-Rücklauf, sonst fände die Seite ihr Formular nicht mehr).
- **Freigabe** (`IssueAsync`, eine Transaktion): PDF über `IPdfGenerator` (Vorlage `Pdf/Templates/dunning.sbn`, Brief nach DIN 5008 an die Mieter; Anschrift der Person zum Stichtag, sonst das Mietobjekt) → Dateiablage → Dokumentenbibliothek (`DocumentCategory.Correspondence`, Bezug Vertrag, nicht freigegeben) → optional Sollstellungen `DunningFee` / `DefaultInterest` (fällig zur Zahlungsfrist; nur, was im Brief gefordert wird) → optional Mail über `IMailSender` (Vorlage `dunning`, PDF als Anhang, `Reference = dunning:<id>`) → Stufe, Datum und Frist am `Dunning` speichern. Ohne E-Mail-Adresse oder Vermieterprofil wird abgelehnt. Das PDF steht danach zum Download bereit (Postversand).
- **Vorlagen:** `dunning.sbn` (PDF) enthält den Text aller drei Stufen und ist eine **Basisvorlage, die rechtlich geprüft werden muss**; die Vorlagenverwaltung (M5) ersetzt sie später (`PdfRequest.TemplateText`). Der Text ist bewusst zurückhaltend (Aufforderung mit Frist, Hinweis auf Gegenstandslosigkeit bei Zahlung, kein Drohen mit Kündigung).
- **Oberfläche:** `/verwaltung/finanzen/mahnungen` (Vorschläge, freigegebene Mahnungen, Mahnsperren, Kündigungshinweise), `/verwaltung/finanzen/mahnungen/{id}` (Freigabe-Dialog mit Vorschau-PDF per `…/vorschau.pdf`, Auswahl Gebühr/Zinsen/E-Mail, „Vorschau aktualisieren“, „Freigeben“, „Verwerfen“; nach der Freigabe Download), Abschnitt *Mahnwesen* im Mieterkonto (Verlauf, Mahnsperre setzen/aufheben), Einstellungen unter *Einstellungen Finanzen*, Dashboard-Aufgaben „N Mahnvorschläge prüfen“ und „Rückstand mit Kündigungsrelevanz“. Recht **Finanzen**.
- **Tests:** Domain (`DunningTests`: Planung, Stufen, Zinsen, Kündigungsrelevanz, Sperre), Integration (`Finance/DunningTests` mit echtem Gotenberg: Vorschlag ohne Versand, Zahlung entfernt Vorschlag, Freigabe mit PDF-Text/Dokument/Gebühr/Mail, Eskalation mit Zinsen, Mahnsperre, Verwerfen, Job, Dashboard), Web (`DunningPagesTests`, PDF-Erzeugung durch einen Platzhalter ersetzt).

### Betriebskosten: Umlageschlüssel & Rechenkern (#28, #29)

- **Umlageschlüssel** (`Domain.OperatingCosts.CostAllocationRule`, Tabelle `cost_allocation_rules`): je Objekt und umlagefähiger Kostenart eine Zeitreihe (`TimeSeries`, DB-Überlappungsschutz auf Objekt + Kostenart). Schlüssel `AllocationKey`: Wohnfläche, Heizfläche, Personen (Personentage), Einheiten, Verbrauch (Zählertyp), direkte Zuordnung (`CostItem.UnitId`), fester Prozentsatz je Einheit (`cost_allocation_shares`, Rest beim Vermieter). Optional **Vorwegabzug** (Prozent oder Betrag pro Jahr, geht an den Vermieter). Änderungen gelten ab dem Stichtag; endet ein Schlüssel oder fehlt er, rechnet die Vorschau mit Wohnfläche und warnt. Heizkosten (BetrKV Nr. 4–6) haben keinen Schlüssel dieses Kerns.
- **Weitere Einstellungen:** `Unit.SharedFlatBilling` (WG: *Pauschale* = Zimmer ohne Abrechnung, Ist-Kosten werden intern gegen die Pauschale gestellt, Standard; *ByRoom* = Anteil der Wohnung nach Zimmerfläche inkl. Gemeinschaftsanteil auf die Zimmer). `Lease.ExcludedCostTypes`: im Vertrag nicht vereinbarte Kostenarten (Standard: alle), deren Anteil bleibt beim Vermieter. `CostSubtype` (Tabelle `cost_subtypes`) und `CostItem.SubtypeId`: eigene Unterarten einer Kostenart (z. B. Gebäudeversicherung unter Nr. 13), sie erscheinen am Beleg und im Protokoll.
- **Rechenkern** (`OperatingCostCalculator.Calculate(BillingInput, extraGroups?) → BillingResult`, rein, ohne Datenbank): Abrechnungszeitraum höchstens 12 Monate. Kostenpositionen werden nach ihrem Abrechnungszeitraum tagesanteilig in den Zeitraum geschnitten (z. B. Versicherung Juli–Juni). Je Kostenart wird der Zeitraum an Schlüsselwechseln geteilt; innerhalb eines Abschnitts wird zwischen allen Änderungen (Mietbeginn/-ende, Fläche, Personen) in Segmente zerlegt und je Einheit gewichtet (Fläche × Tage, Personentage, Einheitentage, Verbrauch je Segment). Leerstand und Leerstandszimmer sind eigene Parteien und gehen zum Vermieter; im Personenschlüssel zählt ein leeres Mietobjekt mit `BillingOptions.VacancyPersons` (1) Person. Nicht umlagefähige Positionen (`Allocable = false`) werden nur ausgewiesen.
- **Rundung:** intern volle Präzision, je Kostenart einmal auf Cent (`CentDistribution`, Restcent-Verfahren: abrunden, fehlende Cents an die größten Nachkommastellen, bei Gleichstand die frühere Partei: Verträge nach Mietbeginn, dann Leerstände). Es gilt immer Σ Mieteranteile + Vermieteranteil = Kosten der Kostenart (der Kern wirft sonst `InvalidOperationException`); die Tests prüfen das auch mit Zufallsdaten.
- **Vorauszahlungen:** `PrepaymentMonth(Monat, Komponente, Soll, Gezahlt)` aus dem Mieterkonto: `OperatingCostService` verrechnet Zahlungen wie das Konto (`TenantAccounts.Allocate`) und übergibt je Sollstellung den tatsächlich getilgten Betrag; Monate am Rand eines abweichenden Abrechnungszeitraums werden tagesanteilig gerechnet. Offene Vorauszahlungen erzeugen einen Hinweis. Heizkostenvorauszahlungen werden nicht mit den Betriebskosten verrechnet, sondern mit den Heizkosten (*Heizkosten & CO₂-Aufteilung*).
- **Rechenprotokoll:** jede Zeile eines Mieters trägt `ProtocolEntry`s (Titel, Formel, eingesetzte Werte, Ergebnis, Notiz mit den Zeiträumen und ggf. „interpolierter Zählerstand“), dazu Mietzeit, Vorauszahlungen, Ergebnis und § 35a-Anteile je Mieter.
- **Heizkosten einstecken:** Der Kern kennt `CostGroup` und `ICostGroupCalculator`. Positionen der Gruppe Heizung werden an einen registrierten Rechner übergeben (Ergebnis wird in Mieterzeilen, Kostenarten und Vermieteranteile eingemischt, mit eigener Vorauszahlungssaldo-Gruppe). Ohne Rechner stehen sie in `UncalculatedGroups`. Der Rechner ist der `HeatingCostCalculator` (siehe *Heizkosten & CO₂-Aufteilung*); `OperatingCostService` registriert ihn immer. `GroupResult` kann zusätzlich `FlatRateActuals` (Ist-Kosten nicht abgerechneter Verträge) und `Sections` (objektweite Erläuterungen für die Vorschau) liefern.
- **Dienste:** `ICostAllocation` (Schlüssel, WG-Modus, Vertragsvereinbarung), `ICostSubtypes`, `IOperatingCosts.PreviewAsync(Objekt, Jahr | Zeitraum)` (`OperatingCostService` baut aus Belegen, Schlüsseln, Verträgen, Mieterkonto und Zählern den `BillingInput`; Zähler über `MeterConsumptionSource` mit `ConsumptionCalculator`, Messstellen über Zählertausch hinweg; Zähler sind der Einheit zugeordnet, Zimmerzähler werden nicht ausgewertet). Es wird nichts gespeichert.
- **Oberfläche** (Recht Finanzen): `/verwaltung/objekte/{id}/umlageschluessel` (Schlüssel, Verlauf, Beenden, WG-Modus, Unterarten), `/verwaltung/objekte/{id}/nebenkosten?jahr=` (Vorschau: Kostenarten, Vermieteranteil, Abrechnung je Mieter mit Protokoll, Pauschalvergleich, fehlende Daten, Heizkosten und CO₂-Aufteilung), `/verwaltung/objekte/{id}/heizung` (Heizungseinstellungen), `/verwaltung/vertraege/{id}/kostenarten`. Abrechnungsassistent siehe *Abrechnungs-Assistent* (#32), PDF, Versand und Vorauszahlungsanpassung siehe *Abrechnungs-PDF & Versand* (#33).

### Abrechnungs-Assistent (#32)

- **Ablauf** (`/verwaltung/objekte/{id}/abrechnung?jahr=&schritt=1..6`, Recht Finanzen, ohne Zwischenspeicher: jeder Schritt rechnet neu aus den Stammdaten): 1. Zeitraum & Objekt (Kalenderjahr, Grenze der Vorjahresabweichung `abweichung`, Standard 20 %), 2. Checkliste Kosten, 3. Zählerstände, 4. Personen/Belegung, 5. Berechnung & Vorschau (inkl. Ist-Kosten gegen Pauschale), 6. Freigabe. Die Schrittnummern sind `Domain.Statements.WizardStep`.
- **Dienst:** `IStatements` (`StatementService`) baut auf `IOperatingCosts` auf: `PrepareAsync` rechnet den Zeitraum und das Vorjahr und liefert `StatementWizard` mit den Befunden (`PlausibilityFinding`: Schritt, Schwere `Info`/`Warning`/`Error`, Text, Kostenart). `ReleaseAsync`, `ListAsync`, `GetAsync` verwalten die freigegebenen Abrechnungen.
- **Plausibilitätsprüfungen:** rein in `PlausibilityChecker.Check(aktuell, vorjahr, Grenze)`: Kostenart im Vorjahr vorhanden, jetzt ohne Kosten (fehlender Beleg); Abweichung über der Grenze; neue Kostenart (Info); Schlüsselsumme (Mieter + Vermieter = Gesamtkosten, sonst Fehler); Kostenart im Vertrag nicht vereinbart; Vorauszahlungen gezahlt gegen Soll; alle Probleme des Rechenkerns (Fehler bleiben Fehler). Der Dienst ergänzt, was außerhalb des Ergebnisses liegt: Zähler im Zeitraum ohne Anfangs-/Endstand (interpolierte Stände als Info), Mietobjekte/Zimmer ohne Vertrag im Zeitraum (Lücke zwischen zwei Verträgen = Warnung, am Rand = Info), Festanteile ungleich 100 %, Hinweise zum Buchhaltungsbeginn.
- **Ist-Kosten vs. Pauschale:** Schritt 5 zeigt je Pauschalvertrag Ist-Kosten, gezahlte Pauschale, Differenz und einen Vorschlag je Monat (Differenz ÷ Monate des Zeitraums, aufgerundet) als Grundlage einer Erhöhung nach § 560 Abs. 1 BGB. Rein intern, es wird nichts gebucht.
- **Freigabe:** `BillingStatement` (Tabelle `billing_statements`) friert das Ergebnis als JSON (`json`, nicht `jsonb`, damit der Text byte-genau bleibt) ein. Es gibt keine ändernden Methoden. Fehler (`Error`) verhindern die Freigabe, Warnungen müssen bestätigt werden. Eine Korrektur ist eine neue Fassung (`Revision` + `SupersedesId`) mit Pflichtbegründung; unveränderte Daten erzeugen keine neue Fassung. Eindeutig je (Objekt, Zeitraum, Fassung) per Teilindex in der Migration, weil EF keine Indizes über Complex-Type-Spalten kann. Der Snapshot serialisiert `Period` über einen eigenen Konverter (nur `from`/`to`, die berechneten Eigenschaften wie `Days` scheitern bei offenen Zeiträumen).
- **Differenz-Abrechnung:** `StatementDifference.Between(alt, neu)` vergleicht die Salden je Vertrag. `ClaimsExcluded` ist wahr, wenn nach der Frist Nachforderungen entstehen (§ 556 Abs. 3 Satz 3 BGB); Guthaben bleiben fällig. Die Detailseite `/verwaltung/abrechnungen/{id}` zeigt Differenz und Hinweis; `/verwaltung/objekte/{id}/abrechnungen` listet alle Fassungen.
- **Frist (§ 556 Abs. 3 BGB):** `StatementDeadline.Of(zeitraum)` = Ende + 12 Monate, Erinnerung ab dem 1. des 10. Monats danach (1. Oktober), Stufen `Notice`/`Warning`/`Urgent` bei 6/4/2 Wochen, danach `Expired`. `StatementDeadlineTodos` (Dashboard „Abrechnung“) prüft das Vorjahr und das Jahr davor für Objekte mit Vorauszahlungsverträgen, solange keine Abrechnung freigegeben ist; Dringlichkeit Info → Soon → Urgent. Der Assistent zeigt dieselbe Frist als Hinweis.

### Abrechnungs-PDF & Versand (#33)

- **Dienst:** `IStatementDispatch` (`StatementDispatchService`) arbeitet auf einer freigegebenen Fassung (`StoredStatement`) je Mietvertrag. `PreviewAsync` rendert nur (Endpunkt `/verwaltung/abrechnungen/{id}/mieter/{leaseId}/vorschau.pdf`, `no-store`), `DispatchAsync` erzeugt das Schreiben und protokolliert den Versand als `StatementDispatch` (Tabelle `statement_dispatches`, je Fassung und Vertrag einmal). Nur die **aktuelle** Fassung lässt sich versenden.
- **Ablauf von `DispatchAsync`** (eine Transaktion, wie beim Mahnwesen): PDF über `IPdfGenerator` (Vorlage `Pdf/Templates/statement.sbn`, DIN 5008 mit Anschrift aus `LetterParts`) in die Dateiablage, Dokument (Kategorie `Statement`, `SharedWithTenants = true`, damit es im Mieterportal erscheint), Sollstellung, bei E-Mail die Outbox-Mail (Vorlage `statement`, Referenz `statement:<id>:<leaseId>`, PDF im Anhang), dann `StatementDispatch` und Vorauszahlungsvorschläge.
- **Buchung:** `Amounts(Saldo, früher gebucht, nach Frist)`: gebucht wird der Saldo abzüglich dessen, was frühere Fassungen derselben Abrechnung schon gebucht haben (Korrektur = Differenz). Positive Beträge als `ChargeType.AdditionalPayment`, negative als `Refund`, fällig `Statements:PaymentDays` nach dem Versand. Nach dem 31.12. des Folgejahres wird eine Nachzahlung **nicht** gebucht (`ClaimExcluded`, Vermerk im Protokoll und im Schreiben), ein Guthaben bleibt.
- **Zugangsnachweis:** Bei E-Mail ist die gespeicherte Mail (`OutgoingMail`, Status, Zeitpunkt) der Nachweis des Versands, bei Postversand gilt das eingetragene Versanddatum. Zusätzlich lässt sich der Zugang mit Datum und Bemerkung (z. B. Einwurf-Einschreiben) bestätigen (`ConfirmReceiptAsync`, nicht vor dem Versand).
- **Vorauszahlungsanpassung (§ 560 Abs. 4 BGB):** `PrepaymentCalculator.Propose` = Kosten des Mieters, auf zwölf Monate hochgerechnet (bei Teilzeiträumen), ÷ 12 × (1 + Sicherheitszuschlag), auf volle Euro aufgerundet, getrennt für Betriebs- und Heizkosten. Wirksam ab dem 1. des übernächsten Monats nach dem Versand (im Versanddialog änderbar). Der Vorschlag steht im selben PDF und als `PrepaymentProposal` (Tabelle `prepayment_proposals`) offen; erst `ApplyProposalAsync` legt über `ILeaseService.SetComponentAsync` einen neuen Mietbestandteil an (Betrag änderbar), `DeclineProposalAsync` verwirft ihn. Kein Vorschlag bei beendeten Verträgen oder unverändertem Betrag.
- **Anlagen im selben PDF:** Heizkostenabrechnung (nur wenn der Vertrag Heizkosten hat) mit Zählerständen Anfang/Ende der Zähler der Wohnung (bzw. des Zimmers, aus den Ablesungen am Tag vor Beginn und am Ende) und den Abschnitten der Heizkostenrechnung (Aufteilung Heizung/Warmwasser, CO₂-Stufe mit Vermerk „Stand/prüfen“), danach die **§ 35a-Bescheinigung** (haushaltsnahe Dienstleistungen und Handwerkerleistungen, nur wenn Arbeitskosten anfallen).
- **Gesamtübersicht für den Vermieter:** Abschnitt auf der Detailseite: Kosten, auf Mieter umgelegt, Vermieteranteil (Leerstand, nicht vereinbart, Vorwegabzug), Nachzahlungen und Guthaben, Positionen des Vermieters, Pauschale (Ist-Kosten gegen Einnahmen).

**Checkliste der formellen Anforderungen** (Abgleich mit `statement.sbn`; der Integrationstest `Letter_contains_the_formal_requirements_and_dispatch_books_mails_and_shares` prüft den Text des erzeugten PDFs):

| Anforderung (BGH, § 259 BGB, § 556 BGB) | Umsetzung im Schreiben |
|---|---|
| Abrechnungszeitraum und Zeitraum der Mietzeit | Einleitung: „vom … bis zum …“, Mietzeit und Tage |
| Zusammenstellung der **Gesamtkosten je Kostenart** | Tabelle „Betriebskosten“, Spalte Gesamtkosten |
| Angabe und **Erläuterung des Verteilerschlüssels** | Spalte „Verteilerschlüssel“ (z. B. Wohnfläche), „Ihr Anteil am Schlüssel“ (z. B. 21.900 von 21.900 m²·Tage) und Erläuterungstext unter der Tabelle |
| **Berechnung des Mieteranteils** | Spalte „Ihr Anteil“, Summe der Betriebskosten (Rechenschritte im Rechenprotokoll des Systems) |
| **Abzug der Vorauszahlungen** | Zeilen „abzüglich geleisteter Vorauszahlungen“ (gezahlt, Soll in Klammern) |
| **Saldo** (Nachzahlung/Guthaben) | Zeile „Nachzahlung/Guthaben“ und Abschnitt „Ergebnis“ |
| Nicht umlagefähige/nicht vereinbarte Kosten erkennbar | Hinweiszeile bei nicht vereinbarten Kostenarten; Vermieteranteil bleibt außerhalb |
| **Heizkosten** nach HeizkostenV/CO₂ | Abschnitt „Heiz- und Warmwasserkosten“ und Anlage mit Zählerständen |
| Hinweis auf **Belegeinsicht** | Abschnitt „Belegeinsicht“ (inkl. Einwendungsfrist § 556 Abs. 3 Satz 5) |
| **Zahlungsziel**, Erstattungsweg, Bankverbindung | Satz unter „Ergebnis“ mit Fälligkeit, IBAN/BIC, Verwendungszweck; bei Guthaben Erstattungshinweis |
| § 35a EStG | Anlage „Bescheinigung nach § 35a EStG“ |
| Anpassung der Vorauszahlung | Abschnitt „Anpassung Ihrer Vorauszahlungen (§ 560 Abs. 4 BGB)“ |
| Korrektur erkennbar | „Korrigierte Abrechnung (Fassung n)“ mit Grund, Differenz zur bisherigen Fassung |

### Heizkosten & CO₂-Aufteilung (#30, #31)

Fachliche Festlegungen und Standardwerte (70/30, Gas, Wärmezähler im Warmwasserkreis, getrennte Zähler) stehen in `docs/fachlichkeit.md`. **Alle Rechtstabellen sind versionierte Daten mit dem Status „Stand/prüfen“** und werden so in Oberfläche und Warnungen gezeigt.

- **Rechenkern** (`Domain.Heating`, rein): `HeatingCostCalculator(HeatingParameters)` implementiert `ICostGroupCalculator`. Ablauf in `HeatingRun` (aufgeteilt in `HeatingRun.cs` Kosten/Brennstoff/§ 9, `HeatingRun.Parties.cs` Gewichte je Mieter und Leerstand, `HeatingRun.Distribution.cs` Verteilung, CO₂ und Zusammenfassung):
  1. Kostenpositionen tagesanteilig in den Zeitraum schneiden; `BillingCost` trägt dazu `HeatingKind`, `ReceiptId`, `FuelQuantity`, `FuelUnit`, `Co2Kg`. Die Belegdaten (Menge, CO₂) zählen **einmal je Beleg**, anteilig wie die Position, von der sie gelesen werden.
  2. Brennstoffenergie: Gas/Sonstiges = Σ Menge laut Beleg in kWh (`Einheit`: kWh, MWh; sonst Faktor aus den Einstellungen), Öl = `OilConsumptionCalculator` (Anfangsbestand + Lieferungen − Endbestand; gewichteter Durchschnitt oder FIFO; die Brennstoffkosten werden durch die bewertete Verbrauchsmenge ersetzt).
  3. § 9: Anteil Warmwasser = Wärmemenge Warmwasser ÷ Brennstoffenergie (Zähler `ConsumptionQuantity` aus `HotWaterHeat`, sonst Formel mit Σ Warmwasser-m³ der Einheiten), auf 100 % begrenzt. Kostenart 6 (und 4 bei `JointPlant`) wird geteilt, 5 geht ganz zum Warmwasser.
  4. Verteilung je Pool: Verbrauchsanteil × Verbrauch (Wärme kWh bzw. Warmwasser m³) + Rest × Fläche × Tage (Heizfläche bzw. Wohnfläche). Rundung wie im Kern mit `CentDistribution` (Restcent); es gilt Σ Mieter + Vermieter = Pool, sonst `InvalidOperationException`.
  5. Mieterwechsel: Verbrauch je Nutzerblock (Mietzeit/Leerstand) aus `IConsumptionSource.Quantity`. Ist die Ablesung am Wechseltag interpoliert (`ConsumptionQuantity.InterpolatedDays`), wird der Verbrauch der ganzen Periode nach Gradtagszahlen (`DegreeDayTables`, Heizung) bzw. Tagen (Warmwasser) verteilt. WG-Wohnungen (Zimmer): Verbrauch der Wohnung nach Zimmerfläche und Tagen.
  6. CO₂ (`CarbonCostCalculator`, `CarbonCostTables`): Einstufung aus kg ÷ m² Wohnfläche (tagesgewichtet) ÷ Jahr; Abzug je abgerechnetem Mieter = Anteil an den CO₂-Kosten × Vermieteranteil, Σ auf Cent gerundet und per Restcent verteilt. Ergebnis: Kostenart `heating-co2` (Summe 0, Mieter negativ, Vermieter positiv, Grund `CarbonCostShare`) und je Mieter eine negative Zeile.
  7. Ergebniszeilen: Kostenarten `heating-heat`, `heating-hotwater`, `heating-co2` (Konstanten am `HeatingCostCalculator`); Rechenprotokoll je Mieterzeile; `Sections` `heating` und `carbon`.
- **Versionierte Tabellen:** `CarbonCostTables.All` (Version `CO2KostAufG-2023`, gilt ab Abrechnungsjahr 2023, 10 Stufen) und `DegreeDayTables.All` (`2026-stub`, **Platzhalter**). Neue Fassungen werden als weiterer Eintrag mit `ValidFromYear` ergänzt; `Verified` wird erst nach Prüfung gegen die Quelle auf `true` gesetzt.
- **Daten:** `HeatingSettings` (Tabelle `heating_settings`, je Objekt eine Zeile: Energieträger, Verbrauchsanteil 50–70, gemeinsame Anlage, Wärmezähler Warmwasserkreis, Formelfaktor/-temperatur, kWh je Einheit, Tankzähler, Ölbewertung, Preis Anfangsbestand, CO₂-Aufteilung, Sonderfall). Ohne gespeicherte Einstellungen gelten die Standardwerte. `CostItem.HeatingKind` (Spalte `heating_kind`, nur bei Kostenarten 4 bis 6) ordnet die Position zu (Brennstoff, Betriebsstrom, Wartung, Abgasmessung, Reinigung, Zählermiete, CO₂-Kosten, Sonstige); ohne Angabe gilt ein Beleg mit Brennstoffmenge als Brennstoff, sonst als Sonstige Kosten. Die CO₂-Kosten laut Lieferant werden als **eigene Position** (Art „CO₂-Kosten“) am Brennstoffbeleg erfasst; Menge und CO₂ in kg stehen am Beleg.
- **Dienste:** `IHeatingSettings` (`HeatingSettingsService`: Lesen mit Standardwerten und Zählerauswahl, Speichern mit Prüfung 50–70 % und Zählertyp/Objekt). `OperatingCostService` baut die `HeatingParameters` (Wärmezähler Warmwasserkreis über `MeterConsumptionSource.MeterQuantity`, Tankbestand aus den Ablesungen des Tankzählers zum Tag vor Beginn und zum Ende) und registriert den Rechner immer. Zählerwerte liefert `MeterConsumptionSource` jetzt in kWh bzw. m³ (MWh und Liter werden umgerechnet) und mit den interpolierten Tagen. `BillingLease.ReceivesHeatingStatement` (aus `Lease.ReceivesHeatingCostStatement`): Verträge mit Pauschale und „Heizkosten verbrauchsabhängig“ erhalten eine Heizkostenabrechnung ohne Betriebskosten.
- **Oberfläche:** `/verwaltung/objekte/{id}/heizung` (`HeatingPage`, Recht Finanzen; Hinweis „Stand/prüfen“), Vorschau mit Abschnitten „Heiz- und Warmwasserkosten“ und „CO₂-Kostenaufteilung“, Heizkostentabelle je Mieter mit Verrechnung der Vorauszahlungen, „Art der Heizkosten“ am Kostenposition-Formular.
- **Tests:** Domain (`HeatingGoldenMasterTests`: Musterbeispiel im Stil der Fachliteratur mit erfundenen Zahlen, von Hand gerechnet; `HeatingTenantChangeTests`: Mieterwechsel mit Zwischenablesung bzw. Gradtagszahlen, Warmwasser nach Tagen; `CarbonCostTests`: jede Stufengrenze, Sonderfälle, Jahresumrechnung; `HeatingRulesTests`: Öl (Durchschnitt/FIFO), § 12, Leerstand, WG, Einstellungen, Zufallstest), Integration (`OperatingCosts/HeatingCostServiceTests`), Web (`HeatingPagesTests`).
- **Tests:** Domain (`OperatingCostGoldenMasterTests`: Haus mit vier Einheiten, Mieterwechsel 15.6., zwei Monate Leerstand, WG mit drei Zimmern als Pauschale, Schlüsselwechsel zum 1.7., jahresübergreifende Versicherung, direkte Kosten, nicht umlagefähige Kosten und Heizung ausgenommen, alles von Hand gerechnet mit den Rechenschritten als Kommentar; `OperatingCostCalculatorTests`, `CostAllocationRuleTests`, Zufallstests), Integration (`OperatingCosts/OperatingCostServiceTests`), Web (`OperatingCostPagesTests`).

### AfA & Abschreibungsplan (#36)

- **Rechenkern:** `Domain.Tax.DepreciationCalculator` ist eine reine Funktion (`BuildingAcquisition`, `SubsequentCost`, `MovableAsset` → `DepreciationPlan` mit AfA und Restbuchwert je Jahr und Warnungen). Der Satz kommt aus `StandardRate(Baujahr)` oder dem Überschreibwert; Monate im Anschaffungsjahr: `13 − Monat`.
- **Daten:** `PropertyAcquisition` (eine je Objekt, Tabelle `property_acquisitions`), `PropertyCost` (nachträgliche Kosten, `IsMaintenance` = zählt für die 15-%-Grenze) und `PropertyAsset` (bewegliche Wirtschaftsgüter).
- **Dienst:** `IDepreciationService` liefert Daten und Plan (bis zum laufenden Jahr, mindestens fünf Jahre nach dem Kauf).
- **Oberfläche:** `/verwaltung/objekte/{id}/afa` (Recht Finanzen), erreichbar über „AfA“ in den Objektdetails.
- Die Anlage-V-Auswertung (#39) übernimmt die AfA je Jahr aus diesem Plan.

### Darlehen & Tilgungsplan (#37)

- **Rechenkern:** `Domain.Tax.LoanCalculator.Calculate(LoanTerms, Sondertilgungen)` ist eine reine Funktion. Zinsen je Periode auf die Restschuld nach 30E/360 (`Days360`; ein voller Monat = 1/12 des Jahreszinses, eine angebrochene erste Periode tagesanteilig), auf Cent kaufmännisch gerundet; der Rest der festen Rate tilgt, die letzte Rate ist kleiner. Sondertilgungen mindern die Restschuld nach der Rate der Periode, in die sie fallen. Deckt die Rate die Zinsen nicht, entsteht kein Plan, sondern eine Warnung. `InstalmentFor` rechnet aus Anfangstilgung (% p. a.) die Rate.
- **Daten:** `Loan` (Tabelle `loans`, ein Objekt, Anteil Vermietung in %), `LoanExtraPayment`, `LoanCertificate` (Jahreszinsbescheinigung, eindeutig je Darlehen und Jahr) und `FinancingCost` (Disagio, Bereitstellungszinsen, Grundschuldbestellung, Sonstiges).
- **Dienst:** `ILoanService`. Eine Bescheinigung ersetzt Zins und Restschuld des Plans für ihr Jahr (`LoanYearInterest.Interest`). `ExpensesAsync(Objekt, Jahr)` liefert die abziehbaren Finanzierungskosten (Zinsen × Anteil Vermietung + Einmalkosten im Zahlungsjahr); die Anlage-V-Auswertung (#39) übernimmt sie von dort.
- **Oberfläche:** `/verwaltung/finanzen/darlehen` (Liste, Anlegen) und `/verwaltung/finanzen/darlehen/{id}` (Jahresübersicht, Daten, Bescheinigung, Sondertilgungen, Finanzierungskosten, Tilgungsplan), Recht Finanzen.
- **Zinsbindung:** Das Ende steht als Frist („Darlehen“, 180 Tage Vorlauf) im Fristenkalender (`LoanDeadlineSource`).
- **Bank-Tilgungsplan (CSV):** `ILoanService.PreviewBankPlanAsync`/`ImportBankPlanAsync` (`BankPlanCsv`): Spalten werden an der Kopfzeile erkannt (Datum/Fälligkeit, Zinsen/Zinsanteil, optional Restschuld/Restkapital; Trenner `;`, Tab oder `,`, UTF-8 oder Windows-1252, „Summe“-Zeilen werden übersprungen). Zinsen werden je Kalenderjahr summiert, die letzte Restschuld des Jahres übernommen; die Werte landen als Jahreszinsbescheinigungen (ersetzen gleiche Jahre, haben Vorrang vor dem berechneten Plan). Warnung bei Jahren mit auffällig anderer Ratenzahl. Vorschau vor dem Übernehmen auf der Darlehensseite.
- **Raten-Umsätze:** Auf der Zuordnungsseite eines Ausgangs gibt es für jeden Teil „Darlehensrate (statt Kostenposition)" (`AssignmentPartInput.LoanId`, `TransactionAssignment.LoanId`/`LoanInterest`). Der Zinsanteil kommt aus der Plan-Rate, die dem Buchungsdatum am nächsten liegt (`LoanCalculator.InterestAround`, höchstens 20 Tage Abstand), oder wird selbst eingegeben (erforderlich, wenn der Plan keine Rate in der Nähe hat); der Rest ist Tilgung. Nur Ausgänge, Zins 0 bis Betrag. Rückgängig machen wie bei allen Zuordnungen. Die Darlehensseite listet die „Gebuchten Raten“ mit Summen als Gegenprobe zu Plan und Bescheinigung; sie ändert die Jahreszinsen nicht (Bescheinigung/Plan bleiben maßgeblich).
- **Noch offen aus #37:** Darlehen über mehrere Objekte verteilt.

### Erhaltungsaufwand (#38)

- **Kennzeichnung:** Belegpositionen der Kostenart `CostTypes.MaintenanceKey` („Instandhaltung und Reparaturen“) sind Erhaltungsaufwand; ein eigenes Feld gibt es nicht. Positionen ohne Verteilung werden im Jahr der Zahlung (`Receipt.PaidOn`) sofort abgezogen.
- **Rechenkern:** `Domain.Tax.MaintenanceCalculator.Schedule(Betrag, Startjahr, Jahre)` verteilt gleichmäßig; der Rundungsrest fällt ins erste Jahr, jede Rate trägt den Restbetrag.
- **Daten:** `MaintenanceDistribution` (Tabelle `maintenance_distributions`, eindeutig je Position) mit Startjahr (Zahlungsjahr), Betrag und 2–5 Jahren (§ 82b EStDV).
- **Dienst:** `IMaintenanceService` – `GetAsync` (Positionen, Verteilungen), `DistributeAsync`, `RevokeAsync`, `DeductionAsync(Objekt, Jahr)` (abziehbare Anteile mit Beleg-Id; die Anlage-V-Auswertung #39 übernimmt sie von dort). Unbezahlte Positionen lassen sich nicht verteilen.
- **Abgrenzung:** `DepreciationService` zählt Belegpositionen „Instandhaltung“ zusätzlich zu den manuell erfassten Kosten für die 15-%-Prüfung mit (Datum: Zahlung, sonst Rechnung). Manuelle Kosten also nur eintragen, wenn es keinen Beleg gibt.
- **Oberfläche:** `/verwaltung/objekte/{id}/erhaltungsaufwand` (Positionen, Verteilen/Widerrufen, laufende Verteilungen mit Jahres- und Restbeträgen), Recht Finanzen.

### Anlage V (#39)

- **Rechenkern:** `Domain.Tax.AnlageVCalculator.Calculate(Posten, Zeilennummern)` summiert die `AnlageVPosting`s je `AnlageVCategory` (Einnahmen: Kaltmiete, Umlagen/Vorauszahlungen, NK-Nachzahlungen, Erstattungen, Sonstiges; Werbungskosten: AfA, Schuldzinsen, Finanzierungskosten, Erhaltungsaufwand, Grundsteuer, umlagefähige Betriebskosten, Versicherungen, Verwaltung, Fahrtkosten, Kontoführung, Sonstiges). `AnlageVCategories` ordnet Kostenarten (`ForCostType`) und Forderungen (`ForCharge`) den Kategorien zu; `ReducedRentHint` warnt bei verbilligter Vermietung (< 50 % bzw. 50–66 % der ortsüblichen Miete, § 21 Abs. 2 EStG).
- **Zuflussprinzip:** `CashBasisYear.YearOf(Zahlung, Fälligkeit, wiederkehrend)`: Regelmäßig wiederkehrende Zahlungen, die beide zwischen 22.12. und 10.1. um denselben Jahreswechsel liegen, gehören zum Jahr der Fälligkeit (§ 11 Abs. 1 Satz 2 EStG), alles andere zum Zahlungsjahr. Wiederkehrend sind Mieten und Vorauszahlungen sowie Kostenarten Grundsteuer, Versicherung, Verwaltung, Kontoführung, Zinsen.
- **Quellen:** `AnlageVService` – Einnahmen aus den Zahlungen der Mietverträge des Objekts, aufgeteilt auf die Forderungen, die sie nach `TenantAccounts.Allocate` begleichen (Kaution und Saldovortrag zählen nicht; Auszahlungen an Mieter sind Erstattungen); Ausgaben aus `Receipt.PaidOn` (unbezahlte Belege fehlen und lösen einen Hinweis aus); AfA aus `IDepreciationService`, Zinsen und Einmalkosten aus `ILoanService.ExpensesAsync` (ohne 10-Tage-Regel, Jahressumme), Erhaltungsaufwand aus `IMaintenanceService.DeductionAsync`. Die Kostenarten „Modernisierung“ (AfA) und „Leerstand“ (keine Zahlung) zählen nicht als Ausgabe. Der CO₂-Vermieteranteil ist in den bezahlten Heizkosten enthalten und hat keine eigene Zeile.
- **Zeilenzuordnung:** `TaxLineMapping` (Tabelle `tax_line_mappings`, eindeutig je Steuerjahr und Kategorie). Ohne gepflegten Wert gilt die Vorbelegung `AnlageVCategoryInfo.DefaultLine`; diese Nummern sind Platzhalter und am amtlichen Vordruck des Jahres zu prüfen.
- **Oberfläche:** `/verwaltung/objekte/{id}/anlage-v?jahr=&entgelt=` (Bildschirm mit Drilldown: jeder Posten verlinkt auf Beleg, Mieterkonto, AfA-Plan, Erhaltungsaufwand oder Darlehen), `.../anlage-v.csv` (Zeile; Bezeichnung; Betrag, Formel-Schutz wie `OpenItemsEndpoint`), `.../anlage-v.pdf` (Vorlage `anlage-v.sbn`), Pflege unter `/verwaltung/finanzen/anlage-v-zeilen?jahr=`. Recht Finanzen.
- **Verbilligte Vermietung:** `LocalRent` (Tabelle `local_rents`, ortsübliche Jahresmiete inkl. Umlagen je Objekt und Jahr, eindeutig) wird auf der Anlage-V-Seite gepflegt. Der Anteil der tatsächlichen Miete (Kaltmiete + Umlagen) daran bestimmt `AnlageVCalculator.DeductibleShare`: ab 66 % voller Abzug; unter 50 % anteilige Kürzung der Werbungskosten; von 50 % bis unter 66 % anteilige Kürzung, außer die Totalüberschussprognose ist positiv (Parameter `prognose`, Kästchen auf der Seite). Die Kürzung steht als eigene Zeile in Bildschirm, CSV und PDF (`AnlageVResult.Reduction`); die Zeilen selbst zeigen die vollen Beträge.
- **Noch offen aus #39:** DATEV-Export siehe Abschnitt *DATEV-Export (#40)*, die Totalüberschussprognose selbst wird nicht berechnet.

### Personen & Datenschutz

- `Person` (Mieter, Bürgen, Kontakte) mit Anschriften als Zeitreihe (Voranschrift → aktuelle → neue Anschrift nach Auszug) und beliebig vielen `BankDetails`en (IBAN mit Prüfsumme, `Domain.Common.Iban`). Mieter und Bürgen ergeben sich aus Verträgen, nicht aus einem Feld.
- **Erweiterungspunkte** für Fachmodule: `IPersonReference` (Rolle, Beschreibung, aktiv, Ende) steuert Rollenanzeige, Löschsperre und Aufbewahrungsfrist; `IPersonalDataSource` liefert einen Abschnitt für die DSGVO-Auskunft. Ein neues Modul, das Personendaten speichert, muss beide registrieren.
- **Auskunft** (`IPrivacyService.AccessReportAsync`, `/verwaltung/personen/{id}/auskunft`): JSON mit den serialisierten Entitäten (neue Felder sind automatisch enthalten), logisch gelöschten Einträgen, Konto, Bezügen, Änderungsprotokoll im Klartext und den Abschnitten der Module. Der Abruf steht im Audit-Log.
- **Anonymisieren**: entfernt Name, Kontaktdaten, Anschriften, IBANs (physisch, nicht nur logisch) und die Werte im Änderungsprotokoll. Der Datensatz bleibt für Buchungen erhalten. Vorschläge nach `Privacy:RetentionYears` (10) ab Ende des letzten Bezugs bzw. `Privacy:MonthsWithoutRelation` (12) ohne Bezug; Geschäftskontakte werden nicht vorgeschlagen.
- **Zugriffsprotokoll**: Öffnen einer Personenakte wird protokolliert, derselbe Zugriff (Benutzer, Datensatz, Zweck) höchstens einmal je 30 Minuten.

### Fristen- & Wartungskalender (#46)

- **Quellen** (`IDeadlineSource`, je Fachmodul eine Klasse in `Infrastructure/Deadlines/DeadlineSources.cs`, registriert in `DependencyInjection`): `StatementDeadlineSource` (NK-Abrechnung, 31.12. des Folgejahres, solange keine Abrechnung freigegeben ist; Vorlauf 90 Tage), `LeaseDeadlineSource` (Ende von Befristung/Kündigung, ungeanwendete Staffeln, frühester Termin einer Mieterhöhung bzw. Indexanpassung), `LoanDeadlineSource` (Zinsbindungsende, Vorlauf 180 Tage), `CalibrationDeadlineSource` (Eichfristen), `DocumentDeadlineSource` (Dokumente mit „gültig bis“, z. B. Energieausweis, Versicherung) und `TaskDeadlineSource` (von Hand eingetragene Wartungen). Neue abgeleitete Fristen sind nur eine weitere Quelle; sie brauchen keine Eingabe. Jeder Eintrag (`DeadlineEntry`) hat einen stabilen `Key`, ein Recht (`AccessRight`) und optional einen eigenen Vorlauf.
- **Wartungen** (`RecurringTask`, Tabelle `recurring_tasks`; `TaskCompletion` = Erledigung mit optionalem Dokument als Nachweis): Art (Rauchmelder, Heizungswartung, Schornsteinfeger, Trinkwasser, Dachrinnen, Spielplatz, Versicherung, Sonstiges), optional ein Objekt, Abstand in Monaten (0 = einmalig). `Complete` hält den Rhythmus: die nächste Fälligkeit ist das erste Folgedatum nach dem Erledigungsdatum, versäumte Termine werden übersprungen; einmalige Wartungen werden geschlossen.
- **Erinnerungen** (`DeadlineReminders`, Job `deadline-reminders` täglich 7:30): eine Mail an die aktiven Admins (`deadline-reminder.sbn`) mit allen Fristen, deren Vorlauf (je Frist oder `DeadlineSettings.DefaultLeadDays`, 30) begonnen hat und für die noch keine Mail ging. Gemerkt wird das in `deadline_reminders` (`Key` + Fälligkeit, eindeutig, ohne Audit). Montags zusätzlich die Wochenübersicht (`deadline-summary.sbn`: Überfälliges und die nächsten 30 Tage). Beides ist in den Einstellungen abschaltbar.
- **iCal-Feed:** `/kalender/{token}.ics` (anonym, `no-store`), Token aus 24 Zufallsbytes (`DeadlineSettings.CalendarToken`, nicht im Export, im Audit maskiert); ein neuer Link macht den alten ungültig, „abschalten“ löscht das Token. Inhalt: alle Fristen der nächsten 12 Monate plus Überfälliges als Ganztagstermine (`ICalendarWriter`, RFC 5545, gefaltet auf 75 Oktette).
- **Oberfläche:** `/verwaltung/fristen` (Liste mit Zeitraum 30/90 Tage/12 Monate, Monatsansicht; Fristen nach Recht des Benutzers gefiltert), `/verwaltung/fristen/wartung` (Liste, Eintragen) und `/verwaltung/fristen/wartung/{id}` (als erledigt eintragen mit Nachweis, Verlauf, Angaben, Löschen), `/verwaltung/einstellungen/fristen` (Vorlauf, Mails, Kalender-Abo; Recht System). Das Dashboard zeigt fällige Wartungen (7 Tage, überfällig dringend); die anderen Fristen stehen dort bereits über ihre eigenen Aufgaben.
- **Offen aus dem Issue:** „Aus einer Wartung ein Ticket für den Hausmeister/Handwerker erzeugen“ folgt mit dem Ticketsystem (M6, #49).
- **Tests:** Domain (`RecurringTaskTests`: Rhythmus, einmalig, Eingaben; `ICalendarWriterTests`: Format, Escapen, Falten), Integration (`DeadlineTests`: abgeleitete Fristen ohne Eingabe, Rechtefilter, Erledigung mit Nachweis, Erinnerung zum Vorlauf nur einmal, abschaltbar, Wochenübersicht nur montags, Feed mit geheimem Link), Web (`DeadlinePageTests`, Seiten im `PageSmokeTests`).

### Mieterportal (#48)

- **Zugang und Zuordnung:** Rolle *Mieter*, Policy `Policies.Portal`, Anmeldung auch per Magic Link. Das Konto gehört zu einer `Person` (`Person.UserId`); deren Mietverträge (`LeaseParty` mit Rolle Mieter) bestimmen alles, was das Portal zeigt. `ITenantPortal` (`Infrastructure/Portal/TenantPortal.cs`) leitet die Person immer aus dem angemeldeten Konto ab und vertraut keiner Id aus der Anfrage: ein fremder oder unbekannter Vertrag liefert `null`, die Seite antwortet mit 404. Konten ohne Person (oder andere Rollen) sehen nichts.
- **Seiten** (statisch, Layout `PortalLayout` mit Tabs): `/portal` (Übersicht: neue Dokumente, Mietverhältnisse, Ansprechpartner = Vermieterprofil und Hausmeister der Objekte), `/portal/dokumente`, `/portal/vertrag[/{id}]` (nur Ansicht: Laufzeit, Bestandteile mit Summe, Abrechnungsart, Fälligkeit, Kaution; bei genau einem Vertrag direkt dorthin), `/portal/kontakt`. Aushänge und Nachrichten stehen im Abschnitt *Aushänge & Nachrichten*; Meldungen (Tickets) und der Notdienst-Hinweis (Vermieterprofil, `EmergencyInfo`) stehen auf der Übersicht.
- **Dokumente:** Es gelten die bestehenden Freigaben (`DocumentShares`, Abschnitt *Dokumente*). Öffnet ein Mieter die Datei (`/dateien/{id}`), merkt `MarkReadAsync` das in `DocumentRead` (Tabelle `document_reads`, eindeutig je Dokument und Benutzer, nur für Dokumente, die für genau dieses Konto freigegeben sind). Der Status „gelesen“ steht im Portal und am Dokument in der Verwaltung („Geöffnet im Portal“); er ist ein Zugangsindiz, kein Zustellnachweis.
- **Mail bei neuem Dokument:** `DocumentNotifications` (Job `document-notifications`, alle 10 Minuten) mailt Mietern mit Portalkonto und E-Mail-Adresse, wenn ein für Mieter freigegebenes Dokument neu ist (`Document.TenantsNotifiedAt` leer; Vorlage `document-shared.sbn`, nur Titel und Link, keine Datei). Dokumente, die der Mieter ohnehin per Mail bekommt (Abrechnungsversand, Schreiben mit Mailversand), setzen `DocumentInput.TenantsInformedElsewhere` und werden nicht noch einmal angekündigt. Die Migration markiert bereits freigegebene Dokumente als angekündigt.
- **Kontaktdaten ändern:** Der Mieter beantragt E-Mail, Telefon, Mobil (`ContactChangeRequest`, Tabelle `contact_change_requests`; ein neuer Antrag ersetzt den offenen). Die Daten der Person ändern sich erst, wenn die Verwaltung unter `/verwaltung/personen/aenderungen` (Recht *Stammdaten*, Dashboard-Aufgabe „Änderung der Kontaktdaten bestätigen“) übernimmt (`IContactChangeRequests`) oder ablehnt. Die Anmelde-Adresse des Kontos bleibt unberührt.
- **Tests:** Integration (`TenantPortalTests`: nur eigene Verträge, fremde Id, andere Rollen, Gelesen-Status, Änderungsantrag, Mail genau einmal), Web (`PortalPagesTests`: 404 für fremden Vertrag, Dokumentliste mit Gelesen-Status, Antrag senden, Verwaltung gesperrt), Browser (`UiAuditTests`: Portalseiten bei 360 px und Kontrast).

### DATEV-Export (#40)

- **Umfang:** Buchungsstapel (Format EXTF, Kopfsatz-Version 700, Kategorie 21, Formatversion 13) aus den Geldbewegungen nach **Zahlungsdatum** (Zuflussprinzip wie die Anlage V): Zahlungen der Mieter (aufgeteilt nach den Forderungen, die sie tilgen, über `TenantAccounts.Allocate`; Kaution und Saldovortrag sind keine Einnahme), Auszahlungen an Mieter und die Positionen bezahlter Belege (`CostItem`, Kostenart `leerstand` ausgenommen). Seite `/verwaltung/finanzen/datev` (Bereich *Darlehen & Steuer*, Recht Finanzen). Umsatzsteuer und BU-Schlüssel werden nicht gebucht (Vermietung ohne Umsatzsteuer).
- **Buchung:** Zahlungseingang = Bank im Soll, Gegenkonto Einnahmenkonto; bezahlter Beleg = Kostenkonto im Soll, Gegenkonto Bank (Gutschrift im Haben); Auszahlung = Bank im Haben. Belegfeld 1 = Rechnungsnummer (36 Zeichen), Buchungstext höchstens 60 Zeichen, Belegdatum `TTMM`, Betrag mit Komma ohne Tausenderpunkt.
- **Datei:** nur die führenden 14 Felder bis zum Buchungstext (`DatevFormat`, `Domain/Datev/Datev.cs`), Windows-1252, CRLF, Semikolon, Text in Anführungszeichen; Kopfzeile mit 31 Feldern (Berater, Mandant, Beginn Wirtschaftsjahr, Sachkontenlänge, Zeitraum, Festschreibung, Kontenrahmen). Konten mit weniger Stellen als die Sachkontenlänge werden rechts mit Nullen aufgefüllt, längere werden abgelehnt. `DatevFormat.Validate` prüft Betrag, Kontenlänge, Konto ≠ Gegenkonto und Datum im Zeitraum, bevor geschrieben wird.
- **Einstellungen** (`DatevSettings`, Tabelle `datev_settings`): Berater- (1001–9999999) und Mandantennummer (1–99999), Kontenrahmen SKR 03/04, Sachkontenlänge 4–8, Beginn des Wirtschaftsjahres, Diktatkürzel. Der Zeitraum eines Exports darf nicht über das Wirtschaftsjahr hinausgehen.
- **Kontenzuordnung** (`DatevAccount`, nur abweichende Werte; Schlüssel `bank`, `income:<Anlage-V-Kategorie>`, `cost:<Kostenart>`): Vorschläge je Kontenrahmen in `DatevAccountKeys.Default` – **nur Vorschläge**, die Konten legt der Steuerberater fest. Ein Wert, der dem Vorschlag entspricht, wird nicht gespeichert und folgt damit einem Wechsel des Kontenrahmens; „Zurücksetzen“ löscht alle Abweichungen.
- **Protokoll und Doppelexport** (`DatevExport` mit Zeitraum, Anzahl, Summe, Festschreibung, SHA-256 und der Datei selbst, damit sie erneut heruntergeladen werden kann: `/verwaltung/finanzen/datev/{id}.csv`; `DatevExportedItem` je Quelle, eindeutig): Posten, die in einem früheren Export stecken, werden übersprungen, außer „erneut aufnehmen“ ist gewählt. Zusätzlich die einfache Buchungsliste als CSV (`…/datev/buchungen.csv?von=&bis=`, UTF-8 mit BOM, Formel-Präfixe entschärft), ohne Protokoll.
- **Hinweise in der Vorschau:** Belege mit Rechnungsdatum im Zeitraum ohne Zahlungsdatum, nicht vollständig aufgeteilte Belege und Mietverhältnisse mit Zahlungen ohne Forderung (Überzahlung, Vorauszahlung) fehlen im Export.
- **Offen:** Export der Belegbilder als ZIP (Dateiname = Belegnummer), Kostenstelle je Objekt (Feld 37), Umsatzsteuer/BU-Schlüssel, Buchung der Sollstellungen (Forderungskonten) statt der Geldbewegungen, Zinsanteil der Darlehensraten. **Abnahme durch den Steuerberater steht noch aus** (manueller Test mit der DATEV-Importfunktion); die Formatprüfung ist bisher nur gegen die hier umgesetzte Beschreibung getestet.
- **Tests:** Domain (`Datev/DatevFormatTests`: Kopfzeile mit 31 Feldern, Zeilenaufbau, Auffüllen der Konten, Windows-1252, Feldlängen, Prüfregeln, Einstellungen, Vorschläge für alle Schlüssel), Integration (`Tax/DatevExportTests`: Buchungen nach Zahlungsdatum, Export nur mit Mandantendaten und im Wirtschaftsjahr, kein Doppelexport, Konten ändern und zurücksetzen, Kontenlänge, Buchungsliste), Web (`DatevPageTests`, `PageSmokeTests`).

### Tickets: Schadensmeldungen & Aufträge (#49)

- **Modell:** `Ticket` (Tabelle `tickets`: Titel, Beschreibung, Kategorie, Dringlichkeit, Objekt, optional Einheit (leer = Allgemeinflächen), Ort, Zugangszeiten, Melder = `Person`, Zuständiger = `Person` mit Konto, Termin, Beleg), `TicketComment` (`IsInternal`) und `TicketPhoto` (Datei der Meldung oder eines Kommentars, höchstens 5 je Meldung/Kommentar). Status: neu → in Prüfung → beauftragt → in Arbeit → erledigt → geschlossen; ein geschlossenes Ticket bleibt geschlossen.
- **Ein Dienst, drei Sichten** (`ITickets`/`TicketService`): Der Dienst leitet die Sicht aus dem Konto ab. *Verwaltung* (Admin oder Recht Stammdaten): alles, interne Kommentare, jeder Status, Zuweisen, Beleg verknüpfen. *Mieter*: eigene Meldungen (Melder = Person des Kontos), nur öffentliche Kommentare, melden und kommentieren. *Hausmeister/Handwerker*: zugewiesene Tickets, öffentliche Kommentare, Status nur „in Arbeit“/„erledigt“, ohne Zugangszeiten des Mieters. Was die Sicht nicht sehen darf, ist „nicht gefunden“. Zugewiesen werden können nur Personen vom Typ Hausmeister/Handwerker mit Konto.
- **Fotos:** Dateien über `IFileStore`; `TicketFileShare` gibt sie an Melder und Zuständige frei, Fotos interner Kommentare nie. Mieter melden auf dem Handy mit Kamera (`AppFileUpload`, `capture`).
- **Oberfläche:** Portal `/portal/tickets`, `/neu` (mit Notfall-Hinweis aus dem Vermieterprofil), `/{id}`; Verwaltung `/verwaltung/tickets` (Liste, `/neu`, `/{id}` mit Zuständigkeit und Beleg); Service `/service` (Aufträge) und `/service/tickets/{id}`. Verlauf, Fotos, Status und Kommentare liefert die gemeinsame Komponente `TicketThread`.
- **Benachrichtigungen:** Vorlage `ticket-update.sbn`. Neue Meldung → Admins; Statuswechsel, Kommentar und Zuweisung → Melder und Zuständiger, nicht an den, der gehandelt hat. Dashboard-Aufgabe „Neue Meldung“ (dringend bei hoher Dringlichkeit). `TicketPersonReferences` verhindert das Löschen beteiligter Personen und liefert das Aufbewahrungsdatum.
- **Kosten:** `ITickets.CostsAsync` summiert die Brutto-Beträge der verknüpften Belege je Ticket/Einheit; eine eigene Auswertungsseite gibt es noch nicht.
- **Ticket aus einer Wartung:** Die Wartungsseite (`/verwaltung/fristen/wartung/{id}`) hat den Knopf „Ticket anlegen“; `/verwaltung/tickets/neu?wartung={id}` belegt Titel, Objekt, Kategorie (`MaintenanceTicket.CategoryFor`) und Beschreibung vor, angelegt wird erst nach dem Absenden.
- **Offen:** Termin mit Uhrzeit. Arbeit, Rechnung und Handwerker-Link stehen im Abschnitt *Service-Bereich*.
- **Tests:** Domain (`TicketTests`), Integration (`Tickets/TicketTests`: Melden mit drei Fotos, Sichten, interne Kommentare samt Fotos, Zuweisung und Statusrechte, Mails, geschlossen), Web (`PortalPagesTests`: Seiten und Sperren), Browser (`UiAuditTests`).

### Service-Bereich für Hausmeister & Handwerker (#51)

- **Aufträge:** `/service` und `/service/tickets/{id}` (Abschnitt *Tickets*) zeigen dem Zuständigen Fotos, Ort, Beschreibung und die öffentlichen Kommentare, aber keine Zugangszeiten des Mieters. Dazu kommen Kommentar mit Fotos „nachher“ und Status „in Arbeit“/„erledigt“.
- **Arbeit und Rechnung** (`ITicketWork`, `TicketWork` = Datum, Minuten, Material, Bemerkung; `TicketInvoice` = hochgeladene Datei): schreiben dürfen der Zuständige mit Konto und, solange das Ticket offen ist, der Handwerker-Link; die Verwaltung liest mit Summe. Die Rechnung ist eine Datei am Ticket; daraus einen Beleg zu machen und zu verknüpfen (Kosten je Einheit) bleibt Sache der Verwaltung. `TicketFileShare` gibt dem Zuständigen die Rechnungen frei.
- **Handwerker-Link ohne Konto** (`IContractorLinks`, `TicketAccessLink`, Tabelle `ticket_access_links`): die Verwaltung erzeugt ihn am Ticket (`/verwaltung/tickets/{id}`: Empfänger, 1–60 Tage, Standard 14) und sieht die URL **nur einmal**; gespeichert wird nur der SHA-256 des 256-Bit-Geheimnisses. Öffentliche Seite `/auftrag/{token}` (Layout `ContractorLayout`, anonym), Dateien über `/auftrag/{token}/datei/{id}` (nur Fotos und Rechnungen genau dieses Tickets, nie Fotos interner Kommentare). Der Link zeigt kein anderes Ticket und keine Kontaktdaten des Mieters. Er endet mit Ablauf, Widerruf oder Abschluss des Tickets; unbekannt, abgelaufen, widerrufen und geschlossen sehen von außen gleich aus. Aktionen: Kommentar mit Fotos, Status „in Arbeit“/„erledigt“, Arbeit, Rechnung; die Verwaltung (und bei Kommentar und Status der Melder) bekommt dazu die Mail `ticket-update`. Dateien werden erst gespeichert, nachdem der Link geprüft wurde.
- **Regelmäßige Hausmeisteraufgaben** (`ICareRoutines`, `CareRoutine` je Objekt mit Rhythmus täglich bis jährlich, `CareCheck` = Abhaken mit Datum und Name): die Verwaltung legt sie unter `/verwaltung/tickets/routinen` an und beendet sie; der Hausmeister sieht unter `/service/routinen` nur die Aufgaben seiner Objekte (`PropertyAssignment`), fällige zuerst, und hakt „Erledigt (heute)“ ab. Die Häkchen (zuletzt, Anzahl im Jahr, `ChecksAsync` je Jahr) sind der Nachweis für die Umlage Hauswart und § 35a; ein Datum in der Zukunft wird abgelehnt.
- **Überfällig:** `CareRoutineTodos` meldet der Verwaltung im Dashboard („Tickets“) jede aktive Aufgabe, deren Fälligkeitstag ohne Häkchen verstrichen ist (`CareRoutine.IsOverdue`; eine noch nie abgehakte zählt ab dem Anlegetag), ab 7 Tagen Verzug dringend.
- **Nachweis als CSV:** `/verwaltung/tickets/routinen.csv?jahr=` (Recht Admin, Semikolon, UTF-8 mit BOM, Formel-Präfixe entschärft) listet alle Häkchen eines Jahres je Objekt und Aufgabe; Knöpfe für dieses und das Vorjahr stehen auf `/verwaltung/tickets/routinen`.
- **Nachweis als PDF:** `/verwaltung/tickets/routinen.pdf?jahr=` (`ICareRoutines.ProofPdfAsync`, Vorlage `care-proof.sbn`): je Objekt und Aufgabe Rhythmus, Anzahl und die Häkchen mit Datum, Name und Notiz; Aufgaben ohne Häkchen stehen mit „Keine Eintragung“ drin.
- **Foto am Abhaken:** `CareCheck.FileId` (optional, Datei im `IFileStore`); der Hausmeister wählt unter `/service/routinen` „Erledigt mit Foto“ (Kamera am Handy). Die Verwaltung sieht die Häkchen je Aufgabe unter `/verwaltung/tickets/routinen/{id}` (Jahr wählbar, Foto über `/dateien/{id}`); CSV und PDF vermerken „Foto: ja“.
- **Arbeit am Beleg:** Der Beleg (`/verwaltung/belege/{id}`) zeigt im Reiter „Auftrag“ die Tickets, die ihn als Rechnung verknüpft haben, mit Gesamtzeit und den Arbeitseinträgen samt Material (`ITicketWork.ForReceiptAsync`, nur Verwaltung), damit sich die Rechnung gegen Stunden und Material prüfen lässt.
- **Tests:** Domain (`Tickets/ServiceWorkTests`: Link-Gültigkeit, Token, Arbeit, Fälligkeit), Integration (`Tickets/ServiceWorkTests`: Link zeigt nur ein Ticket ohne interne Daten, Ablauf/Widerruf/Abschluss, Zuständiger bucht nur auf eigene Tickets, Hausmeisteraufgaben nur im eigenen Objekt), Web (`PortalPagesTests`, `PageSmokeTests`), Browser (`UiAuditTests`).

### Aushänge & Nachrichten (#50)

- **Aushänge** (`Notice`, Tabelle `notices`; `NoticeFile` = Anhänge, höchstens 5; `NoticeRead` = Bestätigung je Konto, eindeutig): je Objekt mit Titel, Text, Gültig ab/bis (leer = heute bzw. bis auf Weiteres) und optionalem Pflicht-Lesen. `INotices` (`Infrastructure/Notices/NoticeService.cs`): die Verwaltung (Admin oder Recht Stammdaten) legt an, zieht zurück (Ende gestern, der Aushang bleibt im Archiv) und sieht, wer gelesen hat (Mieter mit Portalzugang der laufenden Mietverhältnisse im Objekt). Mieter sehen nur *aktuelle* Aushänge ihrer Objekte; nach dem Ablaufdatum verschwinden sie von `/portal` und `/portal/aushaenge` samt Dateizugriff (`NoticeFileShare`).
- **Bestätigen:** `/portal/aushaenge` mit „Gelesen“ bzw. bei Pflicht-Lesen „Gelesen bestätigen“ (`AcknowledgeAsync` prüft, dass der Aushang zu einem Objekt des Mieters gehört). Reines Öffnen zählt nicht als gelesen.
- **Mail:** Job `notice-notifications` (alle 10 Minuten, `NoticeNotifications`, Vorlage `notice.sbn`) mailt Mietern mit Portalkonto und E-Mail-Adresse den Titel und den Link zum Portal, je Aushang einmal (`Notice.TenantsNotifiedAt`). Schon abgelaufene Aushänge werden nicht mehr angekündigt.
- **PDF fürs Treppenhaus:** `/verwaltung/aushaenge/{id}/aushang.pdf` (Recht Stammdaten) über `IPdfGenerator` und die eingebettete Vorlage `Pdf/Templates/notice.sbn`.
- **Nachrichten** (`MessageThread` je Mietverhältnis mit `TenantUnread`/`AdminUnread`, `Message`, `MessageFile`): `IMessages` leitet die Sicht aus dem Konto ab. Mieter sehen die Unterhaltungen ihrer Mietverhältnisse und schreiben nur für laufende Mietverhältnisse; die Verwaltung sieht alles und kann selbst eine Unterhaltung beginnen. Fremde Unterhaltungen sind „nicht gefunden“. Öffnen setzt die eigene Seite auf gelesen. Die Mail (`message-update.sbn`) enthält **keinen Nachrichtentext**, nur einen Hinweis und den Link (Mieter → Admins, Verwaltung → Mieter mit Portalkonto). Dashboard-Aufgabe „Neue Nachricht“. Anhänge über `IFileStore`, für Mieter freigegeben durch `NoticeFileShare`.
- **Archiv je Mietverhältnis:** `/verwaltung/vertraege/{id}/kommunikation` zeigt alle Nachrichten und die Aushänge der Mietzeit mit dem Lesestatus je Mieter (Nachweisbarkeit).
- **Oberfläche:** Portal `/portal/aushaenge`, `/portal/nachrichten[/neu|/{id}]` (Tab *Nachrichten*, Aushänge und ungelesene Nachrichten auf der Übersicht); Verwaltung unter dem Bereich *Mitteilungen* (`/verwaltung/aushaenge[/neu|/{id}]`, `/verwaltung/nachrichten[/neu|/{id}]`). Verlauf: Komponente `ConversationThread`.
- **Pflicht-Lesen offen:** `NoticeAcknowledgementTodos` meldet der Verwaltung im Dashboard („Aushänge“) jeden aktuellen Pflicht-Aushang, den eine Woche nach Beginn noch nicht alle Mieter bestätigt haben.
- **Zurückgezogen:** Ein Aushang, über den die Mieter schon eine Mail bekamen, löst beim vorzeitigen Zurückziehen eine zweite Mail aus (`Notice.WithdrawnAt` / `WithdrawalNotifiedAt`, Vorlage `notice-withdrawn.sbn`, derselbe Job), einmal je Aushang. Was nie angekündigt oder schon abgelaufen war, bleibt still.
- **Einzelne Einheiten:** `Notice.UnitIds` (`uuid[]`, leer = ganzes Haus; `AppliesToUnit`). Gewählte Einheiten müssen zum Objekt gehören. Wer den Aushang sieht, bestätigt, Anhänge öffnet, die Mail bekommt und als Leser gezählt wird, richtet sich nach der Einheit des laufenden Mietverhältnisses (`TenantScope.Reaches`/`OwnUnitsAsync`); im Formular gibt es dafür eine Auswahl der Einheiten des Objekts, in der Liste die Spalte „Objekt · Wohnung 1“.
- **Tests:** Domain (`Notices/NoticeTests`), Integration (`Notices/NoticeTests`: Reichweite nur im Objekt, Ablauf, Bestätigung, Mail genau einmal, Nachrichten nur für die eigenen Mietverhältnisse, Mail ohne Text), Web (`PortalPagesTests`, `PageSmokeTests`), Browser (`UiAuditTests`).


### Eigentümer je Objekt (#54)

- **Modell:** `PropertyOwner` (Tabelle `property_owners`: Objekt, Person, Anteil in % mit höchstens zwei Nachkommastellen, Gültig ab/bis, beides optional). Ohne Eintrag gehört das Objekt wie bisher allein dem Vermieter. Die Anteile aller an einem Tag gültigen Eigentümer dürfen 100 % nicht übersteigen (`PropertyOwner.ExceedsFullShare` prüft die Starttage), dieselbe Person nicht doppelt im selben Zeitraum (`Overlaps`). Ergeben die heute gültigen Anteile nicht 100 %, steht ein Hinweis am Objekt.
- **Dienst:** `IPropertyOwners` (`Get`, `Add`, `End` = Ende heute, bleibt als Historie; `Remove` für Fehleingaben; `OwnedBy` für die Eigentümer-Sicht). `PropertyOwnerReferences` (`IPersonReference`) verhindert das Löschen eines Eigentümers als Person und zeigt die Rolle „Eigentümer“.
- **Oberfläche:** Objektseite, Reiter „Eigentümer“ (Recht Stammdaten).
- **Anlage V je Eigentümer:** `IAnlageVService.GetForOwnerAsync` rechnet dieselbe Auswertung des Objekts und setzt jede Buchung mit dem Anteil des Eigentümers an (`PropertyOwner.YearShare`: nach Tagen gewichteter Jahresanteil, 50 % bis Juni und 100 % ab Juli = rund 75 %), je Buchung auf den Cent gerundet. Die Prüfung auf verbilligte Vermietung bleibt die des ganzen Objekts. Hinweise nennen den Anteil, die Rundung und einen Anteilswechsel im Jahr. Auf der Anlage-V-Seite gibt es die Auswahl „Auswertung für“ (Ganzes Objekt oder Eigentümer); CSV und PDF nehmen `?eigentuemer={personId}`; wer im Jahr nichts besaß, bekommt 404. Eine Feststellungserklärung der Gemeinschaft (Anlage FB) ist nicht umgesetzt.
- **Rolle „Eigentümer“:** `Roles.Owner`, Policy `Policies.Owner`, Start `/eigentuemer`. Wie Verwaltung mit Passwort und 2FA (`Roles.RequiringTwoFactor`, kein Magic Link), weil Steuerzahlen sichtbar sind. Der Zugang kommt aus der Person (`Person.UserId`): `SetUpAccessAsync` vergibt die Rolle an Personen, die Eigentümer sind und keine Mieter. `IOwnerPortal` (`Infrastructure/Portal/OwnerPortal.cs`) leitet die Person immer aus dem Konto ab; ein fremdes Objekt oder ein Jahr ohne Eigentum liefert nichts (Seite „Keine Auswertung“, CSV 404). Seiten: `/eigentuemer` (Objekte mit Anteil) und `/eigentuemer/objekte/{id}/anlage-v?jahr=` mit CSV. Zeilen ohne Links in die Verwaltung; die Verwaltungsseiten und -exporte bleiben gesperrt.
- **Vermieterprofil je Objekt:** Es kann mehrere `LandlordProfile`s geben (Einstellungen → Vermieter: Reiter je Profil, „+ Weiteres Profil“; das älteste ist das Standardprofil und nicht löschbar, ein von einem Objekt verwendetes ebenfalls nicht). `Property.LandlordProfileId` (leer = Standard, Auswahl „Absender auf Dokumenten“ in den Objektangaben, sobald es mehr als ein Profil gibt). `LandlordProfiles.ForPropertyAsync/ForUnitAsync` löst das Profil auf; `PdfRequest.PropertyId` sorgt dafür, dass `PdfGenerator` Briefkopf und Fußzeile des Objekts nimmt. Umgestellt sind Mietvertrag, Schreiben, Mieterhöhung, Mahnung, Abrechnungsversand, Übergabeprotokoll, Aushang-PDF und Anlage-V-PDF. **Noch beim Standardprofil:** der Absendername in den Benachrichtigungs-Mails (Aushänge, Nachrichten, Tickets, Dokumente) und die Notfallangaben im Portal.
- **Tests:** Domain (`PropertyOwnerTests`), Integration (`Properties/PropertyOwnerTests`), Web (`Tax/AnlageVPageTests`, `Portal/OwnerPortalTests`: nur eigene Objekte und Anteile, Verwaltung gesperrt, andere Rollen ohne Zugang).

### Hintergrundjobs (Quartz.NET 3)

- Persistenter Store in PostgreSQL (Tabellen `qrtz_*`, angelegt per Migration aus dem offiziellen Skript `Persistence/Sql/quartz_3_22_6_postgres.sql`). Geplante Jobs überstehen Neustarts.
- Neue Jobs registriert man in `DependencyInjection.AddJobs` mit `q.ScheduleInterval<TJob>(…)` oder `q.ScheduleDaily<TJob>(…)` (deutsche Uhrzeit). Die Logik gehört in einen testbaren Dienst, der Job ruft ihn nur auf (Beispiel `MailOutboxJob` → `MailOutbox`).
- Jeder Lauf wird in `job_runs` protokolliert (`JobRunLog`). Die Einträge werden nach `Jobs:LogRetentionDays` (30) gelöscht. Übersicht unter `/verwaltung/system/jobs`.
- `Jobs:Active=false` schaltet den Scheduler ab (z. B. in Web-Tests).
- Hinweis: Quartz 4.x ist erst seit September 2026 verfügbar und hat eine stark geänderte API. Bewusst wird die ausgereifte 3.x-Linie genutzt.

## Tests

| Projekt | Inhalt |
|---|---|
| `MietHahn.Domain.Tests` | Reine Unit-Tests der Fachlogik (Rechenkerne), ohne I/O |
| `MietHahn.Integration.Tests` | Gegen echtes PostgreSQL (Testcontainers, `postgres:18-alpine`). `PostgresFixture` startet einen Container je Testlauf, **jeder Test bekommt eine eigene Datenbank**, die beim Aufräumen wieder gelöscht wird (`DropDatabaseAsync`; sonst läuft die Platte des CI-Runners voll). `InfrastructureEnvironment` liefert die komplett registrierte Infrastruktur (migrierte DB, temporäre Dateiablage, `FakeTimeProvider`). Mailtests laufen gegen einen echten SMTP-Server (`MailpitFixture`) |
| `MietHahn.Web.Tests` | Komponenten mit bUnit (`BunitContext`). Ende-zu-Ende über HTTP mit `MietHahnWebFactory` (echte App, eigene DB je Test) und `TestBrowser`: lädt Seiten, folgt Weiterleitungen und schickt Blazor-Formulare inklusive Antiforgery-Token ab (Formular über den `FormName`). TOTP-Codes liefert `TestHelpers.Totp` |
| `MietHahn.Web.Tests/Browser` | Echte Browser-Tests mit Playwright (Chromium) gegen die per Kestrel gestartete App: 360-px-Breite ohne horizontales Scrollen, Navigation, Formulare mit `blazor.web.js`. Ohne installierten Browser werden sie übersprungen (Trait `Category=Browser`). Browser installieren: `pwsh tests/MietHahn.Web.Tests/bin/Debug/net10.0/playwright.ps1 install chromium` |
| `MietHahn.TestHelpers` | Gemeinsame Fixtures (PostgreSQL, Mailpit, Gotenberg) und Hilfen; selbst kein Testprojekt |
| `MietHahn.Architecture.Tests` | Schichtregeln (NetArchTest) |

Testdatenbanken: Der PostgreSQL-Container läuft mit `max_connections=300`, die Verbindungszeichenfolgen schließen ungenutzte Pool-Verbindungen nach 2 s (sonst „too many clients“ bei vielen parallelen Tests mit je eigener Datenbank).

Konventionen: Testnamen auf Deutsch mit Unterstrichen (`Loeschen_ist_logisch_…`), Zeit über `FakeTimeProvider`, kein `DateTime.Now` im Produktivcode (immer `TimeProvider`).

Testergebnisse als TRX und Abdeckung als Cobertura (wie in der CI):

```bash
dotnet test -- --report-trx --results-directory testergebnisse --coverage --coverage-output-format cobertura
```

## CI/CD (GitHub Actions)

| Workflow | Auslöser | Inhalt |
|---|---|---|
| `.github/workflows/ci.yml` | Push auf den Default-Branch (`ccr-*`, vorsorglich auch `main`), Pull Requests, manuell | Speicherplatz freiräumen, Restore, `dotnet format --verify-no-changes`, Release-Build (Warnungen sind Fehler), Playwright-Chromium installieren, alle Tests inkl. Testcontainers und Browser-Tests. TRX und Cobertura als Artefakt `testergebnisse` (14 Tage). Zweiter Job baut App- und Backup-Image (Layer-Cache über `type=gha`) und führt `deploy/backup/restore-test.sh` aus |
| `.github/workflows/release.yml` | Tag `v*.*.*` | Multi-Arch-Images (`linux/amd64`, `linux/arm64`) `ghcr.io/ther00st3r/miethahn` und `…/miethahn-backup` mit Tags `1.2.3`, `1.2`, `sha-…` und `latest` |
| `.github/dependabot.yml` | wöchentlich | NuGet (gruppiert: .NET-Plattform, Testpakete; Quartz-Major-Updates ausgeschlossen, siehe Architektur), Docker-Basisimages, Compose-Images, Actions |

Vor dem Push lokal dasselbe prüfen: `dotnet format --verify-no-changes && dotnet build -c Release && dotnet test`. Workflows lassen sich mit [actionlint](https://github.com/rhysd/actionlint) prüfen. Release-Ablauf und Branch-Schutz stehen in [CONTRIBUTING.md](../CONTRIBUTING.md).
