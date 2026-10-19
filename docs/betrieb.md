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
| `app` | `ghcr.io/ther00st3r/miethahn` (chiseled, Non-Root, ohne Shell) | Volume `dateien` → `/daten` (Belege, Fotos, PDFs) |
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

Datenbank-Migrationen laufen beim Start automatisch. Vorher sichern: `docker compose … run --rm backup sichern`. Für reproduzierbare Updates `MIETHAHN_IMAGE` auf eine Version setzen und gezielt hochzählen.

### PostgreSQL-Major-Upgrade (z. B. 18 → 19)

Das Datenformat ist zwischen Major-Versionen nicht kompatibel:
1. Sichern: `docker compose … run --rm backup sichern`.
2. `docker compose … down`, Volume `miethahn_postgres` umbenennen oder löschen.
3. Image von `postgres` **und** die Basis in `deploy/backup/Dockerfile` anheben, `up -d postgres`.
4. `docker compose … run --rm backup wiederherstellen`, dann `up -d`.

## Sicherheit

- TLS und HSTS über Caddy; die App setzt CSP, `X-Frame-Options: DENY`, `nosniff`, Referrer- und Permissions-Policy.
- Die App übernimmt Schema und Client-IP aus `X-Forwarded-*` nur von Absendern aus privaten Netzen (Docker-Netz). Die HTTPS-Umleitung macht Caddy (`Hosting__HttpsRedirection=false` im Image).
- **Data-Protection-Schlüssel** (Cookies, Tokens) liegen in der Tabelle `data_protection_keys`. Anmeldungen überstehen so Neustarts und sind im DB-Backup enthalten. Die Schlüssel sind dort nicht zusätzlich verschlüsselt. Wer Zugriff auf die Datenbank hat, hat ohnehin Zugriff auf alle Daten; die Datenbank ist daher nur intern erreichbar. Die Warnung „No XML encryptor configured“ beim ersten Start ist erwartet.
- Uploads: Caddy begrenzt auf 30 MB, die App zusätzlich auf `FileStore__MaxSizeMb` (25).
- Geheimnisse nur in `deploy/.env` (per `.gitignore` ausgeschlossen), Dateirechte `chmod 600 deploy/.env`.

## Backup

Der Dienst `backup` (Image `ghcr.io/ther00st3r/miethahn-backup`, Quelle `deploy/backup/`) sichert jede Nacht um `BACKUP_TIME` (Standard 02:30):

1. die Datenbank per `pg_dump` (Custom-Format; passt zur PostgreSQL-Version, da das Image auf `postgres:18-alpine` basiert),
2. das Volume `dateien` (Belege, Fotos, PDFs),

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
$DC run --rm backup sichern      # sofort sichern
$DC run --rm backup snapshots    # vorhandene Stände
$DC logs backup                  # Zeitplan und Ergebnisse
$DC run --rm backup pruefen      # zusätzlich 10 % der Daten lesen und prüfen (z. B. monatlich)
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
$DC run --rm backup wiederherstellen          # neuester Stand; oder: wiederherstellen <ID>
$DC start app
```

Das Skript holt den Stand aus dem Repository, legt die Datenbank neu an (`DROP DATABASE … WITH (FORCE)`, `pg_restore`), ersetzt den Inhalt des Volumes `dateien` und setzt die Dateirechte für den App-Benutzer (UID 1654). Es bricht ab, solange die App noch verbunden ist.

### Auf einem neuen Server

1. Installation wie oben bis einschließlich `deploy/.env`. Dabei **dasselbe** `RESTIC_PASSWORD`, `RESTIC_REPOSITORY` und die Zugangsdaten eintragen. `POSTGRES_PASSWORD` darf neu sein.
2. Nur die Datenbank starten: `$DC up -d postgres`
3. `$DC run --rm backup wiederherstellen`
4. `$DC up -d`. Anmeldungen, Konten und Zwei-Faktor-Einstellungen sind erhalten (die Anmelde-Schlüssel liegen in der Datenbank).

### Restore-Test

`deploy/backup/restore-test.sh` spielt den Ablauf vollständig durch und läuft in der CI bei jedem Push: App gegen leere Datenbank starten, Testdaten anlegen, sichern, **alle Volumes löschen**, in eine leere Datenbank wiederherstellen, dann Schema, Inhalt jeder Tabelle (Prüfsumme) sowie Dateien samt Eigentümer vergleichen. Zum Schluss muss die App auf dem wiederhergestellten Stand starten. Lokal:

```bash
docker build -t miethahn:lokal . && docker build -t miethahn-backup:lokal deploy/backup
deploy/backup/restore-test.sh
```

## Vollexport

Unabhängig vom Backup lässt sich unter *Einstellungen → Datensicherung* (Recht *System*) ein **Vollexport** als ZIP herunterladen. Er ist ohne MietHahn lesbar:

- `daten/<tabelle>.json` und `daten/<tabelle>.csv` (Semikolon, UTF-8 mit BOM, Dezimalkomma; für Excel) für jede Tabelle, gelöschte Datensätze eingeschlossen,
- `dateien/<id>/<originalname>` für alle hochgeladenen Dateien,
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
