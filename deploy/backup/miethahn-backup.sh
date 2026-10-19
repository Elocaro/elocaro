#!/bin/sh
# MietHahn backup: back up the database (pg_dump) and file storage to a restic repository, or restore from it.
#
#   miethahn-backup zeitplan                 back up daily at BACKUP_TIME (default in the container)
#   miethahn-backup sichern                  back up immediately
#   miethahn-backup snapshots                list existing backups
#   miethahn-backup wiederherstellen [ID]    restore a backup (default: latest); the app must be stopped
#   miethahn-backup pruefen                  check the repository including a data sample
#   miethahn-backup restic …                 any restic command
#
# After each backup run the result is written to BACKUP_STATUS_DATEI; MietHahn reads it and warns the admin.
set -eu

DATEN="${BACKUP_DATA_PATH:-/data}"
ARBEIT="${BACKUP_ARBEITSVERZEICHNIS:-/arbeit}"
STATUS_DATEI="${BACKUP_STATUS_DATEI:-/status/backup.json}"
ZEIT="${BACKUP_TIME:-02:30}"
DB="${PGDATABASE:-miethahn}"
APP_UID="${APP_UID:-1654}"
# Fixed hostname, otherwise every new container creates its own snapshot series.
RESTIC_HOST="${BACKUP_HOSTNAME:-miethahn}"
TAG=miethahn

log() { printf '%s %s\n' "$(date '+%Y-%m-%d %H:%M:%S')" "$*"; }

fehler() { log "FEHLER: $*" >&2; exit 1; }

status_schreiben() { # success message snapshot duration
    mkdir -p "$(dirname "$STATUS_DATEI")"
    meldung=$(printf '%s' "$2" | tr '\n\r\t"' '    ' | tr -d '\134' | cut -c1-500)
    printf '{"timestamp":"%s","succeeded":%s,"message":"%s","snapshot":"%s","durationSeconds":%s}\n' \
        "$(date -u '+%Y-%m-%dT%H:%M:%SZ')" "$1" "$meldung" "$3" "$4" > "$STATUS_DATEI.tmp"
    mv "$STATUS_DATEI.tmp" "$STATUS_DATEI"
}

repository_bereit() {
    [ -n "${RESTIC_REPOSITORY:-}" ] || fehler "RESTIC_REPOSITORY ist nicht gesetzt."
    [ -n "${RESTIC_PASSWORD:-}${RESTIC_PASSWORD_FILE:-}" ] || fehler "RESTIC_PASSWORD ist nicht gesetzt."
    if ! restic cat config > /dev/null 2>&1; then
        log "Repository $RESTIC_REPOSITORY wird angelegt"
        restic init
    fi
}

sichern_intern() {
    repository_bereit
    rm -rf "$ARBEIT"
    mkdir -p "$ARBEIT"

    log "Datenbank $DB sichern"
    # Uncompressed: restic deduplicates and compresses itself (unchanged tables hardly cost any space).
    pg_dump --format=custom --compress=0 --file="$ARBEIT/$DB.dump" "$DB"

    log "Sicherung nach $RESTIC_REPOSITORY"
    restic backup --host "$RESTIC_HOST" --tag "$TAG" --json --quiet "$ARBEIT" "$DATEN" > "$ARBEIT/ergebnis.json"
    sed -n 's/.*"snapshot_id":"\([0-9a-f]*\)".*/\1/p' "$ARBEIT/ergebnis.json" | tail -n 1 > /tmp/snapshot-id

    log "Alte Sicherungen aufräumen"
    restic forget --host "$RESTIC_HOST" --tag "$TAG" --prune \
        --keep-daily "${BACKUP_KEEP_DAILY:-7}" \
        --keep-weekly "${BACKUP_KEEP_WEEKLY:-4}" \
        --keep-monthly "${BACKUP_KEEP_MONTHLY:-12}"

    log "Repository prüfen"
    restic check

    rm -rf "$ARBEIT"
    log "Sicherung $(cat /tmp/snapshot-id) abgeschlossen"
}

