# Umsetzungsplan: Elocaro vertriebsfähig machen

Abgeleitet aus der [Produkt- und UX-Bewertung](produktbewertung.md) (Befunde P1–P14, Blocker V1–V8). Stand 24.10.2026, Basis Commit 59527c2.

**Ziel:** Eine fremde Person installiert Elocaro ohne Hilfe, kommt ohne Fehlversuch zum ersten Vertrag, zur ersten Zahlung und zur ersten Nebenkostenabrechnung und darf die Software rechtlich nutzen. Danach: erste zahlende Kunden in einem Pilot.

**Aufwand:** S ≈ bis 1 Tag · M ≈ 2–4 Tage · L ≈ 1–2 Wochen (eine Person, inkl. Tests und Doku nach CLAUDE.md).

## Stand der Umsetzung (24.10.2026)

| Paket | Stand | Issue |
|---|---|---|
| AP-01 `global.json` | erledigt | #71 |
| AP-02 Lizenz AGPL-3.0, „Über Elocaro“ | erledigt | #72 |
| AP-03 Release | vorbereitet (Version, Changelog); **Tag v0.1.0 und öffentliche Pakete stehen aus (Freigabe)** | #73 |
| AP-04 Installationsskript | erledigt | #74 |
| AP-05 Auftritt | README, SECURITY.md, Vorlagen erledigt; **Repository öffentlich stellen und Discussions stehen aus (Freigabe)** | #75 |
| AP-10 bis AP-19 (Phase 1) | erledigt, Gesamttestlauf grün | #76 bis #85 |
| AP-20 Rechtsprüfung, AP-21 Marke, AP-24 Testgruppe, AP-25 Rahmen | extern, offen | #86, #87, #90, #91 |
| AP-22 Rechtsstände | Übersicht und Hinweise erledigt; Prüfvermerk je Tabelle offen | #88 |
| AP-23 Bankdaten | Matcher erledigt; Echtexporte, FinTS-Pilot, photoTAN offen | #89 |
| AP-30 Import | erledigt (CSV; kein `.xlsx`) | #92 |
| AP-31 Beispieldaten | erledigt; öffentliche Demo-Instanz offen | #93 |
| AP-32 Navigation | umgesetzt (sechs Bereiche, zwei Tab-Zeilen); der Klicktest mit Nutzern steht noch aus (Testgruppe) | #94 |
| AP-33 Portal-Konto | erledigt | #95 |
| AP-34 Plattformen | Anleitung erledigt; Vorlagen fehlen (ungeprüft) | #96 |
| AP-35 Kaution | erledigt (ohne Bankkonto-Zinsen, ohne Portal) | #97 |
| AP-36 Kennzahlen | Übersicht erledigt; Cashflow je Jahr offen | #98 |
| AP-40 bis AP-44 (Phase 4) | nicht begonnen | #99 bis #103 |

**Gefundene Blocker für den Vertrieb der Heizkostenabrechnung:** Die Gradtagszahlen im Code sind ein **Platzhalter** („nicht aus einer amtlichen Tabelle“), die CO₂-Stufentabelle ist von Hand erfasst und ungeprüft. Beides steht auf der Seite „Rechtsstände“ als „prüfen“. Vor einem öffentlichen Start klären (Tabelle ersetzen, prüfen, `Verified = true`).

## Ergebnis der zweiten Prüfung (24.10.2026)

