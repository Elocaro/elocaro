#!/usr/bin/env bash
# MietHahn installation: asks for the basics, writes deploy/.env and starts the stack.
#
#   ./deploy/install.sh              interactive, with a real domain (Let's Encrypt)
#   ./deploy/install.sh --lokal      try it out on this machine (https://localhost, self-signed certificate)
#   ./deploy/install.sh --nur-env    write deploy/.env only, do not start anything
#   ./deploy/install.sh --force      overwrite an existing deploy/.env (the old file is kept as .env.bak)
#
# Every question can be answered up front through the environment (MIETHAHN_DOMAIN, ACME_EMAIL, MAIL_SENDER, SMTP_HOST,
# SMTP_PORT, SMTP_SECURITY, SMTP_USER, SMTP_PASSWORD); with MIETHAHN_NONINTERACTIVE=1 nothing is asked.
set -euo pipefail

cd "$(dirname "$0")"

LOCAL=0
ENV_ONLY=0
FORCE=0
for arg in "$@"; do
  case "$arg" in
    --lokal) LOCAL=1 ;;
    --nur-env) ENV_ONLY=1 ;;
    --force) FORCE=1 ;;
    -h|--help) sed -n '2,11p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unbekannte Option: $arg (siehe --help)" >&2; exit 2 ;;
  esac
done

say() { printf '%s\n' "$*"; }
fail() { printf 'Fehler: %s\n' "$*" >&2; exit 1; }

# ask VAR "Frage" "Vorgabe": keeps a value that is already set, otherwise asks (or takes the default when non-interactive)
ask() {
  local var=$1 question=$2 default=${3-} current=${!1-}
  if [ -n "$current" ]; then return; fi
  if [ "${MIETHAHN_NONINTERACTIVE:-0}" = 1 ] || [ ! -t 0 ]; then
    printf -v "$var" '%s' "$default"
    return
  fi
  local answer
  if [ -n "$default" ]; then read -r -p "$question [$default]: " answer; else read -r -p "$question: " answer; fi
  printf -v "$var" '%s' "${answer:-$default}"
}

# (head closes the pipe early, tr then ends with SIGPIPE: that must not abort the script under pipefail)
secret() { local value; value=$(LC_ALL=C tr -dc 'A-Za-z0-9' </dev/urandom | head -c 40 || true); printf '%s' "$value"; }

# Values go into the env file in single quotes (docker compose then takes them literally, "$" included), so only
# line breaks and the single quote itself are not allowed.
check_value() {
  case "$2" in
    *[$'\n\r\'']*) fail "$1 enthält einen Zeilenumbruch oder ein einfaches Anführungszeichen (') und kann so nicht gespeichert werden." ;;
  esac
}

if [ "$ENV_ONLY" = 0 ]; then
  command -v docker >/dev/null 2>&1 || fail "Docker ist nicht installiert (https://docs.docker.com/engine/install/)."
  docker compose version >/dev/null 2>&1 || fail "Das Docker-Compose-Plugin fehlt (docker compose version schlägt fehl)."
  docker info >/dev/null 2>&1 || fail "Der Docker-Dienst ist nicht erreichbar (läuft er, und darf dieser Benutzer ihn nutzen?)."
fi

if [ -f .env ] && [ "$FORCE" = 0 ]; then
  fail "deploy/.env existiert bereits. Mit --force überschreiben (Sicherung als .env.bak) oder die Datei von Hand anpassen."
fi

say "MietHahn einrichten"
say ""

if [ "$LOCAL" = 1 ]; then
  MIETHAHN_DOMAIN=localhost
  ACME_EMAIL=${ACME_EMAIL:-admin@localhost.invalid}
  MAIL_SENDER=${MAIL_SENDER:-verwaltung@localhost.invalid}
  SMTP_HOST=${SMTP_HOST:-localhost}
  say "Lokaler Probelauf: Adresse https://localhost (Zertifikat selbst signiert, der Browser warnt einmalig)."
  say "E-Mails werden nicht zugestellt, solange kein SMTP-Server eingetragen ist (deploy/.env)."
else
  say "Die Domain muss per A/AAAA-Eintrag auf diesen Server zeigen, Port 80 und 443 müssen offen sein."
  ask MIETHAHN_DOMAIN "Domain (z. B. vermietung.example.de)"
  [ -n "$MIETHAHN_DOMAIN" ] || fail "Ohne Domain geht es nicht. Zum Ausprobieren: ./deploy/install.sh --lokal"
  ask ACME_EMAIL "Kontakt-E-Mail für Let's Encrypt" "admin@$MIETHAHN_DOMAIN"
  ask MAIL_SENDER "Absenderadresse für E-Mails an Mieter" "verwaltung@$MIETHAHN_DOMAIN"
  ask SMTP_HOST "SMTP-Server des Mailanbieters (z. B. smtp.example.de)"
  [ -n "$SMTP_HOST" ] || fail "Ohne SMTP-Server können keine Einladungen und Abrechnungen verschickt werden."
fi