sichern() {
    start=$(date +%s)
    protokoll=/tmp/backup.log
    : > /tmp/snapshot-id
    # Separate process so that `set -e` takes effect (it would be ineffective inside `if` conditions).
    set +e
    "$0" _sichern > "$protokoll" 2>&1
    rc=$?
    set -e
    cat "$protokoll"
    dauer=$(( $(date +%s) - start ))
    if [ "$rc" -eq 0 ]; then
        status_schreiben true "Sicherung erfolgreich" "$(cat /tmp/snapshot-id)" "$dauer"
    else
        status_schreiben false "$(grep -v '^$' "$protokoll" | tail -n 3)" "" "$dauer"
    fi
    return "$rc"
}

sekunden_bis() { # HH:MM
    jetzt=$(date +%s)
    ziel=$(date -d "$(date '+%Y-%m-%d') $1" +%s) || fehler "BACKUP_TIME '$1' ist keine Uhrzeit (HH:MM)."
    [ "$ziel" -gt "$jetzt" ] || ziel=$((ziel + 86400))
    echo $((ziel - jetzt))
}

zeitplan() {
    sekunden_bis "$ZEIT" > /dev/null
    log "Tägliche Sicherung um $ZEIT ($TZ), Ziel $RESTIC_REPOSITORY"
    while true; do
        warten=$(sekunden_bis "$ZEIT")
        log "Nächste Sicherung in $((warten / 60)) Minuten"
        sleep "$warten"
        sichern || log "Sicherung fehlgeschlagen, nächster Versuch zur nächsten geplanten Zeit"
        sleep 60
    done
}

wiederherstellen() {
    snapshot="${1:-latest}"
    repository_bereit

    verbunden=$(psql -d postgres -tAc "SELECT count(*) FROM pg_stat_activity WHERE datname = '$DB' AND pid <> pg_backend_pid()")
    if [ "$verbunden" -gt 0 ] && [ "${ERZWINGEN:-0}" != 1 ]; then
        fehler "$verbunden Verbindung(en) zur Datenbank $DB offen. Erst die App stoppen: docker compose … stop app"
    fi

    ziel=/wiederherstellung
    rm -rf "$ziel"
    log "Sicherung $snapshot aus $RESTIC_REPOSITORY holen"
    restic restore "$snapshot" --host "$RESTIC_HOST" --tag "$TAG" --target "$ziel"
    dump="$ziel$ARBEIT/$DB.dump"
    [ -f "$dump" ] || fehler "Die Sicherung enthält keinen Datenbank-Dump ($ARBEIT/$DB.dump)."

    log "Datenbank $DB neu anlegen und einspielen"
    psql -d postgres -v ON_ERROR_STOP=1 -q \
        -c "DROP DATABASE IF EXISTS \"$DB\" WITH (FORCE)" \
        -c "CREATE DATABASE \"$DB\""
    pg_restore --exit-on-error --no-owner --dbname="$DB" "$dump"

    log "Dateiablage $DATEN ersetzen"
    mkdir -p "$DATEN"
    find "$DATEN" -mindepth 1 -delete
    if [ -d "$ziel$DATEN" ]; then
        cp -a "$ziel$DATEN/." "$DATEN/"
    fi
    chown -R "$APP_UID:$APP_UID" "$DATEN"

    rm -rf "$ziel"
    log "Wiederherstellung abgeschlossen. Jetzt die App starten: docker compose … start app"
}

befehl="${1:-zeitplan}"
[ $# -gt 0 ] && shift
case "$befehl" in
    zeitplan) zeitplan ;;
    sichern) sichern ;;
    _sichern) sichern_intern ;;
    snapshots) repository_bereit; restic snapshots --host "$RESTIC_HOST" --tag "$TAG" ;;
    wiederherstellen) wiederherstellen "$@" ;;
    pruefen) repository_bereit; restic check --read-data-subset="${BACKUP_PRUEFEN_ANTEIL:-10%}" ;;
    restic) exec restic "$@" ;;
    *) fehler "Unbekannter Befehl '$befehl' (zeitplan, sichern, snapshots, wiederherstellen, pruefen, restic)" ;;
esac
