# Betrieb (VPS mit Docker Compose)

## Überblick

```
Internet ──443/80──▶ caddy (TLS, Let's Encrypt) ──▶ app:8080 (Elocaro)
                                                      ├─▶ postgres:5432 (nur intern)
                                                      └─▶ gotenberg:3000 (nur intern)
backup (nachts) ──▶ postgres + Volume dateien ──▶ restic-Repository (S3/SFTP/…)
```

| Dienst | Image | Daten |
|---|---|---|
| `app` | `ghcr.io/elocaro/elocaro` (chiseled, Non-Root, ohne Shell) | Volume `files` → `/data` (Belege, Fotos, PDFs) |
| `postgres` | `postgres:18-alpine` | Volume `postgres` (Daten inkl. Anmelde-Schlüssel) |
| `gotenberg` | `gotenberg/gotenberg:8.39.0` | – |
| `backup` | `ghcr.io/elocaro/elocaro-backup` (kleine ASP.NET-Core-API `src/Elocaro.Backup` um pg_dump, restic und rclone) | Volumes `backup-status`, `backup-cache`; Repository extern bzw. `deploy/backup-repo` |
| `caddy` | `caddy:2.11-alpine` | Volumes `caddy-data`, `caddy-config` (Zertifikate) |

PostgreSQL und Gotenberg liegen im Netz `intern` ohne Internetzugang. Die App ist zusätzlich im Netz `extern` (ausgehend für SMTP, später FinTS und KI-API). Von außen erreichbar ist nur Caddy.

## Voraussetzungen

- VPS mit Linux, ≥ 2 GB RAM (Gotenberg/Chromium braucht ~500 MB), ≥ 20 GB Platte
- Docker Engine mit Compose-Plugin
- Domain mit A/AAAA-Eintrag auf den Server, Ports 80 und 443 offen
- SMTP-Zugang (z. B. vom Mailanbieter der Domain)

## Installation

Eine Übersicht aller Wege (Probelauf, Server, NAS, Azure) mit Checkliste und Fehlersuche steht in [installation.md](installation.md); hier folgen die Einzelheiten.

**Mit Installationsskript (empfohlen):**

```bash
git clone https://github.com/Elocaro/elocaro.git && cd elocaro
./deploy/install.sh            # fragt Domain, Kontakt-Mail und SMTP ab, erzeugt die Passwörter, schreibt deploy/.env und startet alles
```

