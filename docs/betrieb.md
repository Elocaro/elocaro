# Betrieb (VPS mit Docker Compose)

## Überblick

```
Internet ──443/80──▶ caddy (TLS, Let's Encrypt) ──▶ app:8080 (MietHahn)
                                                      ├─▶ postgres:5432 (nur intern)
                                                      └─▶ gotenberg:3000 (nur intern)
backup (nachts) ──▶ postgres + Volume dateien ──▶ restic-Repository (S3/SFTP/…)
```

| Dienst | Image | Daten |
|---|---|---|
| `app` | `ghcr.io/ther00st3r/miethahn` (chiseled, Non-Root, ohne Shell) | Volume `files` → `/data` (Belege, Fotos, PDFs) |
| `postgres` | `postgres:18-alpine` | Volume `postgres` (Daten inkl. Anmelde-Schlüssel) |
| `gotenberg` | `gotenberg/gotenberg:8.38.1` | – |
| `backup` | `ghcr.io/ther00st3r/miethahn-backup` (pg_dump + restic) | Volumes `backup-status`, `backup-cache`; Repository extern bzw. `deploy/backup-repo` |
| `caddy` | `caddy:2.10-alpine` | Volumes `caddy-data`, `caddy-config` (Zertifikate) |

PostgreSQL und Gotenberg liegen im Netz `intern` ohne Internetzugang. Die App ist zusätzlich im Netz `extern` (ausgehend für SMTP, später FinTS und KI-API). Von außen erreichbar ist nur Caddy.

## Voraussetzungen

- VPS mit Linux, ≥ 2 GB RAM (Gotenberg/Chromium braucht ~500 MB), ≥ 20 GB Platte
- Docker Engine mit Compose-Plugin
- Domain mit A/AAAA-Eintrag auf den Server, Ports 80 und 443 offen
- SMTP-Zugang (z. B. vom Mailanbieter der Domain)

## Installation

```bash
git clone https://github.com/TheR00st3r/MietHahn.git && cd MietHahn
cp deploy/.env.example deploy/.env
nano deploy/.env        # Domain, Passwörter, SMTP eintragen (Datenbankpasswort: openssl rand -base64 32)
docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d
docker compose -f deploy/docker-compose.yml --env-file deploy/.env ps
```

Nach 1–2 Minuten ist MietHahn unter `https://<domain>` erreichbar. Beim ersten Aufruf führt die Seite durch die **Ersteinrichtung** (Admin-Konto, danach Zwei-Faktor-Anmeldung).

