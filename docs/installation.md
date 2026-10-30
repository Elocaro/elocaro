# Installation

Elocaro läuft als Docker-Compose-Stack: App, PostgreSQL, Gotenberg (PDF), Backup und Caddy (TLS). Alles, was Docker Compose ausführen kann, kann ihn betreiben. Diese Seite zeigt die Wege und hilft bei der Wahl; die Einzelheiten stehen in [Betrieb](betrieb.md).

## Welcher Weg passt?

| Ich möchte … | Weg | Dauer |
|---|---|---|
| Elocaro erst einmal ansehen | [Demo](https://demo.elocaro.de) im Browser, keine Installation | 1 Minute |
| es auf dem eigenen Rechner ausprobieren | [Probelauf lokal](#1-probelauf-auf-dem-eigenen-rechner) | 5 Minuten |
| es dauerhaft betreiben (empfohlen) | [Eigener Server mit Skript](#2-eigener-server-mit-dem-installationsskript) | 10 Minuten |
| volle Kontrolle über jede Einstellung | [Von Hand mit Docker Compose](#3-von-hand-mit-docker-compose) | 15 Minuten |
| einen Server in der Microsoft-Cloud mieten | [Azure-VM](azure.md) | 1 Stunde |
| es auf NAS, Home-Server oder mit vorhandenem Proxy betreiben | [Plattformen](#4-nas-home-server-und-vorhandener-proxy) | je nach Umgebung |

## Das brauchen Sie

- **Server:** Linux (Debian 12 oder Ubuntu 24.04 empfohlen), mindestens 2 GB RAM (die PDF-Erzeugung braucht etwa 500 MB), mindestens 20 GB Platte. Ein Raspberry Pi 4/5 mit 4 GB reicht für einen kleinen Bestand (die Images gibt es für `amd64` und `arm64`).
- **Docker Engine mit Compose-Plugin** ([Installationsanleitung von Docker](https://docs.docker.com/engine/install/)).
- **Domain** mit A/AAAA-Eintrag auf den Server und offene Ports 80 und 443. Für den Probelauf nicht nötig.
- **SMTP-Zugang** zum Mailversand (z. B. beim Anbieter der Domain). Ohne ihn laufen Anmeldelinks, Mahnungen und Benachrichtigungen nicht.

## 1. Probelauf auf dem eigenen Rechner

Ohne Domain und ohne Mailserver, auf `https://localhost` mit selbst signiertem Zertifikat:

```bash
git clone https://github.com/Elocaro/elocaro.git && cd elocaro
./deploy/install.sh --lokal
```

Der Browser warnt einmal wegen des Zertifikats; die Warnung ist hier erwartet. Der erste Aufruf führt durch die Ersteinrichtung. Zum Aufräumen: `docker compose -f deploy/docker-compose.yml --env-file deploy/.env down -v` (löscht auch die Daten).

## 2. Eigener Server mit dem Installationsskript

Der empfohlene Weg für den dauerhaften Betrieb.

1. **DNS:** Den A-Eintrag (und bei IPv6 den AAAA-Eintrag) der Domain auf den Server zeigen lassen und warten, bis er sich verbreitet hat. Let's Encrypt stellt das Zertifikat erst danach aus.
2. **Installieren:**
   ```bash
   git clone https://github.com/Elocaro/elocaro.git && cd elocaro
   ./deploy/install.sh
   ```
   Das Skript fragt Domain, Kontakt-Mail und SMTP-Zugang ab, erzeugt die Passwörter, schreibt `deploy/.env` (Rechte 600), startet den Stack und wartet, bis die Adresse antwortet.
3. **Ersteinrichtung:** `https://<domain>` öffnen, Administrator anlegen, Zwei-Faktor-Anmeldung einrichten und dem Einrichtungsassistenten folgen.

Für Automatisierung lassen sich alle Fragen vorab über Umgebungsvariablen beantworten (`ELOCARO_DOMAIN`, `ACME_EMAIL`, `MAIL_SENDER`, `SMTP_HOST`, `SMTP_PORT`, `SMTP_SECURITY`, `SMTP_USER`, `SMTP_PASSWORD`); mit `ELOCARO_NONINTERACTIVE=1` wird nichts gefragt. `--nur-env` schreibt nur die Datei, `--force` ersetzt eine vorhandene (die alte bleibt als `.env.bak`).

## 3. Von Hand mit Docker Compose

```bash
git clone https://github.com/Elocaro/elocaro.git && cd elocaro
cp deploy/.env.example deploy/.env
nano deploy/.env        # Domain, Passwörter, SMTP eintragen (Datenbankpasswort: openssl rand -base64 32)
docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d
docker compose -f deploy/docker-compose.yml --env-file deploy/.env ps
```

Die Werte und weitere Schalter beschreibt [Betrieb → Konfiguration](betrieb.md#konfiguration). Das Image kommt von `ghcr.io/elocaro/elocaro`; ist es nicht abrufbar, baut Compose es beim Start aus dem Quelltext (dauert einige Minuten). Für reproduzierbare Updates `ELOCARO_IMAGE` auf eine feste Version setzen.

## 4. NAS, Home-Server und vorhandener Proxy

Alle Wege nutzen dieselbe [docker-compose.yml](../deploy/docker-compose.yml) und dieselbe `deploy/.env`. Läuft auf dem Gerät schon ein Proxy, der Port 80/443 belegt, übernimmt er TLS und Caddy entfällt.

| Plattform | Vorgehen |
|---|---|
| **Traefik** | Override-Datei `deploy/docker-compose.traefik.yml` dazunehmen ([Betrieb → Traefik](betrieb.md#traefik-statt-caddy)). |
| **Portainer** | *Stacks → Add stack → Repository*, Compose-Pfad `deploy/docker-compose.yml`, Werte der `.env` als Umgebungsvariablen eintragen. |
| **Coolify** | Ressource *Docker Compose* aus dem Repository; den Dienst `caddy` entfernen, die Domain dem Dienst `app` (Port 8080) zuweisen. |
| **Synology** | Container Manager → *Projekt*; Caddy auf andere Ports legen und den DSM-Reverse-Proxy auf diesen Port zeigen lassen. |
| **Unraid** | Plugin *Docker Compose Manager*; die Volumes `postgres` und `files` gehören auf Array oder Cache-Pool und in die Sicherung. |
| **Proxmox** | In einer VM (oder einem LXC mit Nesting) mit Docker wie auf einem eigenen Server installieren. |
| **Raspberry Pi** | Wie ein eigener Server; Pi 4/5 mit 4 GB RAM, 64-Bit-Betriebssystem. |

Einzelheiten zu jeder Plattform: [Installation auf anderen Plattformen](installation-plattformen.md). Geprüft ist der Weg auf einem Linux-Server mit Docker; die Hinweise für die übrigen Plattformen beruhen auf deren Dokumentation und sind nicht auf jeder Plattform ausprobiert.

## Nach der Installation

1. **Ersteinrichtung** abschließen (Administrator, Zwei-Faktor, Einrichtungsassistent).
2. Unter *Einstellungen → E-Mail-Versand* eine **Testnachricht** senden.
3. Unter *Einstellungen → Vermieter* die **Angaben des Vermieters** pflegen; sie erscheinen in Briefkopf und Fußzeile.
4. **Datensicherung einrichten** und eine erste Sicherung anstoßen ([Betrieb → Backup](betrieb.md#backup)). Das Passwort `RESTIC_PASSWORD` gut aufbewahren: Ohne es gibt es keine Wiederherstellung. Ein Ziel außerhalb des Servers ist Pflicht.

## Prüfen, aktualisieren, Probleme

```bash
curl https://<domain>/health                                                    # Gesundheitsprüfung
docker compose -f deploy/docker-compose.yml --env-file deploy/.env ps           # alle Dienste "healthy"/"Up"?
docker compose -f deploy/docker-compose.yml --env-file deploy/.env logs -f app  # Protokoll der App
```

**Aktualisieren:** erst sichern (`docker compose … run --rm backup backup`), dann `pull` und `up -d`; die Datenbank wird beim Start automatisch angepasst ([Betrieb → Update](betrieb.md#update)). Die laufende Version steht unter „Über Elocaro“.

| Problem | Ursache und Abhilfe |
|---|---|
| Browser meldet kein Zertifikat / Zertifikatsfehler | DNS zeigt noch nicht auf den Server, oder Port 80/443 ist gesperrt. Mit `docker compose … logs caddy` prüfen. |
| Seite lädt, reagiert aber nicht | Kurz warten: Nach dem Start meldet Caddy bis zu 30 Sekunden `503`. Bleibt es, `/health` und die App-Logs ansehen. |
| Keine Mails | SMTP-Werte in `deploy/.env` prüfen (`SMTP_SECURITY`: `StartTls` für Port 587, `SslOnConnect` für 465); bei manchen Anbietern ist ausgehend Port 25 gesperrt. |
| Port 80/443 belegt | Es läuft schon ein anderer Proxy: [Traefik](betrieb.md#traefik-statt-caddy) oder die Plattform-Hinweise oben nutzen. |

Hilfe gibt es über die [Issues](https://github.com/Elocaro/elocaro/issues).