Das Skript prüft Docker und Compose, schreibt `deploy/.env` (Rechte 600, ein vorhandenes `.env` wird nur mit `--force` ersetzt und als `.env.bak` gesichert), startet den Stack und wartet, bis die Adresse antwortet. Optionen: `--lokal` (Probelauf auf `https://localhost` mit selbst signiertem Zertifikat, ohne Domain), `--nur-env` (nur die Datei schreiben). Alle Fragen lassen sich über Umgebungsvariablen vorab beantworten (`ELOCARO_DOMAIN`, `ACME_EMAIL`, `MAIL_SENDER`, `SMTP_HOST`, `SMTP_PORT`, `SMTP_SECURITY`, `SMTP_USER`, `SMTP_PASSWORD`; mit `ELOCARO_NONINTERACTIVE=1` wird nichts gefragt). Das Sicherungsziel steht danach auf „lokal“; ein externes Ziel richten Sie wie unter [Backup](#backup) beschrieben ein.

**Von Hand:**

```bash
git clone https://github.com/Elocaro/elocaro.git && cd elocaro
cp deploy/.env.example deploy/.env
nano deploy/.env        # Domain, Passwörter, SMTP eintragen (Datenbankpasswort: openssl rand -base64 32)
docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d
docker compose -f deploy/docker-compose.yml --env-file deploy/.env ps
```

Nach 1–2 Minuten ist Elocaro unter `https://<domain>` erreichbar. Beim ersten Aufruf führt die Seite durch die **Ersteinrichtung** (Admin-Konto, danach Zwei-Faktor-Anmeldung). Sie verlangt den **Einrichtungscode**, damit nicht übernimmt, wer die neue Installation zuerst aufruft: `ELOCARO_SETUP_CODE` in `deploy/.env`. Ist dort keiner eingetragen (auch bei älteren Installationen), erzeugt die App bei jedem Start einen und schreibt ihn ins Protokoll, solange es noch kein Konto gibt: `docker compose -f deploy/docker-compose.yml --env-file deploy/.env logs app | grep Einrichtungscode`. Sobald ein Konto existiert, hat der Code keine Wirkung mehr.

Anschließend:
1. Unter *Einstellungen → E-Mail-Versand* eine Testnachricht senden.
2. *Einstellungen → Vermieter* pflegen. Die Angaben erscheinen in Briefkopf und Fußzeile von Dokumenten.
3. Backup einrichten und eine erste Sicherung anstoßen (siehe [Backup](#backup)).

## Konfiguration

Alle Einstellungen kommen über Umgebungsvariablen in `deploy/.env` (eingetragen in `deploy/docker-compose.yml`). Weitere Schlüssel lassen sich im Abschnitt `environment` des Dienstes `app` ergänzen; `__` trennt Ebenen (z. B. `SignIn__RequestsPerMinute=20`).

| Variable | Bedeutung |
|---|---|
| `ELOCARO_DOMAIN`, `ACME_EMAIL` | Domain und Kontakt für Let's Encrypt; daraus wird `Application__BaseUrl` (Links in Mails) |
| `POSTGRES_PASSWORD` | Datenbankpasswort |
| `MAIL_*`, `SMTP_*` | Absender und SMTP-Zugang (`SMTP_SECURITY`: `StartTls` für Port 587, `SslOnConnect` für 465) |
| `ELOCARO_IMAGE` | Image-Tag, Standard `ghcr.io/elocaro/elocaro:latest`; empfohlen: feste Version, z. B. `ghcr.io/elocaro/elocaro:0.1.0` (Änderungen siehe `CHANGELOG.md`). Die laufende Version steht unter „Über Elocaro“ (`/ueber`) |

## KI-Zugriff über MCP (optional)

`MCP_ENABLED=true` in `deploy/.env` schaltet die lesende MCP-Schnittstelle `/mcp` ein, mit der KI-Assistenten (Claude, GitHub Copilot, Cursor u. a.) die Daten abfragen können. Sie ist standardmäßig aus und in der Demo nie verfügbar. Weitere Schlüssel: `MCP_REQUESTS_PER_MINUTE` (je Absenderadresse, 120), `MCP_MAX_RESULTS` (Einträge je Abfrage, 50) und `ACCESS_TOKEN_MAX_DAYS` (längste Gültigkeit eines Zugriffstokens, 90). Anmeldung, Rechte, Anleitungen für die Clients und Datenschutzhinweise: [mcp.md](mcp.md). Hinter Caddy ist nichts weiter einzustellen; der Proxy muss den Header `Authorization` weiterreichen (Standard).

## Belegauslesung (optional, KI)

Beim Erfassen eines Belegs kann Elocaro aus dem hochgeladenen PDF bzw. Foto einen **Vorschlag** auslesen (Lieferant, IBAN, Rechnungsnummer und -datum, Leistungszeitraum, Brutto/Netto/USt, Kostenart, § 35a, Brennstoffmenge und CO₂). Der Vorschlag füllt nur das Formular vor; gespeichert wird erst, wenn Sie bestätigen. Unsicher erkannte Felder sind gelb markiert, Unstimmigkeiten (Netto + USt ≠ Brutto, ungewöhnlicher Steuersatz, ungültige IBAN, unmögliche Daten) stehen als Warnung darüber.

**Standard ist „Keiner“: Es verlässt kein Dokument den Server.** Der Anbieter wird ausschließlich über Umgebungsvariablen (`deploy/.env`) gewählt, ein Wechsel braucht keine Codeänderung:

| Variable (`deploy/.env`) | Konfigurationsschlüssel | Bedeutung |
|---|---|---|
| `RECEIPT_AI_PROVIDER` | `ReceiptExtraction__Provider` | `None` (Standard), `Claude` oder `Ollama`. Ein unbekannter Wert schaltet die Auslesung ab und wird unter *Einstellungen → Belegauslesung* gemeldet |
| `RECEIPT_AI_ACCEPT_EXTERNAL` | `ReceiptExtraction__AcceptExternalProcessing` | **Ausdrückliche Einwilligung** (`true`), dass Belege an einen externen Anbieter gehen. Ohne sie tut der Claude-Anbieter nichts, auch nicht mit Schlüssel |
| `ANTHROPIC_API_KEY` | `ReceiptExtraction__Claude__ApiKey` | API-Schlüssel. Nur hier; er wird nie in der Datenbank gespeichert und nie protokolliert |
| `RECEIPT_AI_CLAUDE_MODEL` | `ReceiptExtraction__Claude__Model` | Modellname, Standard `claude-sonnet-5-5` |
| – | `ReceiptExtraction__Claude__BaseUrl` | Standard `https://api.anthropic.com/` (z. B. für einen eigenen Proxy) |
| `OLLAMA_URL` | `ReceiptExtraction__Ollama__BaseUrl` | Adresse von Ollama, Standard in Compose `http://host.docker.internal:11434/` |
| `OLLAMA_MODEL` | `ReceiptExtraction__Ollama__Model` | Modell **mit Bildeingabe**, z. B. `qwen2.5vl:7b` oder `llama3.2-vision` (vorher `ollama pull …`) |
| – | `ReceiptExtraction__TimeoutSeconds` / `__MaxRequestMb` | Zeitlimit je Anfrage (120) und Obergrenze aller Seiten eines Belegs (20 MB) |

**Datenschutz**

- **Claude API:** Die Belege werden zum Auslesen an Anthropic übermittelt. Sie enthalten oft personenbezogene Daten (Mieter, Handwerker, Anschriften, IBANs). Vor dem Einschalten den Auftragsverarbeitungsvertrag des Anbieters abschließen und die Verarbeitung ins Verzeichnis von Verarbeitungstätigkeiten aufnehmen. Hinweis und Status zeigen die Belegseiten und *Einstellungen → Belegauslesung* (Recht *System*).
- **Ollama:** Läuft Ollama auf dem eigenen Server oder im eigenen Netz, bleiben die Belege dort. Ollama liest **nur Fotos (JPG, PNG), keine PDF-Dateien**; ein PDF muss als Foto/Scan hochgeladen werden. Die App erreicht Ollama über das Netz `extern`; der Compose-Dienst `app` kennt dafür `host.docker.internal`.
- Gesendet wird nur auf Klick auf **„Vorschlag aus Beleg auslesen“**, nie im Hintergrund. Jede Übergabe steht im Zugriffsprotokoll (Audit-Log, „Beleg zur Auslesung an … übergeben“). Prompts, Dokumentinhalte und der Schlüssel erscheinen in keinem Log.
- Die Seitenanzeige nennt den Verbrauch der letzten Auslesung (Eingabe-/Ausgabe-Tokens) als Kostenhinweis. Die Preise legt der Anbieter fest.
- **Lernen:** Wird ein Beleg mit Kostenart gespeichert, merkt sich Elocaro „Lieferant → Kostenart“ (Tabelle `supplier_cost_type_rules`, im Änderungsprotokoll und in der Datensicherung). Beim nächsten Beleg dieses Lieferanten hat die gelernte Kostenart Vorrang vor dem Modell.

Beispiel `deploy/.env` für Claude:

```
RECEIPT_AI_PROVIDER=Claude
RECEIPT_AI_ACCEPT_EXTERNAL=true
ANTHROPIC_API_KEY=sk-ant-...
```

## Belege per E-Mail (optional)

Elocaro kann ein Postfach abrufen und die Anhänge (PDF, JPG, PNG) als **offene Belege** ablegen: ohne Betrag und Kostenart, mit dem Hinweis „Betrag fehlt“ in der Belegliste und auf der Übersicht. Beträge und Kostenart ergänzen Sie danach wie bei jedem Beleg.

Voraussetzung ist ein eigenes Postfach mit IMAP-Zugang (z. B. `belege@ihre-domain.de`); ein Mailserver gehört nicht zu Elocaro. Einstellungen (Umgebungsvariablen mit `__` statt `:`):

| Schlüssel | Bedeutung |
|---|---|
| `DegreeDays__Enabled` | `false` schaltet den Abruf der Monatsgradtage vom Deutschen Wetterdienst ab (`opendata.dwd.de`, alle 12 Stunden); Standard `true`. Ohne Abruf gilt beim Mieterwechsel ohne Zwischenablesung der Platzhalter |
| `UpdateCheck__Enabled` (`UPDATE_CHECK_ENABLED`) | `false` schaltet den Hinweis auf neue Versionen ab; Standard `true`. Zweimal täglich fragt die App öffentlich bei GitHub (`api.github.com/repos/elocaro/elocaro/releases`) nach den veröffentlichten Versionen; GitHub sieht dabei die Adresse des Servers, übertragen wird nichts über die Installation. Es wird nie etwas installiert |
| `Documents__InstallDefaults` | `false` schaltet das automatische Anlegen der Standardvorlagen beim Start ab; Standard `true` |
| `ReceiptInbox__Enabled` | `true` schaltet den Abruf ein (alle fünf Minuten) |
| `ReceiptInbox__Host`, `Port`, `Security` | IMAP-Server, Standard Port 993 und `SslOnConnect` |
| `ReceiptInbox__UserName`, `Password` | Zugangsdaten des Postfachs |
| `ReceiptInbox__AllowedSenders__0` … | erlaubte Absender: Adresse oder `@domain`; **ohne Eintrag wird nichts abgerufen** |
| `ReceiptInbox__ProcessedFolder` | Ordner für verarbeitete Mails (Standard `Elocaro-verarbeitet`), leer = nur als gelesen markieren |
| `ReceiptInbox__MaxAttachmentMegabytes` | Größenlimit je Anhang, Standard 15 |
| `ReceiptInbox__Extract` | `true` liest die Daten mit der [Belegauslesung](#belegauslesung-optional-ki) aus (nur wenn diese bereit ist; Dokumente verlassen dann ggf. den Server) |

**Sicherheit:** Die Absenderadresse einer Mail lässt sich fälschen. Verwenden Sie eine nicht öffentliche Adresse und ein Postfach, dessen Server SPF/DKIM prüft und Spam aussortiert. Mails anderer Absender, ohne verwertbaren Anhang oder mit zu großen Dateien werden nicht übernommen (nur im Log vermerkt). Eine Virenprüfung führt Elocaro nicht durch; setzen Sie sie am Mailserver ein.

## Benachrichtigungen auf dem Gerät (Web-Push)

Elocaro ist als Web-App installierbar. Jeder Benutzer kann unter *Konto & Sicherheit → Benachrichtigungen auf diesem Gerät* Hinweise einschalten, die auch ohne geöffnete Seite erscheinen: neue Nachrichten, Meldungen (Tickets), Fristen, neue Dokumente und Aushänge, Zählerstand-Erinnerungen, Verbrauchsinformation und die Warnung zur Datensicherung. Sie folgen den bestehenden Benachrichtigungs-Mails: wer eine solche Mail bekäme und Push eingeschaltet hat, bekommt zusätzlich den Hinweis.

- **Datenschutz:** Die Push-Nachricht selbst ist leer. Das Gerät holt Titel und Kurztext nach dem Aufwecken vom eigenen Server (nur mit gültiger Anmeldung); beim Push-Dienst des Browser-Herstellers (Google, Mozilla, Apple, Microsoft) liegt kein Inhalt. Die Hinweise sind absichtlich allgemein („Neue Nachricht“).
- **Abschalten:** Pro Gerät am selben Schalter. Ungültig gewordene Geräte entfernt Elocaro selbst.
- **iPhone/iPad:** Push geht nur für Web-Apps, die zum Home-Bildschirm hinzugefügt wurden (Teilen → „Zum Home-Bildschirm“).
- **Technik:** Der Server erzeugt beim ersten Aufruf ein VAPID-Schlüsselpaar (privater Schlüssel durch Data Protection geschützt in der Datenbank; bei Verlust der Schlüsselringe müssen Geräte neu abonnieren). Die Absenderkennung ist `Push__Contact` (Standard: die Absenderadresse der Mails). Zugelassen sind nur die Push-Dienste von Google, Mozilla, Apple und Microsoft; weitere Hosts über `Push__AllowedHosts__0` …. Der Versand läuft über den Job `push-dispatch` (jede Minute). Bei Verbindungen ohne HTTPS (außer `localhost`) stehen Service Worker und Push nicht zur Verfügung.
- **Offline:** Ein Service Worker zeigt bei fehlender Verbindung eine Hinweisseite statt der Browser-Fehlerseite; die Anwendung selbst benötigt weiterhin eine Verbindung.

## Gesundheit & Logs

```bash
curl https://<domain>/health        # {"status":"Healthy"}
docker compose -f deploy/docker-compose.yml --env-file deploy/.env logs -f app
```

- `/health/live` meldet, ob der Prozess antwortet. Darauf prüfen Docker (`HEALTHCHECK` per `dotnet Elocaro.Web.dll healthcheck`) und Caddy.
- `/health` prüft Datenbank, Gotenberg, ob die Dateiablage beschreibbar ist und den Zustand der Datensicherung. Öffentlich meldet es nur den Gesamtstatus: `Healthy`, `Degraded` (beide HTTP 200) oder `Unhealthy` (HTTP 503).
- Welche Prüfung betroffen ist, sehen nur Konten mit dem Recht System: Angemeldet im Browser `/health` aufrufen, die Antwort enthält dann `checks` (`database`, `gotenberg`, `file-store`, `backup`), weiterhin ohne Fehlerdetails. Ohne Anmeldung stehen die Ursachen im App-Log (`logs -f app`).

## Update

Aktualisiert wird mit den veröffentlichten Images (`ghcr.io/elocaro/elocaro` und `…/elocaro-backup`), auf dem Server wird nichts gebaut:

```bash
deploy/update.sh 0.2.0     # auf eine bestimmte Version (empfohlen)
deploy/update.sh latest    # auf die neueste Version
```

Das Skript sichert zuerst (`backup backup`), merkt sich die laufenden Images als `elocaro-vorher:app` und `elocaro-vorher:backup`, trägt die Version in `deploy/.env` ein (`ELOCARO_IMAGE`, `ELOCARO_BACKUP_IMAGE`; die alte Datei bleibt als `.env.bak-vor-update`), lädt die Images, startet `app` und `backup` neu und wartet, bis `https://<Domain>/health/live` antwortet. Datenbank-Migrationen laufen beim Start automatisch und nicht zurück: Geht ein Update mit Schemaänderung schief, stellt man die Sicherung von vorhin wieder her (Abschnitt Wiederherstellen). Ohne Schemaänderung genügt es, die beiden `elocaro-vorher:…`-Images in `deploy/.env` einzutragen und `up -d --no-deps app backup` auszuführen.

**Hinweis in der App:** Unter *Einstellungen → System → Neuigkeiten* sehen Administratoren, welche Version läuft, ob eine neuere veröffentlicht wurde und was sich geändert hat; auf der Übersicht erscheint dann die Aufgabe „Neue Version … verfügbar“. Die Seite nennt den Befehl `deploy/update.sh <Version>`; die App selbst aktualisiert nichts, denn dafür bräuchte sie Zugriff auf Docker und damit auf den ganzen Server. Die Beschreibungen stammen aus den GitHub-Releases (Abschnitt der Version im `CHANGELOG.md`, den der Release-Workflow anlegt). Abschalten: `UpdateCheck__Enabled=false`.

Von Hand geht es auch: `ELOCARO_IMAGE` und `ELOCARO_BACKUP_IMAGE` setzen, dann `docker compose -f deploy/docker-compose.yml --env-file deploy/.env pull` und `up -d`.

**Privates Paket:** Solange die Images in GHCR nicht öffentlich sind, einmalig auf dem Server `docker login ghcr.io` mit einem Token (nur Scope `read:packages`) ausführen.

**Umstieg von selbst gebauten Images:** Frühere Installationen bauten auf dem Server (`docker compose build`, `ELOCARO_IMAGE=elocaro:0.1.0` und `ELOCARO_VERSION` in `deploy/.env`). `deploy/update.sh` ersetzt diese Zeilen; die Quellen unter dem Installationsordner werden nicht mehr gebraucht, nur `deploy/` (Compose-Datei, `Caddyfile`, Skripte) muss zum Stand passen.

### PostgreSQL-Major-Upgrade (z. B. 18 → 19)

Das Datenformat ist zwischen Major-Versionen nicht kompatibel:
1. Sichern: `docker compose … run --rm backup backup`.
2. `docker compose … down`, Volume `elocaro_postgres` umbenennen oder löschen.
3. Image von `postgres` **und** die Basis in `deploy/backup/Dockerfile` anheben, `up -d postgres`.
4. `docker compose … run --rm backup restore`, dann `up -d`.

## Module und Einrichtungsassistent

Nicht jeder braucht alle Funktionen. Unter **Einstellungen → Module** lässt sich jedes Fachmodul ein- und ausschalten; zu jedem steht dort, was es tut, wann es sich lohnt und was dazugehört. Immer an sind Objekte, Personen, Verträge, Mieterkonto, Benutzer und Einstellungen.

| Modul | Wofür |
|---|---|
| Bank & Zahlungsabgleich | Bankkonten (FinTS, CSV), Umsätze zuordnen, Regeln |
| Mahnwesen | Rückstände erkennen, Mahnungen vorbereiten |
| Belege | Belegablage und optionale Belegauslesung |
| Nebenkostenabrechnung | Umlageschlüssel, Heizkosten, Abrechnung (braucht Belege) |
| Zähler & Verbrauch | Zählerstände, Import, Verbrauchsinformation für Mieter |
| Mieterhöhung & Indexmiete | Staffeln, Verbraucherpreisindex (VPI), Mieterhöhungs-Schreiben |
| Steuer & Eigentümer | Anlage V, AfA, Darlehen, DATEV, Eigentümer-Portal (braucht Belege) |
| Tickets & Hausmeister | Anliegen, Handwerker, Routineaufgaben |
| Mitteilungen & Aushänge | Nachrichten und Aushänge im Mieterportal |
| Dokumente & Schreiben | Ablage, Schreiben aus Vorlagen |
| Fristen & Wartung | Kalender, Wartungen, Erinnerungsmails |

Ein abgeschaltetes Modul verschwindet aus Navigation, Suche, Tabs und Mieterportal. Seiten und Downloads antworten mit 404, die Hinweise auf dem Dashboard entfallen und die zugehörigen Hintergrundjobs laufen leer durch. **Es werden keine Daten gelöscht**: Wer ein Modul wieder einschaltet, findet alles unverändert vor. Hängt ein Modul von einem anderen ab, schaltet die Seite das andere mit ein bzw. mit aus.

### Auswahl innerhalb der Bereiche

Zusätzlich lässt sich einstellen, was Sie innerhalb eines Bereichs überhaupt verwenden. Das steht unter Einstellungen → Module direkt in der Kachel des jeweiligen Moduls (Auswahl der Grundfunktionen in der Kachel „Immer dabei“) und wird im Assistenten abgefragt:

| Frage | Optionen | Wirkung |
|---|---|---|
| Welche Mietmodelle verwenden Sie? | Staffelmiete, Indexmiete (die Vergleichsmiete ist immer dabei) | Das Vertragsformular bietet nur die gewählten Modelle an und fragt das Modell schon beim Anlegen, sobald es mehr als eines gibt. Indexfelder, die Seite „Verbraucherpreisindex“ und der Staffel-Job gibt es nur bei aktivem Modell. |
| Wie rechnen Sie Nebenkosten ab? | Vorauszahlung, Betriebskostenpauschale, Inklusivmiete | Das Vertragsformular zeigt nur die passenden Felder: bei Vorauszahlung Betriebs- und Heizkosten-Vorauszahlung, bei Pauschale die Pauschale, bei Inklusivmiete den enthaltenen Nebenkostenanteil (zusätzlich zur Kaltmiete). Der Anteil wird als eigene Sollstellung geführt, damit der interne Vergleich „Pauschale gezahlt“ den Betrag zeigt. |
| Wie arbeitet das Mahnwesen? | Mahngebühren, Verzugszinsen | Die Felder dafür erscheinen unter Forderungen → Finanzeinstellungen nur bei Auswahl oder wenn schon Werte gespeichert sind. |
| Welche Teile der Steuer nutzen Sie? | Darlehen, Abschreibung (AfA) und Erhaltungsaufwand, DATEV-Export, Eigentümer-Portal (die Anlage V ist immer dabei) | Seiten, Tabs und Downloads der abgewählten Teile entfallen (404). Der Bereich „Darlehen & Steuer“ bleibt, solange eine Seite darin übrig ist. |
| Was vermieten Sie? | Zimmerweise (WG), Garagen/Stellplätze, Gewerbe (Wohnungen sind immer dabei) | Die Einheitenart-Auswahl bietet nur Gewähltes an. Die Zimmer-Verwaltung der Wohnung erscheint nur bei WG-Vermietung. |
| Welche Angaben im Mietvertrag? | Kaution, Bürgen | Die Felder fehlen in Anlegen-Formular und Vertragsdetails, wenn nicht gewählt. |

Zusätzlich blendet die Heizungsseite eines Objekts Abschnitte nach dem Brennstoff aus: die Aufteilung Heizung/Warmwasser nur bei gemeinsamer Anlage, die Heizöl-Angaben nur bei Heizöl, die CO₂-Aufteilung nur bei Gas und Öl. Der Assistent fragt am Ende auch, ab welchem Monat die Monatsmieten automatisch gebucht werden (nur solange noch kein Start gesetzt ist). Die Mahnwesen-Einstellungen unter Finanzen gibt es nur, solange das Modul Mahnwesen an ist.

Eine abgeschaltete Option bleibt sichtbar, solange ein Vertrag sie noch verwendet. Die Seite nennt dann die Anzahl der Verträge. Bestehende Beträge und Verträge werden nie verändert oder gelöscht.

Nach der Ersteinrichtung (Administrator und Zwei-Faktor-Anmeldung) führt das Dashboard einmalig in den **Einrichtungsassistenten**. Er hat höchstens sieben Schritte und blendet die Navigation aus, bis er fertig ist:

1. **Willkommen:** Die Frage „Wie vermieten Sie?“ (einige Wohnungen, größerer Bestand/Hausmeister, WG-Zimmer) setzt eine Vorauswahl der Funktionen und Optionen. Nichts ist pauschal eingeschaltet.
2. **Funktionen:** Jedes Modul mit Kurzbeschreibung und „Was gehört dazu?“; hier schalten Sie ein und aus.
3. **Ihre Vermietung:** die Optionen der eingeschalteten Module (Mietmodelle, Abrechnungsarten, Steuer, Einheitenarten, Vertragsangaben, Mahnwesen), siehe Tabelle unten.
4. **Ihre Absenderdaten** (nur, solange es kein Vermieterprofil gibt): Name und Anschrift für Briefkopf und Fußzeile.
5. **Erstes Objekt** (nur, solange es kein Objekt gibt): Mit dem Haken „Erstes Objekt jetzt anlegen“ erscheinen Haus mit Anschrift und Wohnungen samt Wohnfläche; ohne Haken überspringt „Weiter“ den Schritt.
6. **Buchhaltung:** Beginn der automatischen Monatsmieten (nur, solange er fehlt). Die Standardvorlagen legt Elocaro beim Start selbst an.
7. **Zusammenfassung:** zeigt, was gespeichert und angelegt wird.

„Später“ überspringt den Assistenten ohne Änderung; die Übersicht zeigt dann die Checkliste „So geht es los“. Unter Einstellungen → Module lässt er sich jederzeit erneut starten (`/verwaltung/einrichtung`; ohne die Einstiegsfrage). Bestehende Installationen behalten beim Update alle Module und werden nicht zum Assistenten geleitet.

## Bankumsätze zuordnen

Nach dem Import erscheinen offene Umsätze unter **Finanzen → Umsätze zuordnen** (auch als Hinweis auf der Startseite). Dort lässt sich der Vorschlag bestätigen, ändern/aufteilen oder der Umsatz ignorieren. Wiederkehrende Zahlungen (Stadtwerke, Versicherungen, private Abbuchungen) lassen sich unter **Zuordnungsregeln** hinterlegen. Eine Zuordnung kann auf der Seite des Umsatzes rückgängig gemacht werden; sie stehen im Änderungsprotokoll und sind Teil der Datensicherung wie alle Tabellen (`transaction_assignments`, `match_rules`). Es sind keine zusätzlichen Einstellungen oder Dienste nötig; die Migration läuft beim Start.

## Mahnwesen

Der Job `dunning` (täglich 6:15, sichtbar unter **System → Hintergrundjobs**) vergleicht die Rückstände mit den Mahnvorschlägen. Er **versendet nichts**: Vorschläge erscheinen auf der Startseite und unter **Finanzen → Mahnwesen**; Brief und E-Mail entstehen erst nach der Freigabe dort. Für die Freigabe sind nötig:

- ein **Vermieterprofil** (Einstellungen → Vermieter) für Briefkopf, Absender und Bankverbindung,
- erreichbarer **Gotenberg** (PDF) und für den E-Mail-Versand ein funktionierender Mailserver (die Mail geht über die Outbox, siehe E-Mail-Versand).

Unter **Finanzen → Einstellungen Finanzen → Mahnwesen** stehen Karenzzeit, Wartezeiten zwischen den Stufen, Zahlungsfrist, Mindestbetrag, Mahngebühren je Stufe, die Verzugszinsen und der **Basiszinssatz**. Der Basiszinssatz ändert sich zum 1. Januar und 1. Juli und muss dort von Hand nachgetragen werden (Veröffentlichung der Deutschen Bundesbank); ohne Wert werden keine Verzugszinsen berechnet. Voreinstellung: keine Gebühren, keine Zinsen. Die freigegebenen Briefe liegen als PDF in der Dokumentenablage (Kategorie *Schriftverkehr*, nicht für Mieter freigegeben) und sind Teil der Datensicherung; Tabellen `dunnings` und `dunning_holds`.

> Die mitgelieferte Mahnvorlage (`Pdf/Templates/dunning.sbn`, `Mail/Templates/dunning.sbn`) ist eine **Basisvorlage und keine Rechtsberatung**. Texte, Fristen, Gebühren und Zinsen vor dem produktiven Einsatz rechtlich prüfen lassen.

## Nebenkostenabrechnung

Der **Abrechnungs-Assistent** (Objekt → Abrechnungs-Assistent) führt in sechs Schritten durch die Abrechnung und friert sie bei der Freigabe ein. Auf der Detailseite der freigegebenen Abrechnung (`/verwaltung/abrechnungen/{id}`) wird das Schreiben je Mietvertrag geprüft (Vorschau als PDF), versendet und der Zugang protokolliert. Der Versand legt das Schreiben in der Dateiablage und im Mieterportal ab und bucht Nachzahlung bzw. Guthaben im Mieterkonto. Dafür sind nötig:

- ein **Vermieterprofil** (Einstellungen → Vermieter) für Briefkopf, Absender und Bankverbindung,
- erreichbarer **Gotenberg** (PDF) und für den E-Mail-Versand ein funktionierender Mailserver.

Konfiguration (Abschnitt `Statements`, Umgebungsvariablen `Statements__…`):

| Schlüssel | Standard | Bedeutung |
|---|---|---|
| `Statements__PaymentDays` | 30 | Tage ab Versand bis zur Fälligkeit einer Nachzahlung bzw. Auszahlung eines Guthabens |
| `Statements__PrepaymentSafetyPercent` | 5 | Sicherheitszuschlag auf den Vorschlag für die neue Vorauszahlung (im Versanddialog überschreibbar) |

Auf der Startseite erinnert ab Oktober des Folgejahres eine Aufgabe an die Abrechnung des Vorjahres, 6, 4 und 2 Wochen vor dem 31.12. wird sie dringlicher (§ 556 Abs. 3 BGB). Nach Fristablauf lässt sich die Abrechnung weiter erstellen. Soll die Erinnerung nicht mehr erscheinen, blendet man sie im Abrechnungs-Assistenten für das Jahr dauerhaft aus („Erinnerung für 2025 ausblenden“); dort lässt sie sich auch wieder einblenden.

> Die mitgelieferte Abrechnungsvorlage (`Pdf/Templates/statement.sbn`, `Mail/Templates/statement.sbn`) ist eine **Basisvorlage und keine Rechtsberatung**. Texte vor dem produktiven Einsatz rechtlich prüfen lassen. Die CO₂- und Gradtagstabellen der Heizkostenabrechnung sind „Stand/prüfen“ (siehe Entwicklung).

## Aufbewahrung ausgehender E-Mails

Die Mail-Warteschlange (`outgoing_mails`) ist der Versandnachweis. Es gelten drei Regeln:

- **Mails mit geheimen Links** (Einladung, Anmeldelink, Passwort zurücksetzen, Signatur-Link): Der Text bleibt nur, bis die Mail zugestellt ist oder endgültig fehlgeschlagen ist, dann ersetzt `[Inhalt entfernt]` ihn. Eine solche Mail lässt sich nicht erneut senden; Einladung oder Link werden neu ausgelöst. Auf dem Dashboard meldet sie sich danach noch eine Woche, die Seite *System → E-Mail-Versand* zeigt sie dauerhaft als fehlgeschlagen.
- **Frist:** Der Job `mail-retention` (täglich 3:45) löscht Mails nach `Mail__RetentionDays` Tagen (Standard 1095, also 3 Jahre; `MAIL_RETENTION_DAYS` in `deploy/.env`; 0 oder weniger schaltet das Löschen ab). Mails, auf die ein Abrechnungsversand oder eine Mahnung verweist, bleiben als Nachweis mit Empfänger, Betreff, Zeitpunkt, Status und Anhang-Verweis; nur ihr Text wird geleert. Wartende Mails bleiben unberührt.
- **Bestand:** Beim Update leert die Migration bereits versendete oder fehlgeschlagene Mails mit geheimen Links. Ältere Datensicherungen enthalten sie weiterhin, bis diese nach der Backup-Aufbewahrung wegfallen; die meisten Links sind da längst abgelaufen (Einladung 7 Tage, Signatur bis zum genannten Datum).

## Sicherheit

- TLS und HSTS über Caddy; die App setzt CSP, `X-Frame-Options: DENY`, `nosniff`, Referrer- und Permissions-Policy.
- Die App übernimmt Schema und Client-IP aus `X-Forwarded-*` nur von Absendern aus privaten Netzen (Docker-Netz). Die HTTPS-Umleitung macht Caddy (`Hosting__HttpsRedirection=false` im Image).
- **Cookies:** Anmelde-, Zwei-Faktor-, Status- und Antiforgery-Cookie sind `Secure`. In Produktion (`ASPNETCORE_ENVIRONMENT=Production`, im Image gesetzt) beantwortet die App deshalb Seiten mit Formularen über unverschlüsseltes HTTP mit einem Fehler 500. Das betrifft nur Proxys, die `X-Forwarded-Proto` nicht setzen oder nicht aus einem privaten Netz kommen.
- **Sitzungen beenden:** *Konto & Sicherheit → Auf allen Geräten abmelden* beendet alle Anmeldungen des eigenen Kontos, offene Seiten spätestens nach einer Minute, und macht „Dieses Gerät merken“ ungültig. In der Demo ist das abgeschaltet, die Konten sind dort geteilt.
- **Data-Protection-Schlüssel** (Cookies, Tokens) liegen in der Tabelle `data_protection_keys`. Anmeldungen überstehen so Neustarts und sind im DB-Backup enthalten. Die Schlüssel sind dort nicht zusätzlich verschlüsselt. Wer Zugriff auf die Datenbank hat, hat ohnehin Zugriff auf alle Daten; die Datenbank ist daher nur intern erreichbar. Die Warnung „No XML encryptor configured“ beim ersten Start ist erwartet.
- **Zertifikat für die Schlüssel (optional, nicht Standard):** siehe [Data-Protection-Schlüssel verschlüsseln](#data-protection-schlüssel-verschlüsseln).
- Uploads: Caddy begrenzt auf 30 MB, die App zusätzlich auf `FileStore__MaxSizeMb` (25). Fotos dürfen höchstens `FileStore__MaxImageMegapixels` (50) groß sein, darüber lehnt die App sie vor dem Dekodieren ab.
- Speicherlimits: `app` höchstens `APP_MEM_LIMIT` (Standard `1536m`), `gotenberg` höchstens `GOTENBERG_MEM_LIMIT` (Standard `1g`), damit ein einzelner Container nicht den ganzen Server lahmlegt. Wird ein Limit erreicht, beendet Docker den Container und startet ihn neu; bei wiederholten Neustarts (`docker compose ps`, `docker inspect … OOMKilled`) den Wert in `deploy/.env` erhöhen.
- Container: Alle Dienste laufen mit `no-new-privileges` und `cap_drop: ALL`. Zurück bekommen nur `postgres` (`CHOWN`, `DAC_OVERRIDE`, `FOWNER`, `SETGID`, `SETUID`: Besitzer des Datenverzeichnisses setzen, dann zum Benutzer `postgres` wechseln), `backup` (`CHOWN`, `DAC_OVERRIDE`, `FOWNER`: Dateien des App-Benutzers lesen und wiederherstellen) und `caddy` (`NET_BIND_SERVICE`). Eigene Erweiterungen an diesen Containern brauchen womöglich mehr.
- **PDF-Erzeugung (SSRF-Schutz):** Gotenberg läuft mit `--chromium-deny-private-ips` und lehnt Adressen im Docker-Netz, Loopback und Link-Local ab. Zusätzlich entfernt `PdfHtmlGuard` aus Vorlagen-HTML Frames, Skripte, `link`/`meta`/`base`/Formulare und alle externen Quellen (`src`, CSS-`url()`, `@import`); nur eingebettete `data:image/`-Bilder bleiben. Eigene Gotenberg-Aufrufe in `docker-compose.override.yml` müssen das Flag übernehmen.
- **Zugriffstoken** (MCP): persönlich, nur als Hash gespeichert, mit Ablauf, wirken nie über die Rechte des Kontos hinaus und enden mit der Sperre des Kontos; siehe [mcp.md](mcp.md).
- Geheimnisse nur in `deploy/.env` (per `.gitignore` ausgeschlossen), Dateirechte `chmod 600 deploy/.env`.

### Data-Protection-Schlüssel verschlüsseln

Mit einem Zertifikat außerhalb der Datenbank werden neue Schlüssel verschlüsselt gespeichert. Ein Datenbankabzug allein reicht dann nicht mehr, um Anmelde-Cookies zu fälschen oder FinTS-Benutzerkennungen zu lesen.

> **Warnung:** Das Zertifikat liegt nicht im Backup. Geht es verloren oder fehlt es nach einer Wiederherstellung, sind alle mit neuen Schlüsseln geschützten Werte verloren: Sitzungen werden ungültig, gespeicherte FinTS-Benutzerkennungen und der private Push-Schlüssel sind unlesbar. Sichern Sie das Zertifikat und sein Passwort **getrennt** von `deploy/.env` und vom Backup, bevor Sie es einschalten. Es lässt sich nicht einfach austauschen und nicht folgenlos wieder entfernen.

```bash
cd deploy
PASS=$(openssl rand -hex 32); echo "DATA_PROTECTION_CERT_PASSWORD=$PASS" >> .env
echo "DATA_PROTECTION_CERT_PATH=/run/elocaro/dataprotection.pfx" >> .env
openssl req -x509 -newkey rsa:4096 -sha256 -days 3650 -nodes -keyout dp.key -out dp.crt -subj "/CN=Elocaro Data Protection"
openssl pkcs12 -export -inkey dp.key -in dp.crt -out dataprotection.pfx -passout "pass:$PASS"
rm dp.key dp.crt
sudo chown 1654 dataprotection.pfx && sudo chmod 400 dataprotection.pfx   # 1654 ist der Benutzer der App im Container
cat > docker-compose.dataprotection.yml <<'YAML'
services:
  app:
    volumes:
      - ./dataprotection.pfx:/run/elocaro/dataprotection.pfx:ro
YAML
docker compose -f docker-compose.yml -f docker-compose.dataprotection.yml --env-file .env up -d
```

Die zusätzliche Compose-Datei bei allen späteren `docker compose`-Aufrufen mit angeben.

- **Vorhandene Schlüssel bleiben, wie sie sind:** Sie bleiben lesbar, aber auch unverschlüsselt in der Datenbank, und Werte damit lassen sich weiter fälschen und entschlüsseln. Verschlüsselt wird nur, was neu entsteht: spätestens beim nächsten Schlüsselwechsel nach 90 Tagen. Vollen Schutz gibt es also nur, wenn das Zertifikat schon bei der Ersteinrichtung gesetzt ist.
- **Startfehler:** Ist `DATA_PROTECTION_CERT_PATH` gesetzt und die Datei fehlt, das Passwort falsch oder kein privater Schlüssel enthalten, startet die App nicht und nennt die Ursache im Log. Leer oder nicht gesetzt: kein Zertifikat, alles wie bisher.
- **Wiederherstellung:** Auf einem neuen Server vor dem ersten Start `dataprotection.pfx`, die Compose-Datei und die beiden Einstellungen wieder einspielen.
- Das Ablaufdatum des Zertifikats prüft Elocaro nicht; ein abgelaufenes Zertifikat funktioniert weiter.

## Backup

Der Dienst `backup` (Image `ghcr.io/elocaro/elocaro-backup`, Quelle `src/Elocaro.Backup`, Image `deploy/backup/Dockerfile`) sichert jede Nacht um `BACKUP_TIME` (Standard 02:30):

1. die Datenbank per `pg_dump` (Custom-Format; passt zur PostgreSQL-Version, da das Image auf `postgres:18-alpine` basiert),
2. das Volume `files` (Belege, Fotos, PDFs),

verschlüsselt und dedupliziert mit [restic](https://restic.net) in ein Repository. Danach werden alte Stände ausgedünnt (7 tägliche, 4 wöchentliche, 12 monatliche; `BACKUP_BEHALTEN_*`) und das Repository geprüft (`restic check`).

### Einrichten

In `deploy/.env`:

| Variable | Bedeutung |
|---|---|
| `RESTIC_PASSWORD` | Schlüssel des Repositorys. **Ohne dieses Passwort ist keine Wiederherstellung möglich.** Außerhalb des Servers aufbewahren (Passwortmanager). |
| `RESTIC_REPOSITORY` | Ziel, siehe unten. Standard `/repo` = Ordner `BACKUP_LOCAL_PATH` (`deploy/backup-repo`) auf demselben Server, nur zum Ausprobieren |
| `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY` | Zugang für S3-kompatible Ziele |
| `BACKUP_TIME` | Uhrzeit (deutsche Zeit, `HH:MM`) |
| `BACKUP_API_TOKEN` | Zugang der App zum Backup-Dienst (nur im internen Docker-Netz; `install.sh` erzeugt ihn, sonst z. B. `openssl rand -base64 32`). Ohne Token läuft die Sicherung wie bisher nur nach den Variablen dieser Tabelle, die App kann sie dann nicht steuern. |

Beispiele für `RESTIC_REPOSITORY` (alle von restic unterstützten Ziele funktionieren):

```
s3:https://s3.eu-central-1.amazonaws.com/mein-bucket/elocaro     # AWS, Hetzner Object Storage, Wasabi, MinIO …
sftp:backup@nas.example.de:/volume1/elocaro                      # SFTP: Schlüssel nach deploy/backup-ssh/ (id_ed25519 + known_hosts)
rest:https://benutzer:passwort@backup.example.de/elocaro          # restic REST-Server
azure:elocaro-backup:/                                           # Azure Blob: Container elocaro-backup, dazu AZURE_ACCOUNT_NAME und AZURE_ACCOUNT_KEY (oder AZURE_ACCOUNT_SAS) in deploy/.env
```

Das Repository wird beim ersten Lauf angelegt. Danach einmal von Hand sichern und prüfen:

```bash
DC="docker compose -f deploy/docker-compose.yml --env-file deploy/.env"
$DC up -d backup
$DC run --rm backup backup      # sofort sichern
$DC run --rm backup snapshots    # vorhandene Stände
$DC logs backup                  # Zeitplan und Ergebnisse
$DC run --rm backup verify      # zusätzlich 10 % der Daten lesen und prüfen (z. B. monatlich)
```

### In der App einrichten

*Verwaltung → System → Datensicherung* (Recht *System*) richtet die Ziele der Sicherung ein, ohne `deploy/.env` zu bearbeiten. Es sind **mehrere Ziele** möglich, z. B. täglich nach Google Drive und wöchentlich auf ein NAS. Jedes Ziel hat seinen eigenen Zeitplan, seine Aufbewahrung, sein Passwort und seinen Status.

- **Ablage:** S3-Speicher, Azure Blob, SFTP-Server, Google Drive oder dieser Server (nur zum Ausprobieren). Die Zugangsdaten und das Passwort der Sicherung werden verschlüsselt in der Datenbank gespeichert (Data Protection), nie angezeigt und tauchen weder im Audit-Log noch im Vollexport auf. Bei einer späteren Änderung bleibt ein leer gelassenes Feld unverändert.
- **Verbindung testen** prüft, ob das Ziel erreichbar ist, das Passwort stimmt und schon ein Repository existiert, ohne etwas zu ändern.
- **Zeitplan je Ziel:** täglich oder wöchentlich (mit Wochentag) zu einer Uhrzeit, dazu die Aufbewahrung. Die App startet die Sicherungen zur eingestellten Zeit (Job `backup`, alle 10 Minuten geprüft), ein Ziel nach dem anderen. Wurde die Zeit verpasst, weil die App nicht lief, holt sie die Sicherung am selben Tag nach. Ein fehlgeschlagener Versuch wird erst zum nächsten Termin wiederholt; die Warnmail (siehe unten) meldet ihn.
- **Fortschritt:** Läuft eine Sicherung oder Wiederherstellung, zeigt die Seite einen Balken mit dem aktuellen Schritt und, beim Übertragen der Dateien, der Datenmenge. Die Seite erkennt auch Läufe, die nach Zeitplan oder von anderen gestartet wurden.
- **Pro Ziel:** *Jetzt sichern* und *Bearbeiten*. Die Tabelle zeigt je Ziel den Ort (Anbieter und Ordner bzw. Bucket), den Zeitplan, die Aufbewahrung und den Stand der letzten Sicherung.
- **Ziele anlegen und ändern** geschieht in einem Dialog (*Ziel hinzufügen*, *Bearbeiten*; dort auch *Ziel entfernen* und *Verbindung testen*), nicht auf der Seite selbst.
- **Sicherung nach `.env`:** Solange kein Ziel in der App angelegt ist, zeigt die Seite das Ziel aus `RESTIC_REPOSITORY` als Zeile *Server-Konfiguration* (Ort ohne Passwort, Uhrzeit aus `BACKUP_TIME`, Aufbewahrung, Stand), mit *Jetzt sichern*, der Liste der Sicherungen und *Wiederherstellen*. *Bearbeiten* öffnet den Dialog mit Anbieter, Ort, Uhrzeit und Aufbewahrung aus der `.env` und übernimmt das Ziel in die App; Zugangsdaten und Passwort der bestehenden Sicherung trägt man dort neu ein. Legt man ein Ziel in der App an, ruht die Sicherung nach `.env` (siehe oben). Der Backup-Dienst bietet dafür `GET /environment` und `POST /environment/{snapshots,backup,restore}`. Darunter listet *Vorhandene Sicherungen* alle Stände aller Ziele (neueste zuerst); jeder Eintrag zeigt die Größe der gesicherten Daten und hat *Wiederherstellen* und *Löschen* (endgültig, mit Bestätigung; gibt den Platz frei, den nur diese Sicherung belegte). Die Größe kennt restic erst ab Version 0.17; ältere Sicherungen zeigen einen Strich. Ein Ziel lässt sich ausschalten oder entfernen; die Sicherungen, die dort schon liegen, bleiben erhalten.
- Speichert man das erste Ziel, **übernimmt die App den Zeitplan**: Die tägliche Sicherung nach den Variablen in `deploy/.env` pausiert (Markierung im Volume `backup-status`), damit nicht doppelt gesichert wird. Ohne Ziele in der App gilt weiter `.env`; entfernt man das letzte Ziel, übernimmt `.env` wieder.
- **Überwachung:** Dashboard, Health-Check und Warnmail melden, sobald **ein** eingeschaltetes Ziel fehlschlägt oder überfällig ist (wöchentliche Ziele erst nach einer Woche und zwei Stunden) und nennen das Ziel.

Die App spricht dazu mit dem Backup-Dienst über eine interne API (`Backup__Url`, `Backup__ApiToken`; Port 8080 nur im Docker-Netz, Anmeldung per Token). Der Dienst speichert keine Zugangsdaten, die App schickt das Ziel bei jedem Aufruf mit; Ergebnisse führt er je Ziel (`backup-<Kennung>.json` im Volume `backup-status`). **Bestehende Installationen:** `BACKUP_API_TOKEN` in `deploy/.env` eintragen und `docker compose … up -d --build backup` ausführen; bis dahin zeigt die Seite, dass der Backup-Dienst nicht erreichbar ist.

#### Google Drive

Bei der Ablage *Google Drive* genügt **„Mit Google anmelden“**: Die App zeigt einen QR-Code und die Adresse `google.com/device`; man scannt den QR-Code mit dem Handy (oder öffnet den Link in einem anderen Fenster), meldet sich bei Google an und erlaubt den Zugriff. Der Code steckt als `user_code` in der Adresse und wird bei Google vorausgefüllt; ist das Feld doch leer (Google dokumentiert den Parameter nicht), steht der Code daneben zum Eintippen. Die App merkt die Bestätigung selbst. Der Ordner (Standard *Elocaro-Sicherung*, Name änderbar) wird in Drive angelegt. Elocaro bekommt nur Zugriff auf Dateien, die es selbst anlegt (Berechtigung `drive.file`), nicht auf das übrige Drive. Das Passwort der Sicherung bleibt wie bei jedem Ziel Pflicht.

Der Zugang erneuert sich selbst, die Sicherung läuft danach ohne Eingriff. Widerruft man ihn im Google-Konto (*Sicherheit → Drittanbieter-Zugriff*), schlägt die Sicherung fehl, und die Warnmail meldet es; dann das Ziel bearbeiten und neu anmelden. Auf einem neuen Server braucht die Wiederherstellung das Passwort der Sicherung und eine neue Anmeldung.

**Der OAuth-Client.** Die Anmeldung läuft über einen OAuth-Client von Google. In den **veröffentlichten Images** (`ghcr.io/elocaro/elocaro`) ist er eingebaut: Die Release-Pipeline setzt ihn beim Bauen aus Repository-Secrets ein, nichts muss eingerichtet werden. Er steht nicht im Quelltext, ist aber im Image lesbar, wie bei jeder App mit eingebautem Client; Google stuft den Schlüssel dieses Client-Typs als nicht vertraulich ein, und der Zugriff reicht nur auf Dateien, die Elocaro selbst anlegt. Die Sicherungen liegen im **eigenen Google-Konto** und zählen zu dessen Speicherplatz; geteilt wird nur das Anfragenlimit, das Google dem Client der App gibt, und das reicht für Sicherungen bei Weitem.

Wer das Image selbst baut (wie die Produktion dieser Installation: `docker compose build`) oder einen eigenen Client möchte, trägt ihn in der Umgebung ein; die Oberfläche zeigt nie eine Client-ID. Die Reihenfolge: **Umgebung**, dann der **eingebaute Client** (ein früher in der App gespeicherter Client gilt weiter und hat Vorrang).

- **Über die Umgebung:** `GOOGLE_DRIVE_CLIENT_ID` und `GOOGLE_DRIVE_CLIENT_SECRET` in `deploy/.env` (landen als `GoogleDrive__ClientId` und `GoogleDrive__ClientSecret` in der App). Wer ihn in der `.env` führt, muss ihn auf einem neuen Server dort wieder eintragen.

Ohne irgendeinen Client ist „Mit Google anmelden“ ausgegraut und verweist auf diesen Abschnitt.

**OAuth-Client anlegen** (einmal; für die Release-Pipeline oder für eine einzelne Installation; etwa zehn Minuten, kostet nichts und braucht keine Abrechnung):

1. In der [Google Cloud Console](https://console.cloud.google.com/) ein **Projekt** anlegen (z. B. „Elocaro“) und die **Google Drive API** aktivieren.
2. *Google Auth Platform → Erste Schritte*: Anwendungsname, Support-E-Mail, Zielgruppe **Extern**, Kontakt-E-Mail.
3. *Datenzugriff → Bereiche hinzufügen*: nur `…/auth/drive.file` („nicht vertraulich“, braucht keine Prüfung durch Google).
4. *Branding*: Startseite, Datenschutz-Link und die zugehörige **autorisierte Domain** eintragen (Google verlangt sie zum Veröffentlichen; sie müssen erreichbar sein). Kein Logo hochladen: Das löst eine Prüfung der App aus.
5. *Zielgruppe → App veröffentlichen* (**In Produktion**). Im Status *Test* läuft der Zugang laut Google nach 7 Tagen ab.
6. *Clients → Client erstellen*, Anwendungstyp **Fernsehgeräte und Geräte mit begrenzter Eingabe**. Client-ID und Client-Schlüssel kopieren (der Schlüssel wird nur einmal angezeigt) und wie oben eintragen.

### Überwachung

Nach jedem Lauf schreibt der Backup-Dienst eine Statusdatei in das Volume `backup-status`, die App liest sie (`Backup__StatusFile`):

- *Einstellungen → Datensicherung* zeigt das Ergebnis der letzten Sicherung.
- Schlägt sie fehl, ist sie älter als 26 Stunden (`Backup__MaxAgeHours`) oder gab es noch keine, erscheint im Dashboard „Datensicherung prüfen“. Alle aktiven Admins erhalten **täglich um 8:05 Uhr eine Warnmail**, bis wieder erfolgreich gesichert wurde. Ein ausgefallener Backup-Container fällt so ebenfalls auf.
- `/health` meldet `Degraded` (HTTP 200, die App bleibt erreichbar); für Konten mit dem Recht System steht dort `backup: Degraded`.

## Wiederherstellen

Die Wiederherstellung ersetzt Datenbank **und** Dateiablage vollständig durch den gewählten Stand.

**In der App:** *Datensicherung → Vorhandene Sicherungen → Wiederherstellen*, zur Bestätigung „WIEDERHERSTELLEN“ eingeben. Was seit dem Stand erfasst wurde, geht verloren. Der Backup-Dienst spielt Datenbank und Dateien ein, die App startet danach von selbst neu (die Data-Protection-Schlüssel und alles Zwischengespeicherte kommen aus dem wiederhergestellten Stand), anschließend neu anmelden. Der Neustart setzt `restart: unless-stopped` voraus (Standard in `deploy/docker-compose.yml`).

**Aus einer Datei:** Jede Sicherung lässt sich als Datei herunterladen und später hochladen, auch auf einem anderen Server.
- *Datensicherung → Vorhandene Sicherungen → Herunterladen* packt den gewählten Stand (je nach Größe etwas Geduld; der Balken zeigt es) und startet danach den Download von selbst: `elocaro-sicherung-<Kennung>.tar`. Der Backup-Dienst löscht die Datei, sobald sie gesendet wurde. Bricht der Download ab, packt man den Stand neu. Eine Datei, die nicht abgeholt wurde (z. B. in einer anderen Sitzung), bleibt höchstens zwei Stunden und erscheint als Hinweis mit *Datei herunterladen* und *Verwerfen*. Das Herunterladen wird im Audit-Log vermerkt.
- **Die Datei ist verschlüsselt:** Sie ist ein eigenständiges restic-Repository mit genau diesem einen Stand, mit demselben Passwort wie die Sicherung. Ohne das Passwort lässt sie sich nicht lesen. Ein Stand aus einem Ziel mit mehreren Ständen belegt als Datei seine volle Größe (keine Deduplizierung über Stände hinweg).
- *Datensicherung → Aus Datei wiederherstellen*: Datei hochladen (nur die `.tar` aus „Herunterladen“; andere Dateien werden abgelehnt), Passwort der Sicherung eingeben, *Datei öffnen* (zeigt Datum und Größe des Stands, ein falsches Passwort wird gemeldet), zur Bestätigung „WIEDERHERSTELLEN“ eingeben. Der Rest läuft wie oben. Die Datei wird beim Hochladen sofort entpackt und nach erfolgreichem Einspielen gelöscht; *Verwerfen* löscht sie vorher.
- Platz: Download und Upload brauchen im Backup-Container Platz in `BACKUP_EXPORT_DIR` (Standard `/cache/export`, das Volume `backup-cache`) in der Größe der Daten.

**Auf der Konsole** (auch ohne laufende App):

```bash
DC="docker compose -f deploy/docker-compose.yml --env-file deploy/.env"
$DC stop app                                  # keine Verbindungen während des Einspielens
$DC run --rm backup snapshots                 # gewünschten Stand wählen
$DC run --rm backup restore          # neuester Stand; oder: restore <ID>
$DC start app
```

Der Befehl holt den Stand aus dem Repository, legt die Datenbank neu an (`DROP DATABASE … WITH (FORCE)`, `pg_restore`), ersetzt den Inhalt des Volumes `files` und setzt die Dateirechte für den App-Benutzer (UID 1654). Es bricht ab, solange die App noch verbunden ist.

### Auf einem neuen Server

**Mit der Ersteinrichtung:** Auf dem frisch installierten Server (wie unter [Installation](installation.md)) bietet die Ersteinrichtung unter dem Formular „Aus einer Datensicherung wiederherstellen“ an. Dort wählt man die Quelle: **Aus einem Ziel** (Cloud oder NAS, wie unten) oder **Aus einer Datei** (die heruntergeladene `.tar`: Einrichtungscode und Datei hochladen, dann Passwort der Sicherung eingeben, *Datei öffnen*, *Diesen Stand wiederherstellen*). Zum Ziel: Einrichtungscode, Ablage, Zugangsdaten und Passwort der Sicherung eingeben, den Stand wählen, fertig: Die App startet neu, danach meldet man sich mit dem bisherigen Konto an. Das geht nur, solange noch kein Konto existiert. Ein [Zertifikat für die Schlüssel](#data-protection-schlüssel-verschlüsseln) muss vorher wieder eingespielt sein, sonst sind gespeicherte Zugangsdaten (Bankzugang, Ziel der Sicherung) nicht lesbar: dann das Ziel in der App neu eingeben.

**Auf der Konsole:**

1. Installation wie oben bis einschließlich `deploy/.env`. Dabei **dasselbe** `RESTIC_PASSWORD`, `RESTIC_REPOSITORY` und die Zugangsdaten eintragen. `POSTGRES_PASSWORD` darf neu sein.
2. Nur die Datenbank starten: `$DC up -d postgres`
3. `$DC run --rm backup restore`
4. `$DC up -d`. Anmeldungen, Konten und Zwei-Faktor-Einstellungen sind erhalten (die Anmelde-Schlüssel liegen in der Datenbank). Wer die Schlüssel mit einem [Zertifikat](#data-protection-schlüssel-verschlüsseln) schützt, muss es vorher wieder einspielen.

### Restore-Test

`deploy/backup/restore-test.sh` spielt den Ablauf vollständig durch und läuft in der CI bei jedem Push: App gegen leere Datenbank starten, Testdaten anlegen, sichern, **alle Volumes löschen**, in eine leere Datenbank wiederherstellen, dann Schema, Inhalt jeder Tabelle (Prüfsumme) sowie Dateien samt Eigentümer vergleichen. Zum Schluss muss die App auf dem wiederhergestellten Stand starten. Lokal:

```bash
docker build -t elocaro:lokal . && docker build -t elocaro-backup:lokal -f deploy/backup/Dockerfile .
deploy/backup/restore-test.sh
```

## Vollexport

Unabhängig vom Backup lässt sich unter *Einstellungen → Datensicherung* (Recht *System*) ein **Vollexport** als ZIP herunterladen. Er ist ohne Elocaro lesbar:

- `data/<table>.json` und `data/<table>.csv` (Semikolon, UTF-8 mit BOM, Dezimalkomma; für Excel) für jede Tabelle, gelöschte Datensätze eingeschlossen,
- `files/<id>/<originalname>` für alle hochgeladenen Dateien,
- `manifest.json` (Tabellen, Spalten, Datentypen, Zeilenzahlen) und `LIESMICH.txt` (Formatbeschreibung).

Nicht enthalten sind Passwort-Hashes, Tokens, Anmeldelinks und Cookie-Schlüssel. Jeder Export wird im Audit-Log vermerkt. Zum Wiederherstellen von Elocaro dient das Backup, nicht der Export.

Der Text ausgehender E-Mails steht nicht im Vollexport, weil er gültige Links enthalten kann. `data/outgoing_mails.*` enthält den Versandnachweis (Empfänger, Betreff, Status, Zeitpunkte, Vorlage, Bezug), die versendeten Dokumente stehen unter `files/`.

## Notfallmappe

Unter *Einstellungen → System → Notfallmappe* (Recht *System*) erzeugt der Vermieter eine **verschlüsselte HTML-Datei** für Angehörige. Sie ergänzt Backup und Vollexport: Backup und Export sind für den Betrieb gedacht, die Notfallmappe ist für Menschen lesbar, ohne Server und ohne Elocaro.

- **Verschlüsselung:** AES-256-GCM (.NET `AesGcm`, zufällige 12-Byte-Nonce), Schlüssel per PBKDF2-SHA256 mit 600.000 Iterationen und zufälligem 16-Byte-Salt aus dem Passwort. Die Parameter stehen unverschlüsselt in der Datei; entschlüsselt wird im Browser mit WebCrypto (`crypto.subtle`), ohne Netzwerk und ohne externe Skripte oder Schriften. Eine Content-Security-Policy in der Datei erlaubt genau ein Skript (per Hash), der Inhalt erscheint in einem `iframe` mit `sandbox` ohne Skriptrecht.
- **Passwort:** mindestens 12 Zeichen, zweimal einzugeben, nur im Request-Body. Es wird nicht gespeichert, geloggt, auditiert oder in einer URL übertragen. Es gibt keine Wiederherstellung und keinen Zweitschlüssel; wer es verliert, verliert die Mappe.
- **Kein Klartext auf der Platte:** Aufbau, Verschlüsselung und Auslieferung laufen im Arbeitsspeicher. Obergrenze für den unverschlüsselten Inhalt samt beigelegten Verträgen: 40 MB (darüber meldet die Seite, dass die Mappe zu groß ist; dann ohne Dokumente erzeugen).
- **Inhalt:** nur fachliche Daten, nie Passwort-Hashes, Tokens, Schlüssel oder Bankzugänge (FinTS). Alle Werte werden beim Erzeugen HTML-kodiert.
- **Hinweistexte** (Tabelle `emergency_folders`) liegen unverschlüsselt in der Datenbank und sind damit auch im Backup und im Vollexport. Dort keine Passwörter eintragen.
- **Protokoll:** Jede Erzeugung schreibt einen Audit-Eintrag (`Notfallmappe`, Aktion *Read*), unabhängig von `Audit:LogAccess`.
- **Aufbewahrung:** Datei und Passwort getrennt aufbewahren, die Datei mindestens einmal im Jahr neu erzeugen. Die Startseite erinnert (Aufgabe „Notfallmappe erstellen/erneuern“, nur mit Recht *System*, nicht auf der Demo-Instanz).
- **Nicht enthalten:** ein automatischer Notfallzugang mit Wartezeit, ein automatischer Versand und ein Schlüssel-Treuhänder. Das wären zusätzliche Zugänge zu allen Daten und brauchen eine eigene Sicherheitsentscheidung.

## Demo-Instanz

Eine öffentliche Demo zeigt Elocaro mit erfundenen Beispieldaten (ein Haus, vier Wohnungen, drei Mieter). Sie gehört **auf einen eigenen Server**, getrennt von echten Daten: Der Demo-Benutzer ist Administrator mit veröffentlichtem Passwort, und Besucher können Dateien hochladen.

1. Server wie unter [Installation](#installation) vorbereiten (Docker, Domain, z. B. `demo.example.de`) und das Repository klonen.
2. `deploy/.env` aus `deploy/.env.example` anlegen: Domain, `POSTGRES_PASSWORD`, `RESTIC_PASSWORD` (wird nicht benutzt) und zusätzlich `DEMO_PASSWORD` (öffentlich) und optional `DEMO_EMAIL`.
3. Starten:

   ```bash
   docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.demo.yml --env-file deploy/.env up -d
   ```

   `Demo__Enabled=true` legt beim ersten Start den Demo-Benutzer an und lädt die Beispieldaten samt versendeter Abrechnungen der letzten zwei Kalenderjahre (antwortet der PDF-Dienst nicht, startet die Demo ohne Abrechnungen und schreibt eine Warnung ins Log). Dazu gibt es je Rolle einen Demo-Benutzer (Verwalter, Hausmeister, Handwerker, Eigentümer, Mieter) mit demselben Passwort; die Anmeldeseite listet alle Konten auf. Zwei-Faktor ist abgeschaltet, Passwort und Zwei-Faktor lassen sich nicht ändern, die Sicherungsmeldung entfällt. Mail-Versand, Datensicherung und FinTS sind aus; die Seiten sind für Suchmaschinen gesperrt (`deploy/Caddyfile.demo`).
4. Jede Nacht zurücksetzen (Cron): `30 3 * * * /pfad/zu/elocaro/deploy/demo/reset.sh >> /var/log/elocaro-demo-reset.log 2>&1`. Das Skript löscht Datenbank und Dateien und startet neu mit frischen Beispieldaten; das Zertifikat bleibt erhalten.

Den Demo-Schalter **nie** auf einer Instanz mit echten Daten setzen.

## Hosting in Azure

Eine Anleitung für den Betrieb auf einer Azure-VM (Ressourcen, Firewall, Blob-Backup, Budget) steht in [azure.md](azure.md).

## Traefik statt Caddy

Läuft auf dem Server schon Traefik, übernimmt es TLS und Weiterleitung; Caddy wird dann nicht gestartet. Dazu die Override-Datei dazunehmen:

```bash
docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.traefik.yml --env-file deploy/.env up -d
```

Sie hängt die App an das externe Traefik-Netz und setzt Router, Zertifikats-Resolver sowie dieselben Regeln wie das Caddyfile (HSTS, Uploads bis 30 MB, Kompression, Gesundheitsprüfung auf `/health/live`). Namen aus `deploy/.env` (Vorgaben in Klammern): `TRAEFIK_NETWORK` (`traefik`), `TRAEFIK_ENTRYPOINT` (`websecure`), `TRAEFIK_CERTRESOLVER` (`letsencrypt`). Die Umleitung von HTTP auf HTTPS gehört in die Traefik-Konfiguration. Die App übernimmt `X-Forwarded-*` nur von Absendern aus privaten Netzen; Traefik im Docker-Netz erfüllt das.

## Lokal ausprobieren

```bash
docker build -t elocaro:lokal .
ELOCARO_DOMAIN=localhost ELOCARO_IMAGE=elocaro:lokal POSTGRES_PASSWORD=test ACME_EMAIL=a@b.de \
  MAIL_SENDER=test@localhost SMTP_HOST=localhost \
  docker compose -f deploy/docker-compose.yml up -d --no-build
```

Für `localhost` stellt Caddy ein Zertifikat seiner internen CA aus (Browserwarnung bestätigen).

## Vorlagen und Schreiben an Mieter

- **Vorlagen** (Einstellungen → Vorlagen): eigene Texte für Schreiben an Mieter und wiederverwendbare **Bausteine** (z. B. Grußformel, Bankverbindung). Die Standardvorlagen legt Elocaro bei jedem Start selbst an, was fehlt (auch neue nach einem Update); eine bewusst gelöschte bleibt gelöscht. Die Texte sind Ausgangspunkte und **keine Rechtsberatung**, bitte vor dem Einsatz prüfen und anpassen. Zu den Standardvorlagen für Schreiben gehören die **Wohnungsgeberbestätigung** für Einzug und Auszug (§ 19 BMG, binnen zwei Wochen auszustellen, Ordnungswidrigkeit bis 1.000 €) und die Bestätigung der Kündigung. Dazu kommen die **Mietbescheinigung** (Miete, Nebenkosten, Wohnfläche, z. B. für Wohngeld), die **Mietschuldenfreiheitsbescheinigung** (nimmt die fälligen Forderungen aus dem Mieterkonto; bei Rückstand erscheint stattdessen ein Hinweis) und die **Kautionsabrechnung** (Eingang, Zinsen, Verrechnungen, Erstattung aus der Kautionsführung). Die Beträge gelten zum Tag der Erstellung; erzeugt werden sie unter *Kommunikation → Dokumente → Schreiben an Mieter*. Die Übergabeprotokoll-Seite erinnert daran. Die Vorlage **Änderung des Umlageschlüssels (§ 556a Abs. 2 BGB)** dient der Erklärung, z. B. Müll künftig nach Personen umzulegen: Sie muss dem Mieter vor Beginn des Abrechnungszeitraums in Textform zugehen und gilt nur für künftige Zeiträume. Zum Eigentumsübergang gibt es die Vorlage **Information Vermieterwechsel (§ 566 BGB)**: Vorher das Vermieterprofil des Erwerbers anlegen und dem Objekt zuordnen, dann das Schreiben an alle Mieter des Objekts erzeugen.
- Im Text stehen Platzhalter wie `{{ salutation }}` oder `{{ tenant_name }}`; die Liste steht unter dem Editor. Bausteine fügt `{{ include 'Grußformel' }}` ein. Die Vorschau zeigt das PDF mit Beispieldaten und benötigt den PDF-Dienst und das Vermieterprofil.
- Jede Änderung des Textes bleibt als **Fassung** erhalten und lässt sich in den Editor zurückladen. Schon erzeugte Schreiben ändern sich nie; sie verweisen auf die Fassung, aus der sie entstanden.
- **Schreiben an Mieter** (Kommunikation → Dokumente → Schreiben an Mieter): Vorlage und Objekt wählen, Empfänger abhaken (Standard: alle laufenden Mietverträge), optional per E-Mail senden und im Mieterportal freigeben. Je Mieter entsteht ein PDF bei den Dokumenten; ein Fehler bei einem Mieter hält die anderen nicht auf. Mieter ohne E-Mail-Adresse bekommen nur das PDF (per Post senden).
- **Austausch und Sicherung:** Vorlagen lassen sich einzeln als Datei exportieren und in einer anderen Installation importieren. Im Gesamtexport und in der Datensicherung sind sie wie alle Tabellen enthalten.

## Mietvertrag erstellen

- Unter **Verträge → Vertrag → Vertragsdokument** wird aus den Stammdaten ein Entwurf als PDF erzeugt: Parteien, Mietobjekt und Fläche, Miete (Standard, Staffel oder Index), Vorauszahlung oder Pauschale, vereinbarte Betriebskostenarten, Kaution, dazu Hausordnung, Energieausweis-Hinweis, Datenschutzinformation und Übergabeprotokoll als Anlagen. Für ein WG-Zimmer wird der **Zimmervertrag** gewählt (Zimmer, Anteil der Gemeinschaftsflächen, Pauschale, Möblierungsliste).
- Vorher die Standardvorlagen (Einstellungen → Vorlagen) prüfen: Sie sind **Basisvorlagen und keine Rechtsberatung**, insbesondere zu Schönheitsreparaturen, Kleinreparaturen, Befristung und Kündigung. Die Möblierungsliste eines Zimmers pflegt man am Zimmer.
- Der Entwurf wird ausgedruckt und unterschrieben. Den Scan lädt man am selben Ort hoch („Unterschrieben hochladen“); er landet bei den Dokumenten des Vertrags und der Vertrag gilt als abgeschlossen. Ein neuer Entwurf ersetzt einen noch offenen.

## Mieterhöhung

- Am Vertrag gibt es **Mieterhöhung**. Der Assistent zeigt die Rahmendaten (Mietmodell, Miete, letzte Erhöhung, Kappungsgrenze, frühester Zugang) und führt durch die vier Fälle: **Vergleichsmiete** (§ 558 BGB), **Indexmiete** (§ 557b), **Staffel** (Information) und **Betriebskostenpauschale** (§ 560).
- **Vergleichsmiete:** Mietspiegel (Spanne und Einordnung der Wohnung in €/m²), drei Vergleichswohnungen oder Gutachten eingeben. „Prüfen“ zeigt, ob 15 Monate und das Jahr seit der letzten Erhöhung eingehalten sind, ob die Kappungsgrenze (20 %, bzw. 15 % wo das am Objekt eingestellt ist) und die Vergleichsmiete passen, und wie hoch die Miete höchstens sein darf. Die Kappungsgrenze stellt man am **Objekt** ein.
- **Modernisierung (§ 559 BGB):** Maßnahme beschreiben, die auf die Wohnung entfallenden Kosten und den Instandhaltungsanteil eingeben. Elocaro rechnet 8 % der umlagefähigen Kosten im Jahr, begrenzt auf 3 € je m² in sechs Jahren (2 € bei einer Miete unter 7 €/m², frühere Erhöhungen zählen mit). Das **vereinfachte Verfahren** (§ 559c, bis 10.000 €, pauschal 30 % Abzug) ist eine Option. Wurde die Maßnahme nicht angekündigt oder wurden die angekündigten Kosten um mehr als 10 % überschritten, beginnt die Erhöhung sechs Monate später. Die Erhöhung braucht keine Zustimmung: mit dem erfassten Zugang steht die neue Miete im Vertrag. Das Schreiben enthält Berechnung, Kappungsgrenze, Fristen und den Hinweis zum Härteeinwand; **die Vorlage muss rechtlich geprüft werden**.
- Nach „Schreiben erzeugen“ liegt das PDF bei den Dokumenten des Vertrags. Nach dem Versand das Datum des **Zugangs** erfassen; daraus berechnet Elocaro die Termine (Zustimmung bis Ende des zweiten Kalendermonats, Wirkung ab dem dritten). Stimmt der Mieter zu, **Zustimmung erfassen**: die neue Miete steht dann ab dem Wirkungstag im Vertrag. Bei Indexmiete und Pauschale wird die neue Miete schon mit dem Zugang eingetragen, weil keine Zustimmung nötig ist.
- Die Texte sind **Basisvorlagen und keine Rechtsberatung** (Einstellungen → Vorlagen → Standardvorlagen). Bitte rechtlich prüfen lassen; das Gutachten oder die Mietspiegelauszüge sind dem Schreiben beizufügen.

Mieter können den Entwurf auch **digital unterschreiben** (einfache elektronische Unterschrift mit persönlichem Link und Protokollseite); Voraussetzungen und Grenzen stehen in [digitale-unterschrift.md](digitale-unterschrift.md).

## Übergabeprotokoll

- Am Vertrag gibt es **Übergabe Einzug** und **Übergabe Auszug**. Das Protokoll wird mit den Räumen, Schlüsselarten und Zählern aus den Stammdaten vorbelegt und lässt sich bequem am **Smartphone** ausfüllen: Zustand je Raum und Bauteil, Mängel mit Fotos (die Kamera öffnet sich direkt), Schlüssel, Zählerstände mit Foto.
- Beim **Auszug** startet das Protokoll mit dem Stand vom Einzug. Der Vergleich zeigt, welche Mängel neu sind, was sich verschlechtert hat und welche Schlüssel fehlen. Außerdem wird die **neue Anschrift** der Mieter erfasst (sie wird beim Unterschreiben bei den Mietern hinterlegt) und im Protokoll auf die spätere Nebenkostenabrechnung und die Kautionsabrechnung hingewiesen.
- **Unterschreiben** kann der Mieter direkt auf dem Gerät (Finger oder Stift), oder man druckt die PDF-Vorschau aus und legt den unterschriebenen Scan ab. Das Protokoll liegt danach bei den Dokumenten des Vertrags und im Mieterportal. Die Zählerstände werden als **Zwischenablesung** zum Übergabetag erfasst und in der Nebenkostenabrechnung verwendet; läuft ein Zähler rückwärts oder weicht stark ab, fragt Elocaro vor dem Ablegen nach.
- Ein unterschriebenes Protokoll ist unveränderlich. Fehler im Entwurf lassen sich vor dem Unterschreiben korrigieren.
