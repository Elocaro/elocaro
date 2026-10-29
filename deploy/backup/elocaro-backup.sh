#!/bin/sh
# Elocaro backup: back up the database (pg_dump) and file storage to a restic repository, or restore from it.
#
#   elocaro-backup schedule                 back up daily at BACKUP_TIME (default in the container)
#   elocaro-backup backup                    back up immediately
#   elocaro-backup snapshots                list existing backups
#   elocaro-backup restore [ID]      restore a backup (default: latest); the app must be stopped
#   elocaro-backup verify                  check the repository including a data sample
#   elocaro-backup restic …                 any restic command
#
# After each backup run the result is written to BACKUP_STATUS_FILE; Elocaro reads it and warns the admin.
set -eu

DATA_DIR="${BACKUP_DATA_PATH:-/data}"
WORKDIR="${BACKUP_WORKDIR:-/work}"
STATUS_FILE="${BACKUP_STATUS_FILE:-/status/backup.json}"
BACKUP_AT="${BACKUP_TIME:-02:30}"
DB="${PGDATABASE:-elocaro}"
APP_UID="${APP_UID:-1654}"
# Fixed hostname, otherwise every new container creates its own snapshot series.
RESTIC_HOST="${BACKUP_HOSTNAME:-elocaro}"
TAG=elocaro

log() { printf '%s %s\n' "$(date '+%Y-%m-%d %H:%M:%S')" "$*"; }

fail() { log "FEHLER: $*" >&2; exit 1; }

write_status() { # success message snapshot duration
    mkdir -p "$(dirname "$STATUS_FILE")"
    message=$(printf '%s' "$2" | tr '\n\r\t"' '    ' | tr -d '\134' | cut -c1-500)
    printf '{"timestamp":"%s","succeeded":%s,"message":"%s","snapshot":"%s","durationSeconds":%s}\n' \
        "$(date -u '+%Y-%m-%dT%H:%M:%SZ')" "$1" "$message" "$3" "$4" > "$STATUS_FILE.tmp"
    mv "$STATUS_FILE.tmp" "$STATUS_FILE"
}

repository_ready() {
    [ -n "${RESTIC_REPOSITORY:-}" ] || fail "RESTIC_REPOSITORY ist nicht gesetzt."
    [ -n "${RESTIC_PASSWORD:-}${RESTIC_PASSWORD_FILE:-}" ] || fail "RESTIC_PASSWORD ist nicht gesetzt."
    if ! restic cat config > /dev/null 2>&1; then
        log "Repository $RESTIC_REPOSITORY wird angelegt"
        restic init
    fi
}

backup_internal() {
    repository_ready
    rm -rf "$WORKDIR"
    mkdir -p "$WORKDIR"

    log "Datenbank $DB sichern"
    # Uncompressed: restic deduplicates and compresses itself (unchanged tables hardly cost any space).
    pg_dump --format=custom --compress=0 --file="$WORKDIR/$DB.dump" "$DB"

    log "Sicherung nach $RESTIC_REPOSITORY"
    restic backup --host "$RESTIC_HOST" --tag "$TAG" --json --quiet "$WORKDIR" "$DATA_DIR" > "$WORKDIR/result.json"
    sed -n 's/.*"snapshot_id":"\([0-9a-f]*\)".*/\1/p' "$WORKDIR/result.json" | tail -n 1 > /tmp/snapshot-id

    log "Alte Sicherungen aufräumen"
    restic forget --host "$RESTIC_HOST" --tag "$TAG" --prune \
        --keep-daily "${BACKUP_KEEP_DAILY:-7}" \
        --keep-weekly "${BACKUP_KEEP_WEEKLY:-4}" \
        --keep-monthly "${BACKUP_KEEP_MONTHLY:-12}"

    log "Repository prüfen"
    restic check

    rm -rf "$WORKDIR"
    log "Sicherung $(cat /tmp/snapshot-id) abgeschlossen"
}

