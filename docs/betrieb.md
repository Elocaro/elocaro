# Betrieb (VPS mit Docker Compose)

## Überblick

```
Internet ──443/80──▶ caddy (TLS, Let's Encrypt) ──▶ app:8080 (MietHahn)
                                                      ├─▶ postgres:5432 (nur intern)
                                                      └─▶ gotenberg:3000 (nur intern)
```

| Dienst | Image | Daten |
|---|---|---|
| `app` | `ghcr.io/ther00st3r/miethahn` (chiseled, Non-Root, ohne Shell) | Volume `dateien` → `/daten` (Belege, Fotos, PDFs) |
| `postgres` | `postgres:18-alpine` | Volume `postgres` (Daten inkl. Anmelde-Schlüssel) |
| `gotenberg` | `gotenberg/gotenberg:8.38.1` | – |
| `caddy` | `caddy:2.10-alpine` | Volumes `caddy-daten`, `caddy-konfig` (Zertifikate) |

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
3. Backups einrichten (siehe unten).

## Konfiguration

Alle Einstellungen kommen über Umgebungsvariablen in `deploy/.env` (eingetragen in `deploy/docker-compose.yml`). Weitere Schlüssel lassen sich im Abschnitt `environment` des Dienstes `app` ergänzen; `__` trennt Ebenen (z. B. `Anmeldung__AnfragenProMinute=20`).

| Variable | Bedeutung |
|---|---|
| `MIETHAHN_DOMAIN`, `ACME_EMAIL` | Domain und Kontakt für Let's Encrypt; daraus wird `Anwendung__BasisUrl` (Links in Mails) |
| `POSTGRES_PASSWORT` | Datenbankpasswort |
| `MAIL_*`, `SMTP_*` | Absender und SMTP-Zugang (`SMTP_SICHERHEIT`: `StartTls` für Port 587, `SslOnConnect` für 465) |
| `MIETHAHN_IMAGE` | Image-Tag, Standard `ghcr.io/ther00st3r/miethahn:latest`; empfohlen: feste Version |

## Gesundheit & Logs

```bash
curl https://<domain>/health        # {"status":"Healthy","pruefungen":{"datenbank":…,"gotenberg":…,"dateiablage":…}}
docker compose -f deploy/docker-compose.yml --env-file deploy/.env logs -f app
```

- `/health/live` meldet, ob der Prozess antwortet. Darauf prüfen Docker (`HEALTHCHECK` per `dotnet MietHahn.Web.dll healthcheck`) und Caddy.
- `/health` prüft Datenbank, Gotenberg und ob die Dateiablage beschreibbar ist. Antwort 503 bei Problemen, ohne Fehlerdetails.

## Update

```bash
docker compose -f deploy/docker-compose.yml --env-file deploy/.env pull
docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d
```

Datenbank-Migrationen laufen beim Start automatisch. Vorher ein Backup ziehen (siehe unten). Für reproduzierbare Updates `MIETHAHN_IMAGE` auf eine Version setzen und gezielt hochzählen.

### PostgreSQL-Major-Upgrade (z. B. 18 → 19)

Das Datenformat ist zwischen Major-Versionen nicht kompatibel:
1. Backup ziehen (`pg_dump`, siehe Backup).
2. `docker compose … down`, Volume `miethahn_postgres` umbenennen oder löschen.
3. Image in `docker-compose.yml` anheben, `up -d postgres`.
4. Dump einspielen (`pg_restore`), dann `up -d`.

## Sicherheit

- TLS und HSTS über Caddy; die App setzt CSP, `X-Frame-Options: DENY`, `nosniff`, Referrer- und Permissions-Policy.
- Die App übernimmt Schema und Client-IP aus `X-Forwarded-*` nur von Absendern aus privaten Netzen (Docker-Netz). Die HTTPS-Umleitung macht Caddy (`Betrieb__HttpsUmleitung=false` im Image).
- **Data-Protection-Schlüssel** (Cookies, Tokens) liegen in der Tabelle `data_protection_keys`. Anmeldungen überstehen so Neustarts und sind im DB-Backup enthalten. Die Schlüssel sind dort nicht zusätzlich verschlüsselt. Wer Zugriff auf die Datenbank hat, hat ohnehin Zugriff auf alle Daten; die Datenbank ist daher nur intern erreichbar. Die Warnung „No XML encryptor configured“ beim ersten Start ist erwartet.
- Uploads: Caddy begrenzt auf 30 MB, die App zusätzlich auf `Dateiablage__MaxGroesseMb` (25).
- Geheimnisse nur in `deploy/.env` (per `.gitignore` ausgeschlossen), Dateirechte `chmod 600 deploy/.env`.

## Backup

Siehe Abschnitt Backup & Restore (Issue „Backup, Restore & Datenexport“). Zu sichern sind die Volumes `postgres` (bzw. ein `pg_dump`) und `dateien`.

## Lokal ausprobieren

```bash
docker build -t miethahn:lokal .
MIETHAHN_DOMAIN=localhost MIETHAHN_IMAGE=miethahn:lokal POSTGRES_PASSWORT=test ACME_EMAIL=a@b.de \
  MAIL_ABSENDER=test@localhost SMTP_HOST=localhost \
  docker compose -f deploy/docker-compose.yml up -d --no-build
```

Für `localhost` stellt Caddy ein Zertifikat seiner internen CA aus (Browserwarnung bestätigen).