Anschließend:
1. Unter *Einstellungen → E-Mail-Versand* eine Testnachricht senden.
2. *Einstellungen → Vermieter* pflegen. Die Angaben erscheinen in Briefkopf und Fußzeile von Dokumenten.
3. Backup einrichten und eine erste Sicherung anstoßen (siehe [Backup](#backup)).

## Konfiguration

Alle Einstellungen kommen über Umgebungsvariablen in `deploy/.env` (eingetragen in `deploy/docker-compose.yml`). Weitere Schlüssel lassen sich im Abschnitt `environment` des Dienstes `app` ergänzen; `__` trennt Ebenen (z. B. `SignIn__RequestsPerMinute=20`).

| Variable | Bedeutung |
|---|---|
| `MIETHAHN_DOMAIN`, `ACME_EMAIL` | Domain und Kontakt für Let's Encrypt; daraus wird `Application__BaseUrl` (Links in Mails) |
| `POSTGRES_PASSWORD` | Datenbankpasswort |
| `MAIL_*`, `SMTP_*` | Absender und SMTP-Zugang (`SMTP_SECURITY`: `StartTls` für Port 587, `SslOnConnect` für 465) |
| `MIETHAHN_IMAGE` | Image-Tag, Standard `ghcr.io/ther00st3r/miethahn:latest`; empfohlen: feste Version |

## Belegauslesung (optional, KI)

Beim Erfassen eines Belegs kann MietHahn aus dem hochgeladenen PDF bzw. Foto einen **Vorschlag** auslesen (Lieferant, IBAN, Rechnungsnummer und -datum, Leistungszeitraum, Brutto/Netto/USt, Kostenart, § 35a, Brennstoffmenge und CO₂). Der Vorschlag füllt nur das Formular vor; gespeichert wird erst, wenn Sie bestätigen. Unsicher erkannte Felder sind gelb markiert, Unstimmigkeiten (Netto + USt ≠ Brutto, ungewöhnlicher Steuersatz, ungültige IBAN, unmögliche Daten) stehen als Warnung darüber.

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
- **Lernen:** Wird ein Beleg mit Kostenart gespeichert, merkt sich MietHahn „Lieferant → Kostenart“ (Tabelle `supplier_cost_type_rules`, im Änderungsprotokoll und in der Datensicherung). Beim nächsten Beleg dieses Lieferanten hat die gelernte Kostenart Vorrang vor dem Modell.

Beispiel `deploy/.env` für Claude:

```
RECEIPT_AI_PROVIDER=Claude
RECEIPT_AI_ACCEPT_EXTERNAL=true
ANTHROPIC_API_KEY=sk-ant-...
```

## Gesundheit & Logs

```bash
curl https://<domain>/health        # {"status":"Healthy","checks":{"database":…,"gotenberg":…,"file-store":…,"backup":…}}
docker compose -f deploy/docker-compose.yml --env-file deploy/.env logs -f app
```

- `/health/live` meldet, ob der Prozess antwortet. Darauf prüfen Docker (`HEALTHCHECK` per `dotnet MietHahn.Web.dll healthcheck`) und Caddy.
- `/health` prüft Datenbank, Gotenberg und ob die Dateiablage beschreibbar ist. Antwort 503 bei Problemen, ohne Fehlerdetails.

## Update

```bash
docker compose -f deploy/docker-compose.yml --env-file deploy/.env pull
docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d
```

Datenbank-Migrationen laufen beim Start automatisch. Vorher sichern: `docker compose … run --rm backup backup`. Für reproduzierbare Updates `MIETHAHN_IMAGE` auf eine Version setzen und gezielt hochzählen.

### PostgreSQL-Major-Upgrade (z. B. 18 → 19)

Das Datenformat ist zwischen Major-Versionen nicht kompatibel:
1. Sichern: `docker compose … run --rm backup backup`.
2. `docker compose … down`, Volume `miethahn_postgres` umbenennen oder löschen.
3. Image von `postgres` **und** die Basis in `deploy/backup/Dockerfile` anheben, `up -d postgres`.
4. `docker compose … run --rm backup restore`, dann `up -d`.

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

Auf der Startseite erinnert ab Oktober des Folgejahres eine Aufgabe an die Abrechnung des Vorjahres, 6, 4 und 2 Wochen vor dem 31.12. wird sie dringlicher (§ 556 Abs. 3 BGB).

> Die mitgelieferte Abrechnungsvorlage (`Pdf/Templates/statement.sbn`, `Mail/Templates/statement.sbn`) ist eine **Basisvorlage und keine Rechtsberatung**. Texte vor dem produktiven Einsatz rechtlich prüfen lassen. Die CO₂- und Gradtagstabellen der Heizkostenabrechnung sind „Stand/prüfen“ (siehe Entwicklung).

## Sicherheit

- TLS und HSTS über Caddy; die App setzt CSP, `X-Frame-Options: DENY`, `nosniff`, Referrer- und Permissions-Policy.
- Die App übernimmt Schema und Client-IP aus `X-Forwarded-*` nur von Absendern aus privaten Netzen (Docker-Netz). Die HTTPS-Umleitung macht Caddy (`Hosting__HttpsRedirection=false` im Image).
- **Data-Protection-Schlüssel** (Cookies, Tokens) liegen in der Tabelle `data_protection_keys`. Anmeldungen überstehen so Neustarts und sind im DB-Backup enthalten. Die Schlüssel sind dort nicht zusätzlich verschlüsselt. Wer Zugriff auf die Datenbank hat, hat ohnehin Zugriff auf alle Daten; die Datenbank ist daher nur intern erreichbar. Die Warnung „No XML encryptor configured“ beim ersten Start ist erwartet.
- Uploads: Caddy begrenzt auf 30 MB, die App zusätzlich auf `FileStore__MaxSizeMb` (25).
- Geheimnisse nur in `deploy/.env` (per `.gitignore` ausgeschlossen), Dateirechte `chmod 600 deploy/.env`.

## Backup

Der Dienst `backup` (Image `ghcr.io/ther00st3r/miethahn-backup`, Quelle `deploy/backup/`) sichert jede Nacht um `BACKUP_TIME` (Standard 02:30):

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

Beispiele für `RESTIC_REPOSITORY` (alle von restic unterstützten Ziele funktionieren):

```
s3:https://s3.eu-central-1.amazonaws.com/mein-bucket/miethahn     # AWS, Hetzner Object Storage, Wasabi, MinIO …
sftp:backup@nas.example.de:/volume1/miethahn                      # SFTP: Schlüssel nach deploy/backup-ssh/ (id_ed25519 + known_hosts)
rest:https://benutzer:passwort@backup.example.de/miethahn          # restic REST-Server
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

### Überwachung

Nach jedem Lauf schreibt der Backup-Dienst eine Statusdatei in das Volume `backup-status`, die App liest sie (`Backup__StatusFile`):

- *Einstellungen → Datensicherung* zeigt das Ergebnis der letzten Sicherung.
- Schlägt sie fehl, ist sie älter als 26 Stunden (`Backup__MaxAgeHours`) oder gab es noch keine, erscheint im Dashboard „Datensicherung prüfen“. Alle aktiven Admins erhalten **täglich um 8:05 Uhr eine Warnmail**, bis wieder erfolgreich gesichert wurde. Ein ausgefallener Backup-Container fällt so ebenfalls auf.
- `/health` meldet `backup: Degraded` (HTTP 200, die App bleibt erreichbar).

## Wiederherstellen

Die Wiederherstellung ersetzt Datenbank **und** Dateiablage vollständig durch den gewählten Stand.

```bash
DC="docker compose -f deploy/docker-compose.yml --env-file deploy/.env"
$DC stop app                                  # keine Verbindungen während des Einspielens
$DC run --rm backup snapshots                 # gewünschten Stand wählen
$DC run --rm backup restore          # neuester Stand; oder: restore <ID>
$DC start app
```

Das Skript holt den Stand aus dem Repository, legt die Datenbank neu an (`DROP DATABASE … WITH (FORCE)`, `pg_restore`), ersetzt den Inhalt des Volumes `files` und setzt die Dateirechte für den App-Benutzer (UID 1654). Es bricht ab, solange die App noch verbunden ist.

### Auf einem neuen Server

1. Installation wie oben bis einschließlich `deploy/.env`. Dabei **dasselbe** `RESTIC_PASSWORD`, `RESTIC_REPOSITORY` und die Zugangsdaten eintragen. `POSTGRES_PASSWORD` darf neu sein.
2. Nur die Datenbank starten: `$DC up -d postgres`
3. `$DC run --rm backup restore`
4. `$DC up -d`. Anmeldungen, Konten und Zwei-Faktor-Einstellungen sind erhalten (die Anmelde-Schlüssel liegen in der Datenbank).

### Restore-Test

`deploy/backup/restore-test.sh` spielt den Ablauf vollständig durch und läuft in der CI bei jedem Push: App gegen leere Datenbank starten, Testdaten anlegen, sichern, **alle Volumes löschen**, in eine leere Datenbank wiederherstellen, dann Schema, Inhalt jeder Tabelle (Prüfsumme) sowie Dateien samt Eigentümer vergleichen. Zum Schluss muss die App auf dem wiederhergestellten Stand starten. Lokal:

```bash
docker build -t miethahn:lokal . && docker build -t miethahn-backup:lokal deploy/backup
deploy/backup/restore-test.sh
```

## Vollexport

Unabhängig vom Backup lässt sich unter *Einstellungen → Datensicherung* (Recht *System*) ein **Vollexport** als ZIP herunterladen. Er ist ohne MietHahn lesbar:

- `data/<table>.json` und `data/<table>.csv` (Semikolon, UTF-8 mit BOM, Dezimalkomma; für Excel) für jede Tabelle, gelöschte Datensätze eingeschlossen,
- `files/<id>/<originalname>` für alle hochgeladenen Dateien,
- `manifest.json` (Tabellen, Spalten, Datentypen, Zeilenzahlen) und `LIESMICH.txt` (Formatbeschreibung).

Nicht enthalten sind Passwort-Hashes, Tokens, Anmeldelinks und Cookie-Schlüssel. Jeder Export wird im Audit-Log vermerkt. Zum Wiederherstellen von MietHahn dient das Backup, nicht der Export.

## Lokal ausprobieren

```bash
docker build -t miethahn:lokal .
MIETHAHN_DOMAIN=localhost MIETHAHN_IMAGE=miethahn:lokal POSTGRES_PASSWORD=test ACME_EMAIL=a@b.de \
  MAIL_SENDER=test@localhost SMTP_HOST=localhost \
  docker compose -f deploy/docker-compose.yml up -d --no-build
```

Für `localhost` stellt Caddy ein Zertifikat seiner internen CA aus (Browserwarnung bestätigen).

## Vorlagen und Schreiben an Mieter

- **Vorlagen** (Einstellungen → Vorlagen): eigene Texte für Schreiben an Mieter und wiederverwendbare **Bausteine** (z. B. Grußformel, Bankverbindung). Beim ersten Mal „Standardvorlagen anlegen“ wählen; die Texte sind Ausgangspunkte und **keine Rechtsberatung**, bitte vor dem Einsatz prüfen und anpassen.
- Im Text stehen Platzhalter wie `{{ salutation }}` oder `{{ tenant_name }}`; die Liste steht unter dem Editor. Bausteine fügt `{{ include 'Grußformel' }}` ein. Die Vorschau zeigt das PDF mit Beispieldaten und benötigt den PDF-Dienst und das Vermieterprofil.
- Jede Änderung des Textes bleibt als **Fassung** erhalten und lässt sich in den Editor zurückladen. Schon erzeugte Schreiben ändern sich nie; sie verweisen auf die Fassung, aus der sie entstanden.
- **Schreiben an Mieter** (Dokumente → Schreiben an Mieter): Vorlage und Objekt wählen, Empfänger abhaken (Standard: alle laufenden Mietverträge), optional per E-Mail senden und im Mieterportal freigeben. Je Mieter entsteht ein PDF bei den Dokumenten; ein Fehler bei einem Mieter hält die anderen nicht auf. Mieter ohne E-Mail-Adresse bekommen nur das PDF (per Post senden).
- **Austausch und Sicherung:** Vorlagen lassen sich einzeln als Datei exportieren und in einer anderen Installation importieren. Im Gesamtexport und in der Datensicherung sind sie wie alle Tabellen enthalten.

## Mietvertrag erstellen

- Unter **Verträge → Vertrag → Vertragsdokument** wird aus den Stammdaten ein Entwurf als PDF erzeugt: Parteien, Mietobjekt und Fläche, Miete (Standard, Staffel oder Index), Vorauszahlung oder Pauschale, vereinbarte Betriebskostenarten, Kaution, dazu Hausordnung, Energieausweis-Hinweis, Datenschutzinformation und Übergabeprotokoll als Anlagen. Für ein WG-Zimmer wird der **Zimmervertrag** gewählt (Zimmer, Anteil der Gemeinschaftsflächen, Pauschale, Möblierungsliste).
- Vorher die Standardvorlagen anlegen (Einstellungen → Vorlagen → „Standardvorlagen anlegen“) und prüfen: Sie sind **Basisvorlagen und keine Rechtsberatung**, insbesondere zu Schönheitsreparaturen, Kleinreparaturen, Befristung und Kündigung. Die Möblierungsliste eines Zimmers pflegt man am Zimmer.
- Der Entwurf wird ausgedruckt und unterschrieben. Den Scan lädt man am selben Ort hoch („Unterschrieben hochladen“); er landet bei den Dokumenten des Vertrags und der Vertrag gilt als abgeschlossen. Ein neuer Entwurf ersetzt einen noch offenen.

## Mieterhöhung

- Am Vertrag gibt es **Mieterhöhung**. Der Assistent zeigt die Rahmendaten (Mietmodell, Miete, letzte Erhöhung, Kappungsgrenze, frühester Zugang) und führt durch die vier Fälle: **Vergleichsmiete** (§ 558 BGB), **Indexmiete** (§ 557b), **Staffel** (Information) und **Betriebskostenpauschale** (§ 560).
- **Vergleichsmiete:** Mietspiegel (Spanne und Einordnung der Wohnung in €/m²), drei Vergleichswohnungen oder Gutachten eingeben. „Prüfen“ zeigt, ob 15 Monate und das Jahr seit der letzten Erhöhung eingehalten sind, ob die Kappungsgrenze (20 %, bzw. 15 % wo das am Objekt eingestellt ist) und die Vergleichsmiete passen, und wie hoch die Miete höchstens sein darf. Die Kappungsgrenze stellt man am **Objekt** ein.
- Nach „Schreiben erzeugen“ liegt das PDF bei den Dokumenten des Vertrags. Nach dem Versand das Datum des **Zugangs** erfassen; daraus berechnet MietHahn die Termine (Zustimmung bis Ende des zweiten Kalendermonats, Wirkung ab dem dritten). Stimmt der Mieter zu, **Zustimmung erfassen**: die neue Miete steht dann ab dem Wirkungstag im Vertrag. Bei Indexmiete und Pauschale wird die neue Miete schon mit dem Zugang eingetragen, weil keine Zustimmung nötig ist.
- Die Texte sind **Basisvorlagen und keine Rechtsberatung** (Einstellungen → Vorlagen → Standardvorlagen). Bitte rechtlich prüfen lassen; das Gutachten oder die Mietspiegelauszüge sind dem Schreiben beizufügen.