run_backup() {
    start=$(date +%s)
    logfile=/tmp/backup.log
    : > /tmp/snapshot-id
    # Separate process so that `set -e` takes effect (it would be ineffective inside `if` conditions).
    set +e
    "$0" _backup > "$logfile" 2>&1
    rc=$?
    set -e
    cat "$logfile"
    duration=$(( $(date +%s) - start ))
    if [ "$rc" -eq 0 ]; then
        write_status true "Sicherung erfolgreich" "$(cat /tmp/snapshot-id)" "$duration"
    else
        write_status false "$(grep -v '^$' "$logfile" | tail -n 3)" "" "$duration"
    fi
    return "$rc"
}

seconds_until() { # HH:MM
    now_ts=$(date +%s)
    target=$(date -d "$(date '+%Y-%m-%d') $1" +%s) || fail "BACKUP_TIME '$1' ist keine Uhrzeit (HH:MM)."
    [ "$target" -gt "$now_ts" ] || target=$((target + 86400))
    echo $((target - now_ts))
}

schedule() {
    seconds_until "$BACKUP_AT" > /dev/null
    log "Tägliche Sicherung um $BACKUP_AT ($TZ), Ziel $RESTIC_REPOSITORY"
    while true; do
        wait_seconds=$(seconds_until "$BACKUP_AT")
        log "Nächste Sicherung in $((wait_seconds / 60)) Minuten"
        sleep "$wait_seconds"
        run_backup || log "Sicherung fehlgeschlagen, nächster Versuch zur nächsten geplanten Zeit"
        sleep 60
    done
}

restore() {
    snapshot="${1:-latest}"
    repository_ready

    connected=$(psql -d postgres -tAc "SELECT count(*) FROM pg_stat_activity WHERE datname = '$DB' AND pid <> pg_backend_pid()")
    if [ "$connected" -gt 0 ] && [ "${FORCE:-0}" != 1 ]; then
        fail "$connected Verbindung(en) zur Datenbank $DB offen. Erst die App stoppen: docker compose … stop app"
    fi

    target=/restore-target
    rm -rf "$target"
    log "Sicherung $snapshot aus $RESTIC_REPOSITORY holen"
    restic restore "$snapshot" --host "$RESTIC_HOST" --tag "$TAG" --target "$target"
    dump="$target$WORKDIR/$DB.dump"
    [ -f "$dump" ] || fail "Die Sicherung enthält keinen Datenbank-Dump ($WORKDIR/$DB.dump)."

    log "Datenbank $DB neu anlegen und einspielen"
    psql -d postgres -v ON_ERROR_STOP=1 -q \
        -c "DROP DATABASE IF EXISTS \"$DB\" WITH (FORCE)" \
        -c "CREATE DATABASE \"$DB\""
    pg_restore --exit-on-error --no-owner --dbname="$DB" "$dump"

    log "Dateiablage $DATA_DIR ersetzen"
    mkdir -p "$DATA_DIR"
    find "$DATA_DIR" -mindepth 1 -delete
    if [ -d "$target$DATA_DIR" ]; then
        cp -a "$target$DATA_DIR/." "$DATA_DIR/"
    fi
    chown -R "$APP_UID:$APP_UID" "$DATA_DIR"

    rm -rf "$target"
    log "Wiederherstellung abgeschlossen. Jetzt die App starten: docker compose … start app"
}

command="${1:-schedule}"
[ $# -gt 0 ] && shift
case "$command" in
    schedule) schedule ;;
    backup) run_backup ;;
    _backup) backup_internal ;;
    snapshots) repository_ready; restic snapshots --host "$RESTIC_HOST" --tag "$TAG" ;;
    restore) restore "$@" ;;
    verify) repository_ready; restic check --read-data-subset="${BACKUP_VERIFY_SUBSET:-10%}" ;;
    restic) exec restic "$@" ;;
    *) fail "Unbekannter Befehl '$command' (schedule, backup, snapshots, restore, verify, restic)" ;;
esac
