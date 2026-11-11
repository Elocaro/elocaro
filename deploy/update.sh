#!/usr/bin/env bash
# Elocaro auf eine veröffentlichte Version aktualisieren (Images aus GHCR, nichts wird auf dem Server gebaut).
#   deploy/update.sh 0.2.0     # auf diese Version
#   deploy/update.sh latest    # auf die neueste Version
# Ablauf: sichern, bisherige Images als Rückfallebene merken, Version in deploy/.env eintragen, laden, neu starten,
# auf "healthy" warten. Bei Fehlern bleibt die Rückfallebene (siehe Ausgabe).
set -euo pipefail

say() { printf '%s\n' "$*"; }
die() { say "Fehler: $*" >&2; exit 1; }

[ $# -eq 1 ] || die "Aufruf: deploy/update.sh <Version|latest>, z. B. deploy/update.sh 0.2.0"
VERSION="${1#v}"
[[ "$VERSION" =~ ^([0-9]+\.[0-9]+\.[0-9]+|latest)$ ]] || die "Ungültige Version '$1' (erwartet X.Y.Z oder latest)"

cd "$(dirname "$0")"
[ -f .env ] || die "deploy/.env fehlt"
COMPOSE=(docker compose -f docker-compose.yml --env-file .env)

# Domain für die Prüfung von außen
DOMAIN="$(sed -n "s/^ELOCARO_DOMAIN=['\"]\{0,1\}\([^'\"]*\)['\"]\{0,1\}$/\1/p" .env | head -n1)"
[ -n "$DOMAIN" ] || die "ELOCARO_DOMAIN fehlt in deploy/.env"

set_env() { # set_env KEY VALUE: ersetzt eine vorhandene Zeile (auch auskommentiert), sonst anhängen
  local key="$1" value="$2"
  if grep -q "^${key}=" .env; then
    sed -i "s|^${key}=.*|${key}=${value}|" .env
  else
    printf '%s=%s\n' "$key" "$value" >> .env
  fi
}

say "1/5 Vorher sichern ..."
"${COMPOSE[@]}" run --rm -T --no-deps backup backup || die "Sicherung fehlgeschlagen, nichts geändert."

say "2/5 Laufende Images als Rückfallebene merken ..."
for svc in app backup; do
  id="$("${COMPOSE[@]}" images -q "$svc" 2>/dev/null | head -n1 || true)"
  if [ -n "$id" ]; then docker tag "$id" "elocaro-vorher:$svc"; fi
done

say "3/5 Version $VERSION in deploy/.env eintragen ..."
cp .env ".env.bak-vor-update"
set_env ELOCARO_IMAGE "ghcr.io/elocaro/elocaro:${VERSION}"
set_env ELOCARO_BACKUP_IMAGE "ghcr.io/elocaro/elocaro-backup:${VERSION}"
# ELOCARO_VERSION gilt nur für selbst gebaute Images
sed -i "/^ELOCARO_VERSION=/d" .env

say "4/5 Images laden und starten ..."
if ! "${COMPOSE[@]}" pull app backup; then
  cp .env.bak-vor-update .env
  die "Laden fehlgeschlagen (Version vorhanden? Beim privaten Paket: docker login ghcr.io). deploy/.env zurückgesetzt, nichts neu gestartet."
fi
"${COMPOSE[@]}" up -d --no-deps app backup

say "5/5 Warten, bis die App antwortet (höchstens 3 Minuten) ..."
for _ in $(seq 1 36); do
  state="$(docker inspect -f '{{.State.Health.Status}}' "$("${COMPOSE[@]}" ps -q app)" 2>/dev/null || true)"
  if [ "$state" = "healthy" ] && curl -fsS -o /dev/null --max-time 5 "https://${DOMAIN}/health/live"; then
    say "Elocaro $VERSION läuft: https://${DOMAIN}"
    exit 0
  fi
  sleep 5
done

say "Die App wurde nicht rechtzeitig gesund. Logs: docker compose -f deploy/docker-compose.yml --env-file deploy/.env logs app" >&2
say "Zurück auf den vorherigen Stand: in deploy/.env ELOCARO_IMAGE=elocaro-vorher:app und ELOCARO_BACKUP_IMAGE=elocaro-vorher:backup setzen," >&2
say "dann 'docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d --no-deps app backup'. Die Migrationen laufen nicht zurück:" >&2
say "bei Schemaänderungen stattdessen die Sicherung von vorhin wiederherstellen (docs/betrieb.md)." >&2
exit 1