ask MAIL_SENDER_NAME "Absendername" "Hausverwaltung"
ask SMTP_PORT "SMTP-Port" "587"
ask SMTP_SECURITY "Verschlüsselung (StartTls für Port 587, SslOnConnect für 465)" "StartTls"
ask SMTP_USER "SMTP-Benutzer (leer lassen, wenn keiner nötig ist)" "${MAIL_SENDER:-}"
if [ -z "${SMTP_PASSWORD:-}" ] && [ "${MIETHAHN_NONINTERACTIVE:-0}" != 1 ] && [ -t 0 ]; then
  read -r -s -p "SMTP-Passwort (leer lassen, wenn keins nötig ist): " SMTP_PASSWORD
  echo
fi
SMTP_PASSWORD=${SMTP_PASSWORD:-}

for pair in "Domain:$MIETHAHN_DOMAIN" "E-Mail:$ACME_EMAIL" "Absender:$MAIL_SENDER" "Absendername:${MAIL_SENDER_NAME:-}" "SMTP-Server:$SMTP_HOST" \
            "SMTP-Port:${SMTP_PORT:-}" "SMTP-Verschlüsselung:${SMTP_SECURITY:-}" "SMTP-Benutzer:${SMTP_USER:-}" "SMTP-Passwort:$SMTP_PASSWORD"; do
  check_value "${pair%%:*}" "${pair#*:}"
done
case "${SMTP_SECURITY}" in StartTls|SslOnConnect|None|Auto) ;; *) fail "SMTP-Verschlüsselung muss StartTls, SslOnConnect, None oder Auto sein." ;; esac
case "${SMTP_PORT}" in ''|*[!0-9]*) fail "SMTP-Port muss eine Zahl sein." ;; esac

[ -f .env ] && cp .env ".env.bak"
umask 077
POSTGRES_PASSWORD=$(secret)
RESTIC_PASSWORD=$(secret)

cat > .env <<ENVFILE
# Erzeugt von deploy/install.sh am $(date +%Y-%m-%d). Nicht committen.
MIETHAHN_DOMAIN='$MIETHAHN_DOMAIN'
ACME_EMAIL='$ACME_EMAIL'

POSTGRES_PASSWORD=$POSTGRES_PASSWORD

MAIL_SENDER='$MAIL_SENDER'
MAIL_SENDER_NAME='$MAIL_SENDER_NAME'
SMTP_HOST='$SMTP_HOST'
SMTP_PORT='$SMTP_PORT'
SMTP_SECURITY='$SMTP_SECURITY'
SMTP_USER='$SMTP_USER'
SMTP_PASSWORD='$SMTP_PASSWORD'

# Belegauslesung per KI ist aus: es verlässt kein Dokument den Server (siehe docs/betrieb.md).
RECEIPT_AI_PROVIDER=None

# Datensicherung mit restic. OHNE DAS PASSWORT GIBT ES KEINE WIEDERHERSTELLUNG: an einem zweiten Ort aufbewahren.
RESTIC_PASSWORD=$RESTIC_PASSWORD
# Ziel: lokal nur zum Ausprobieren (liegt auf demselben Server). Besser extern, siehe docs/betrieb.md.
RESTIC_REPOSITORY=/repo
BACKUP_TIME=02:30
ENVFILE
chmod 600 .env

say ""
say "deploy/.env geschrieben."
say "Wichtig: Das Sicherungspasswort (RESTIC_PASSWORD) steht in deploy/.env. Bewahren Sie es zusätzlich im Passwortmanager auf."
if [ "$LOCAL" = 0 ]; then
  say "Die Sicherung liegt vorerst auf diesem Server (./deploy/backup-repo). Richten Sie ein externes Ziel ein: docs/betrieb.md, Abschnitt Backup."
fi

if [ "$ENV_ONLY" = 1 ]; then
  say "Nur .env geschrieben (--nur-env). Start: docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d"
  exit 0
fi

say ""
say "Starte MietHahn (beim ersten Mal werden die Images geladen) ..."
docker compose -f docker-compose.yml --env-file .env up -d

url="https://$MIETHAHN_DOMAIN"
insecure=""
[ "$LOCAL" = 1 ] && insecure="-k"
say "Warte, bis $url antwortet (höchstens 3 Minuten) ..."
ready=0
for _ in $(seq 1 36); do
  # shellcheck disable=SC2086
  if curl -fsS $insecure -o /dev/null --max-time 5 "$url/health/live" 2>/dev/null; then ready=1; break; fi
  sleep 5
done

if [ "$ready" = 1 ]; then
  say ""
  say "MietHahn läuft: $url"
  say "Der erste Aufruf führt durch die Ersteinrichtung (Administrator-Konto, danach Zwei-Faktor-Anmeldung)."
else
  say ""
  say "MietHahn antwortet noch nicht unter $url."
  say "Bei einer echten Domain: DNS-Eintrag und offene Ports 80/443 prüfen (das Zertifikat braucht sie)."
  say "Protokolle: docker compose -f deploy/docker-compose.yml --env-file deploy/.env logs -f"
  exit 1
fi
