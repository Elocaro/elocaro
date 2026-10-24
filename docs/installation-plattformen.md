# Installation auf verschiedenen Plattformen

MietHahn besteht aus einem Docker-Compose-Stack ([deploy/docker-compose.yml](../deploy/docker-compose.yml)): App, PostgreSQL, Gotenberg (PDF), Backup und Caddy (TLS). Jede Plattform, die Docker Compose ausführen kann, kann ihn betreiben. Die Wege unten nutzen dieselbe Datei und dieselbe `deploy/.env`; es gibt **keine plattformspezifischen Vorlagen**, die nicht auf allen Systemen geprüft wären.

> **Stand der Prüfung:** Geprüft ist der Weg auf einem Linux-Server mit Docker Engine und Compose-Plugin ([Betrieb](betrieb.md), `deploy/install.sh`). Die Hinweise zu den Plattformen darunter beruhen auf deren Dokumentation und sind **nicht** auf jeder Plattform ausprobiert. Wer sie nutzt, hilft mit einer Rückmeldung als Issue.

Gemeinsam für alle Wege:

1. `deploy/.env` aus [deploy/.env.example](../deploy/.env.example) erzeugen (am einfachsten `./deploy/install.sh --nur-env`).
2. Eine **Domain** mit A/AAAA-Eintrag auf den Server und offene Ports 80 und 443, oder den Probelauf ohne Domain (`./deploy/install.sh --lokal`).
3. **Datensicherung** einrichten: Das Passwort (`RESTIC_PASSWORD`) und ein Ziel außerhalb des Servers ([Betrieb → Backup](betrieb.md#backup)). Ohne Passwort gibt es keine Wiederherstellung.
4. Nach dem Start die Ersteinrichtung unter `https://<domain>` durchführen.

## Eigener Server oder VPS (empfohlen)

Debian 12 oder Ubuntu 24.04 mit Docker Engine: `git clone …`, `./deploy/install.sh`. Mindestens 2 GB RAM (Gotenberg/Chromium). Details: [Betrieb](betrieb.md#installation).

## Portainer (Stack)

Stack aus dem Repository anlegen: *Stacks → Add stack → Repository* mit URL `https://github.com/TheR00st3r/MietHahn`, Compose-Pfad `deploy/docker-compose.yml`. Die Werte aus `deploy/.env` als Umgebungsvariablen des Stacks eintragen (Portainer: *Environment variables → Advanced mode*, Inhalt der `.env` einfügen). Caddy belegt 80/443; läuft schon ein anderer Proxy auf dem Host, `ports` des Dienstes `caddy` anpassen und den eigenen Proxy auf `app:8080` zeigen lassen.

## Coolify

Ressource *Docker Compose* aus dem Git-Repository mit Compose-Datei `deploy/docker-compose.yml`. Coolify bringt einen eigenen Proxy mit: den Dienst `caddy` entfernen oder abschalten, die Domain in Coolify dem Dienst `app` (Port 8080) zuweisen und `Application__BaseUrl` auf `https://<domain>/` setzen (Umgebungsvariable `MIETHAHN_DOMAIN` in der `.env`).

## Synology (Container Manager)

*Projekt* anlegen, als Quelle den Ordner mit `deploy/docker-compose.yml` und `deploy/.env` wählen. Die Ports 80/443 sind auf DSM oft belegt: Caddy auf andere Ports legen und den Reverse-Proxy von DSM (mit Zertifikat) auf `localhost:<Port>` zeigen lassen, oder Caddy entfernen und den DSM-Proxy direkt auf den veröffentlichten Port der App zeigen lassen.

## Unraid

Das Plugin *Docker Compose Manager* installieren, einen Stack mit dem Inhalt von `deploy/docker-compose.yml` anlegen und `deploy/.env` im Stack-Ordner ablegen. Die Volumes (`postgres`, `files`) gehören auf den Array- oder Cache-Pool und in die Unraid-Sicherung.

## Proxmox

MietHahn läuft in einer **VM** (oder einem LXC-Container mit aktiviertem Nesting) mit Docker; darin wie auf einem eigenen Server installieren. Die Datensicherung der Proxmox-Umgebung ersetzt nicht das Backup von MietHahn: Datenbank und Dateiablage sichert der Backup-Dienst konsistent.

## Raspberry Pi und andere ARM-Geräte

Die Images erscheinen für `amd64` und `arm64` (siehe [Mitarbeiten → Releases](../CONTRIBUTING.md#releases)). Ein Pi 4/5 mit 4 GB RAM reicht für einen kleinen Bestand; Gotenberg ist der größte Speicherverbraucher.