Weitere Lücken gegenüber den Wettbewerbern sind als Issues erfasst: Zählerstände durch Mieter (#104), Web-Push/Service Worker (#105), digitale Unterschrift (#106), zuverlässige Bankanbindung (#107), Belege per E-Mail (#108), Mieterhöhung nach Modernisierung (#109), Bescheinigungen (#110), Anwenderhandbuch (#111). Details: [produktbewertung.md](produktbewertung.md), Abschnitt 9.

## Übersicht

| Phase | Ergebnis | Arbeitspakete | Aufwand gesamt |
|---|---|---|---|
| **0 – Grundlagen** | Repo baut, ist lizenziert, Version 0.1.0 installierbar | AP-01 bis AP-05 | ca. 1 Woche |
| **1 – Erste Stunde** | Neue Installation führt ohne Fehlversuch bis zum Vertrags-PDF | AP-10 bis AP-19 | ca. 4 Wochen |
| **2 – Vertrauen** | Rechtstexte geprüft, echte Bankdaten getestet, Testgruppe | AP-20 bis AP-25 | ca. 6 Wochen (teils parallel, teils extern) |
| **3 – Reichweite** | Wechsel aus Excel/anderen Tools, schlanke Navigation, Portal mit Konto | AP-30 bis AP-36 | ca. 6 Wochen |
| **4 – Einnahmen (Pilot)** | Hosting- oder Pflege-Angebot mit ersten Kunden | AP-40 bis AP-44 | ca. 4–6 Wochen |

Kritischer Pfad: **AP-01 → AP-03 → AP-04 → (Phase 1) → AP-24 Testgruppe → Phase 4.** Phase 2 startet parallel zu Phase 1, weil juristische Prüfung und Bankpiloten Vorlauf brauchen.

## Entscheidung zur Monetarisierung (24.10.2026)

Elocaro bleibt vorerst ein **Nebenprojekt** mit kleiner zusätzlicher Einnahmequelle. Es bleibt bei AGPL-3.0, ohne Shop und ohne gesperrte Funktionen. Vorbereitet sind: Unterstützen ([foerderung.md](foerderung.md)), Feature-Sponsoring ([feature-sponsoring.md](feature-sponsoring.md)), Rechtsstände-Abo als Konzept ([rechtsstaende-abo.md](rechtsstaende-abo.md)). Hosting ist nur als Pilot für Bekannte gedacht (Phase 4 ruht). Ein Lizenz-Shop (Open Core oder Lizenzschlüssel) wäre nur mit Lizenzwechsel vor dem ersten öffentlichen Release möglich und ist nicht geplant.

## Entscheidungen, die vorher fallen müssen

| # | Frage | Optionen | Empfehlung | Blockiert |
|---|---|---|---|---|
| E1 | Lizenz | AGPL-3.0 · Business Source License · MIT | AGPL-3.0, wenn Open Source + eigenes Hosting; BSL, wenn Hosting exklusiv bleiben soll. Juristisch prüfen. | AP-02, Phase 4 |
| E2 | Geschäftsmodell | nur Open Source · Hosting · Support/Rechtsstands-Abo · Kombination | Open Source + bezahlte Rechtsstands-Pflege, Hosting als Pilot | Phase 4 |
| E3 | Primäre Zielgruppe | Selbsthoster · Privatvermieter 5–50 WE · WG-Vermieter | Start Selbsthoster, Wachstum 5–50 WE | Texte, AP-05 |
| E4 | Name | „Elocaro“ behalten · umbenennen | Erst Markenrecherche (AP-21), dann entscheiden | AP-04 (Image-Namen), FinTS-Registrierung |
| E5 | Portal-Umfang | Mieterkonto lesend ja/nein | ja, lesend | AP-33 |

## Phase 0 – Grundlagen

### AP-01 `global.json` reparieren (V2) · S · sofort
- `global.json` auf `"version": "10.0.100", "rollForward": "latestFeature"` zurücksetzen (wie vor c9ddd0f), `allowPrerelease` entfernen.
- Prüfen, ob in c9ddd0f weitere unbeabsichtigte Änderungen stecken.
- **Akzeptanz:** `dotnet format --verify-no-changes && dotnet build && dotnet test` lokal mit SDK 10.0.x grün; CI-Lauf grün; `docker build .` erfolgreich.

### AP-02 Lizenz (V1) · S · nach E1
- `LICENSE` im Wurzelverzeichnis, Lizenzhinweis in README und `THIRD-PARTY-NOTICES.md` abgleichen (libfintx LGPL-3.0 bleibt dynamisch eingebunden).
- Kopfzeile „Lizenz“ im Impressum/Info-Dialog der App (Benutzermenü → „Über Elocaro“ mit Version, Lizenz, Quellcode-Link; bei AGPL Pflicht für Netznutzer).
- **Akzeptanz:** GitHub erkennt die Lizenz; „Über Elocaro“ zeigt Version und Quellcode-Link.

### AP-03 Release-Prozess (V3) · S
- Tag `v0.1.0` erstellen (`release.yml` baut amd64/arm64), GHCR-Pakete `elocaro` und `elocaro-backup` öffentlich stellen.
- Version in der App sichtbar (Assembly-Informationsversion aus dem Tag), `CHANGELOG.md` beginnen.
- In `docs/betrieb.md` `ELOCARO_IMAGE` auf feste Version statt `latest` empfehlen (steht schon dort) und Beispiel anpassen.
- **Akzeptanz:** Auf einem leeren VPS führt die Anleitung in `docs/betrieb.md` ohne `docker login` zu einer laufenden Instanz.

### AP-04 Installation in einem Schritt (V7, Teil 1) · M
- `deploy/install.sh`: prüft Docker/Compose, fragt Domain, ACME-Mail, SMTP ab, erzeugt Passwörter (`openssl rand`), schreibt `deploy/.env`, startet Compose, wartet auf `/health`, gibt die URL aus.
- Ohne Domain: Modus „lokal ausprobieren“ (HTTP auf `localhost`, Mailpit statt SMTP).
- **Akzeptanz:** Frischer Debian-12- und Ubuntu-24.04-VPS: `curl … | bash` bzw. `./install.sh` → Ersteinrichtung erreichbar in < 10 Minuten. Restore-Test (`deploy/backup/restore-test.sh`) läuft danach grün.

### AP-05 Öffentlicher Auftritt (V6) · M
- README neu: Für wen, was es kann (5 Punkte), Screenshots (Desktop + Handy, aus dem Test), Vergleich „warum nicht SaaS“, Installation in 3 Befehlen, Grenzen („keine Rechtsberatung“, FinTS-Stand).
- Issue-Vorlagen (Fehler, Wunsch, Frage), GitHub Discussions aktivieren, `SECURITY.md` mit Meldeweg (V8).
- Repository öffentlich stellen (nach AP-01 bis AP-03 und E1).
- **Akzeptanz:** Eine Person ohne Vorwissen kann aus dem README in 2 Minuten sagen, ob Elocaro zu ihr passt.

## Phase 1 – Die erste Stunde

Leitfrage: Kommt ein neuer Vermieter ohne Fehlversuch zu Vertrag, Mieterkonto und Vertrags-PDF?

### AP-10 Startklar-Checkliste statt „Alles erledigt“ (P2) · M
- Neue `ITodoSource` „Einrichtung“ (`src/Elocaro.Infrastructure/Admin/…`), liefert offene Schritte als `TodoItem` mit Link: Vermieterprofil, E-Mail-Test, erstes Objekt, Wohnfläche je Einheit, erste Person, erster Vertrag, Bankkonto (Modul Bank), Buchhaltungsbeginn (Modul Bank/Mahnwesen), Vorlagen (Modul Dokumente).
- `Admin/Start.razor`: Leerzustand nur „Alles erledigt“, wenn mindestens ein Vertrag existiert; sonst Karte „So geht es los“ mit Fortschritt (z. B. „3 von 7 erledigt“).
- **Akzeptanz:** Leere Installation zeigt die Checkliste; jeder Punkt verschwindet, sobald er erfüllt ist. Integrationstest je Punkt, Web-Test für Startseite leer/gefüllt.

### AP-11 Voraussetzungen vorab statt nach Fehlversuch (P1) · M
- Standardvorlagen beim ersten Start automatisch anlegen (Migration oder Startup-Seed, idempotent); Schaltfläche „Standardvorlagen anlegen“ bleibt für Nachzügler.
- `Leases/ContractPage.razor`, `RentIncreasePage.razor`, Abrechnungs-PDF, Mahnungen: fehlendes Vermieterprofil **vor** der Aktion anzeigen (`Notice` mit Link, Aktion deaktiviert), nicht erst nach dem Klick.
- Gemeinsamer Dienst `IDocumentReadiness` (Profil, Vorlage, Pflichtdaten wie Wohnfläche) für alle Dokumentseiten.
- **Akzeptanz:** Kein Fehler-`Notice` mehr als Reaktion auf einen Klick, wenn die Ursache vorher bekannt war. Web-Tests für jede Dokumentseite ohne Profil.

### AP-12 Einrichtungsassistent: kürzer, mit Daten (P3) · L
- Modulfragen auf **eine** Seite mit Schaltern und Kurztext zusammenfassen (statt 11 Schritte); „Was gehört dazu?“ aufklappbar.
- Vorauswahl aus einer Einstiegsfrage ableiten: „Wie vermieten Sie?“ (1–5 Wohnungen selbst · größerer Bestand · WG-Zimmer · mit Miteigentümern). Nichts ist pauschal vorausgewählt.
- Neue Datenschritte über das vorhandene `Wizard`-Control: Vermieterprofil (Pflichtfelder Name, Anschrift), erstes Objekt mit Einheiten **inkl. Wohnfläche**, optional Bankkonto, Buchhaltungsbeginn (heute nur in der Zusammenfassung und nur, wenn noch leer).
- Während des Assistenten Seitenleiste ausblenden (Fokus), „Später“ führt zur Checkliste aus AP-10.
- Ziel: ≤ 8 Schritte. Fortschrittsanzeige und Text „11 Bereiche“ konsistent.
- **Akzeptanz:** Testdurchlauf „1–5 Wohnungen“ endet mit Profil, Objekt, Einheiten mit Fläche und gesetztem Buchhaltungsbeginn; Browser-Test für den kompletten Durchlauf.

### AP-13 Wohnfläche im Einheiten-Dialog (P4) · S
- `Properties/UnitEditor.razor` bzw. `UnitDialog.razor`: Feld „Wohnfläche (m²)“ mit `InputArea` beim Anlegen, schreibt die erste Fläche ab Stichtag (gleiche Logik wie Tab *Flächen*).
- Vertrags-PDF: Fehlt die Fläche, Warnung in der Vorschau („Wohnfläche fehlt, der Vertrag nennt keine Fläche“).
- **Akzeptanz:** Nach Anlegen mit Fläche keine „Bitte prüfen“-Warnung; Integrationstest Fläche ab Stichtag.

### AP-14 Datumsfelder (P5) · M
- `Controls/AppDateField.razor`: Platzhalter „TT.MM.JJJJ“, Eingabe ohne Punkte (`01112026`) und ISO (`2026-11-01`) akzeptieren, ungültige Eingabe als Feldfehler statt stillem Verwerfen.
- Neues `AppMonthField` (`MudDatePicker` mit `OpenTo="OpenTo.Month"`/`FixDay=1` oder Monatsauswahl), Eingabe „11.2026“ und „11/2026“; einsetzen in `Finance/Settings.razor`, `SetupWizard.razor` und überall, wo „(Monat)“ im Label steht.
- **Akzeptanz:** bUnit-Tests für alle Eingabeformate; `grep "Monat)"` findet keine `AppDateField` mehr.

### AP-15 Portal: Miete bei künftigem Mietbeginn (P8) · S
- `Infrastructure/Portal/TenantPortal.cs` Zeilen 38 und 66: statt `MonthlyRentOn(today)` den Stichtag `max(today, lease.Start)` verwenden; Anzeige „ab 01.11.2026“.
- **Akzeptanz:** Integrationstest „künftiger Vertrag zeigt Miete ab Beginn“, Web-Test Portalstart.

### AP-16 Einladung: Status, Absender, Bestätigung (P10) · M
- `People/Details.razor` Tab *Zugang*: Status „Einladung gesendet am …“, „ausstehend“ (aus der Mail-Outbox), „angemeldet am …“; Schaltfläche „Erneut einladen“.
- Rückfrage vor dem Versand („Anna Beispiel erhält eine Mail an anna@example.com“).
- Mails an Mieter: Absendername aus dem Vermieterprofil des Objekts („Test Vermieter über Elocaro“), Vorlagentext nennt Vermieter und Objekt. `MailOptions.SenderName` bleibt Rückfall.
- Hinweis nach dem Einladen: „Die Mail wird in den nächsten Minuten versendet.“
- **Akzeptanz:** Integrationstest Absendername je Objektprofil; Web-Test Statusanzeige.

### AP-17 Personendialog schlank (P7) · S
- `People/PersonEditor.razor`: Standard sichtbar nur Anrede, Vorname, Name, E-Mail, Telefon; Rest unter „Weitere Angaben“ (Firma/Gewerk nur bei Kontaktart Firma/Handwerker).
- **Akzeptanz:** Dialog zeigt beim Öffnen ≤ 6 Felder; vorhandene Tests grün.

### AP-18 Texte vereinheitlichen (P11) · S
- Alle Leerzustände auf „Sie“ umstellen (`grep -rn "Lege \|Füge " src`).
- „Monatsmieten aktualisieren“ ohne Buchhaltungsbeginn: Hinweis statt „sind aktuell“.
- „Abweichung zum Vorjahr (± %)“ mit Hinweistext („Kostenarten, die stärker abweichen, werden zur Prüfung markiert“).
- Architekturtest ergänzen, der „Lege |Füge “ in `.razor` meldet.

### AP-19 Erste-Stunde-Browsertest · M
- Neuer Playwright-Test (`tests/Elocaro.Web.Tests/Browser/FirstHourTests.cs`) mit genau dem Ablauf aus dem Usability-Test: Ersteinrichtung → 2FA → Assistent → Objekt/Einheiten → Mieter → Vertrag → Vertrags-PDF → Einladung → Portal → CSV-Import → Zuordnung bestätigen.
- Läuft in CI; schlägt fehl, sobald ein Schritt einen Fehler-`Notice` erzeugt.
- **Akzeptanz:** Test grün in Desktop- und 390-px-Ansicht. Er sichert Phase 1 dauerhaft ab.

**Meilenstein Phase 1:** Release `v0.2.0`. Erneuter Usability-Test (gleiches Skript wie im Bericht): kein Fehlversuch, Zeit bis zum Vertrags-PDF halbiert.

## Phase 2 – Vertrauen (parallel ab Phase 1)

### AP-20 Juristische Prüfung der Standardtexte (V4) · extern, ca. 2–4 Wochen
- Umfang: Mietvertrag Wohnraum, Zimmervertrag WG, Mieterhöhung (§ 558, Index, Staffel, § 560), Zahlungserinnerung/Mahnungen, NK-Abrechnungsanschreiben, Datenschutzinformation, Übergabeprotokoll.
- Ergebnis: geprüfte Fassung je Vorlage mit Prüfdatum; Vorlagenversion in `TemplateDefaults` hochzählen; „Texte vor dem Einsatz prüfen“ ersetzen durch „Geprüft am …, Stand …“.
- Kosten einplanen (Fachanwalt Mietrecht); alternativ Kooperation mit Vermieterverein.

### AP-21 Marke und Name (V5) · S + extern
- DPMA/EUIPO-Recherche „Elocaro“ (Klassen 9, 36, 42), Domain sichern, GitHub-Organisation statt Benutzerkonto.
- Entscheidung E4; bei Umbenennung vor AP-03-Folgereleases und FinTS-Registrierung umstellen.

### AP-22 Rechtsstände pflegbar und sichtbar (V4) · M
- Seite *Einstellungen → Rechtsstände*: Basiszinssatz, CO₂-Stufentabelle, Gradtagszahlen, VPI, Kappungsgrenzen-Verordnungen, jeweils mit „Stand“, Quelle, nächster Prüftermin.
- Dashboard-Aufgabe, wenn ein Stand abläuft (z. B. Basiszinssatz am 1.1./1.7.).
- Vorbereitung für E2: Rechtsstände als signiertes Datenpaket aus dem Release laden (später Abo-Inhalt).
- **Akzeptanz:** Abrechnung und Mahnwesen zeigen den verwendeten Stand im Rechenprotokoll.

### AP-23 Bankdaten aus der Praxis (P12) · M
- Echte (anonymisierte) CSV-/CAMT-Exporte von Sparkasse, Volksbank, DKB, ING, Commerzbank, Deutsche Bank, N26 sammeln; als Testdaten unter `tests/…/Bank/Samples`.
- Spaltenerkennung um Synonyme erweitern (z. B. „Auftraggeber/Empfänger“, „Zahlungsempfänger*in“, „Name Zahlungsbeteiligter“).
- Zuordnung: Wenn Betrag = Monatsmiete und Einheit und Zeitraum im Verwendungszweck stehen, Konfidenz ≥ „mittel“ auch ohne Gegenseite.
- FinTS-Pilot mit zwei Banken aus der Testgruppe; photoTAN/QR-Anzeige ergänzen (laut `docs/banking-fints.md` noch offen).
- **Akzeptanz:** Je Bank ein Import-Test; Zuordnungsquote „Vorschlag mit hoher Konfidenz“ für Mieteingänge ≥ 90 % in den Testdaten.

### AP-24 Testgruppe mit echten Vermietern · laufend
- 5–10 Vermieter (Mischung: 1–5 WE, 10–30 WE, WG), gehostete Testinstanzen oder begleitete Installation.
- Aufgabenliste wie im Bericht (Tabelle 3.3) als moderierter Test (Think-aloud, Aufzeichnung), danach 4 Wochen Echtbetrieb.
- Messgrößen: Aufgabenerfolg, Zeit bis erster Vertrag/erste Zuordnung/erste Abrechnung, SUS-Fragebogen, Fehlermeldungen.
- **Ergebnis:** priorisierte Befundliste für Phase 3; Zitate für README/Website.

### AP-25 Haftungs- und Datenschutzrahmen · S + extern
- Haftungshinweis in App („Über Elocaro“), README und Dokumenten-Fußzeile optional.
- Muster-Datenschutzinformation für Mieter (Vorlage existiert, in AP-20 prüfen lassen).
- Für Phase 4 vorbereiten: Auftragsverarbeitungsvertrag, technische und organisatorische Maßnahmen (TOM), Verzeichnis der Verarbeitungstätigkeiten.

## Phase 3 – Reichweite

### AP-30 Import aus Excel/CSV · L
- Assistent *Einstellungen → Daten importieren*: Vorlage-Datei (xlsx/CSV) zum Herunterladen, Upload, Vorschau, Fehler je Zeile, dann Übernahme.
- Reihenfolge: Objekte → Einheiten (mit Fläche) → Personen → Verträge (Beginn, Miete, Vorauszahlungen, Kaution) → Saldovorträge.
- Idempotent (zweiter Import aktualisiert, legt nicht doppelt an), alles im Audit-Log.
- **Akzeptanz:** Bestand mit 20 Einheiten aus einer Datei in < 5 Minuten; Integrationstests für Fehlerfälle.

### AP-31 Demo-Daten · S
- Schaltfläche „Beispieldaten laden“ (nur auf leerer Installation): ein Haus, 3 Wohnungen, 1 WG, Mieter, Umsätze, Belege für ein Abrechnungsjahr; „Beispieldaten entfernen“.
- Öffentliche Demo-Instanz mit täglichem Reset (für README/Website).

### AP-32 Navigation bündeln (P6) · L
- `Layout/AdminNavModel.cs`: Hauptbereiche auf 7 reduzieren, z. B. Übersicht · Bestand (Objekte, Personen) · Verträge · Geld (Bank, Forderungen, Belege) · Abrechnung & Steuer · Kommunikation (Tickets, Mitteilungen, Dokumente, Fristen) · System.
- Höchstens zwei Tab-Ebenen: Seiten-Tabs innerhalb einer Unterseite (z. B. Mieterkonto: Buchungen/Zahlung/Mahnwesen/Mieten je Jahr) werden Abschnitte oder Filter.
- Handy: Bottom-Bar Aufgaben · Bestand · Geld · Kommunikation · Menü.
- Vorher: Klickpfad-Analyse aus AP-24 (welche Seiten werden tatsächlich genutzt).
- **Akzeptanz:** Erneuter Test: jede Aufgabe aus Tabelle 3.3 in ≤ 3 Klicks ab Dashboard erreichbar.

### AP-33 Portal: Mieterkonto lesend (P9) · M · nach E5
- `/portal/konto`: Saldo, letzte Zahlungen, offene Posten, Vorauszahlungen; nur eigene Verträge (gleiche Absicherung wie `ITenantPortal`).
- **Akzeptanz:** Integrationstest „fremder Vertrag 404“, Browser-Test 360 px.

### AP-34 Installationsvorlagen (V7, Teil 2) · M
- Vorlagen für Coolify, Portainer-Stack, Unraid (Community Apps), Synology Container Manager, Proxmox-LXC-Skript; jeweils mit Backup-Hinweis.
- **Akzeptanz:** Je Plattform einmal installiert und Ersteinrichtung erreicht.

### AP-35 Kaution (P14) · M
- Kautionskonto je Vertrag: Soll (Kaution), Raten (§ 551 BGB: drei Monatsraten), Eingang, Anlageart, Zinsen, Rückzahlung/Verrechnung bei Auszug (Verknüpfung mit `MoveOut`).
- Dashboard-Aufgaben: Kautionsrate offen, Rückzahlung fällig.

### AP-36 Kennzahlen · M
- Startseite: zweite Zeile mit Mieteingang des Monats, Rückstände, Leerstand, Kosten je m² (Vorjahr), jeweils mit Link.
- Objekt-Übersicht: Cashflow-Kurve je Jahr.

**Meilenstein Phase 3:** Release `v1.0.0`, Ankündigung in Foren und Blogs (Vermieter, Selbsthoster), Vorstellung bei Vermietervereinen.

## Phase 4 – Einnahmen (Pilot)

Ausgestaltung hängt an E1/E2. Vorschlag: zwei Angebote, beide mit wenigen Pilotkunden.

### AP-40 Bezahlte Rechtsstands-Pflege · M
- Signiertes Datenpaket aus AP-22 (Basiszinssatz, CO₂, VPI, geprüfte Vorlagen-Updates aus AP-20) als Abo; Instanz prüft Signatur und Lizenzschlüssel.
- Ohne Abo bleibt alles nutzbar, Stände werden manuell gepflegt.

### AP-41 Verwaltetes Hosting · L
- Je Kunde eigener Compose-Stack (eine Installation = ein Vermieter, Mandantenfähigkeit nicht nötig), automatisiert per Skript/Ansible: Anlegen, Update, Backup, Restore-Test, Monitoring über `/health`.
- Subdomain je Kunde, eigene Domain optional; SMTP über gemeinsamen Versanddienst mit Absender des Kunden.
- Preis-Anker aus dem Markt: immocloud 9,99–39,99 €/Monat netto, objego ab 0 €, WISO 34,99 €/Jahr. Kalkulation: Serverkosten je Instanz (PostgreSQL + Gotenberg ≈ 1–2 GB RAM) + Support-Zeit.

### AP-42 Rechtlicher Rahmen Hosting · extern
- AGB, Auftragsverarbeitungsvertrag, TOM, Impressum/Datenschutzerklärung der Website, Kündigung mit Vollexport (Vorteil gegenüber vermietet.de ausdrücklich nennen).

### AP-43 Support · S + laufend
- Kanäle: Discussions (frei), Mail mit Antwortzeit (bezahlt), Wissensdatenbank aus den Seiteninfos (`PageInfo`) generiert.

### AP-44 Website · M
- Landingpage mit Positionierung („Die Vermieterverwaltung, die Ihnen gehört“), Vergleichstabelle, Demo-Link, Preise, Download/Installation.

## Reihenfolge der nächsten zwei Wochen

| Tag | Aufgabe |
|---|---|
| 1 | AP-01 (`global.json`), CI grün; E1/E3/E4 entscheiden |
| 1–2 | AP-15 (Portal-Miete), AP-18 (Texte), AP-13 (Wohnfläche im Dialog) |
| 2–3 | AP-02 Lizenz, AP-03 Release v0.1.0 |
| 3–5 | AP-10 Startklar-Checkliste, AP-11 Voraussetzungen vorab |
| 6–7 | AP-14 Datumsfelder, AP-17 Personendialog |
| 6–10 | AP-12 Assistent neu |
| 8–10 | AP-19 Erste-Stunde-Browsertest; AP-21 Markenrecherche beauftragen; AP-20 Angebot Fachanwalt einholen |

## Erfolgskriterien

| Kennzahl | Heute (Bericht) | Ziel Phase 1 | Ziel Phase 3 |
|---|---|---|---|
| Fehlversuche bis Vertrags-PDF | 3 | 0 | 0 |
| Schritte Einrichtungsassistent | 19 | ≤ 8 | ≤ 8 |
| Tab-Ebenen maximal | 3 | 3 | 2 |
| Installation auf leerem VPS | nicht möglich (V1–V3) | < 10 min mit Skript | < 10 min, 5 Plattformvorlagen |
| Testgruppe | 0 | 5 | 10, davon 3 im Echtbetrieb |
| Mieteingänge mit hoher Konfidenz (Testdaten) | unbekannt | – | ≥ 90 % |

## Umsetzung als Issues

Nach Projektkonvention (README: „Der Plan liegt in den GitHub-Issues“) wird jede Phase ein Epic, jedes Arbeitspaket ein Sub-Issue mit den Akzeptanzkriterien oben. Commits referenzieren `Closes #…`; Doku in `docs/` wird je Paket fortgeschrieben.
