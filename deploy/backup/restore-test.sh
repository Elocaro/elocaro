#!/bin/sh
# End-to-end test for backup and restore (locally and in CI):
#   1. Start the app against an empty database (migrations), create test data and a file
#   2. Back up with restic to a local repository
#   3. Delete all volumes, restart PostgreSQL empty, restore the backup
#   4. Compare the data (schema, content of every table, files incl. owner); the app must start again
#
#   deploy/backup/restore-test.sh [APP_IMAGE] [BACKUP_IMAGE]
set -eu

cd "$(dirname "$0")/.."
export ELOCARO_IMAGE="${1:-elocaro:lokal}"
export ELOCARO_BACKUP_IMAGE="${2:-elocaro-backup:lokal}"
export ELOCARO_DOMAIN=localhost ACME_EMAIL=test@example.de POSTGRES_PASSWORD=restoretest
export MAIL_SENDER=test@example.de SMTP_HOST=localhost RESTIC_PASSWORD=restoretest BACKUP_API_TOKEN=restoretest-token
WORKDIR=$(mktemp -d)
export BACKUP_LOCAL_PATH="$WORKDIR/repo" BACKUP_SSH_PATH="$WORKDIR/ssh"

dc() { docker compose -p elocaro-restoretest -f docker-compose.yml "$@"; }
log() { printf '\n==> %s\n' "$*"; }
cleanup_files() {
    rc=$?
    dc down -v --remove-orphans > /dev/null 2>&1 || true
    # The repository is owned by root (from the container); without root privileges (CI runner) delete it inside the container.
    docker run --rm --entrypoint sh -v "$WORKDIR:/cleanup" "$ELOCARO_BACKUP_IMAGE" -c 'rm -rf /cleanup/repo /cleanup/ssh' > /dev/null 2>&1 || true
    rm -rf "$WORKDIR" || true
    exit "$rc"
}
trap cleanup_files EXIT

wait_for_app() {
    for _ in $(seq 1 90); do
        if [ "$(docker inspect --format '{{.State.Health.Status}}' "$(dc ps -q app)")" = healthy ]; then
            return 0
        fi
        sleep 2
    done
    dc logs app | tail -n 50
    echo "App wurde nicht gesund." >&2
    exit 1
}

sql() { dc run --rm -T --no-deps --entrypoint psql backup -v ON_ERROR_STOP=1 -X -q "$@"; }
sh_backup() { dc run --rm -T --no-deps --entrypoint sh backup -c "$1"; }

fingerprint() { # output file
    {
        echo "## Schema"
        dc run --rm -T --no-deps --entrypoint pg_dump backup --schema-only --no-owner | grep -v -e '^--' -e '^\\restrict' -e '^\\unrestrict' -e '^$'
        echo "## Daten"
        sql -tA <<'SQL'
SELECT format('SELECT %L, count(*), md5(coalesce(string_agg(t::text, E''\n'' ORDER BY t::text), '''')) FROM %I.%I t',
              table_schema || '.' || table_name, table_schema, table_name)
FROM information_schema.tables
WHERE table_type = 'BASE TABLE' AND table_schema NOT IN ('pg_catalog', 'information_schema')
ORDER BY 1
\gexec
SQL
        echo "## Dateien"
        sh_backup 'cd /data && find . -type f -exec stat -c "%u:%g %s %n" {} + | sort && find . -type f -exec sha256sum {} + | sort'
    } > "$1"
}

log "App starten (leere Datenbank, Migrationen)"
dc up -d app
wait_for_app
dc stop app

log "Testdaten anlegen"
content="Mietvertrag Müller – Testdatei"
sha=$(printf '%s' "$content" | sha256sum | cut -d' ' -f1)
sh_backup "mkdir -p /data/files/${sha%"${sha#??}"} && printf '%s' '$content' > /data/files/${sha%"${sha#??}"}/$sha && chown -R 1654:1654 /data"
sql <<SQL
INSERT INTO stored_files (id, name, mime_type, size, sha256, created_at, created_by, deleted)
VALUES ('0192a000-0000-7000-8000-000000000001', 'Mietvertrag Müller.txt', 'text/plain', $(printf '%s' "$content" | wc -c), '$sha', now(), 'restore-test', false);
SQL

fingerprint "$WORKDIR/before.txt"
grep -q "public.stored_files|1|" "$WORKDIR/before.txt" || { echo "Testdaten fehlen im Fingerabdruck" >&2; exit 1; }

log "Sichern"
dc run --rm -T --no-deps backup backup
sh_backup 'cat /status/backup.json' | tee "$WORKDIR/status.json"
grep -q '"succeeded":true' "$WORKDIR/status.json"

log "Alles löschen (Volumes), leere Datenbank starten"
dc down -v
dc up -d --wait postgres

log "Wiederherstellen"
dc run --rm -T --no-deps backup restore latest

fingerprint "$WORKDIR/after.txt"
log "Datenstand vergleichen"
if ! diff -u "$WORKDIR/before.txt" "$WORKDIR/after.txt"; then
    echo "Wiederhergestellter Datenstand weicht ab." >&2
    exit 1
fi

log "App auf wiederhergestellter Datenbank starten"
dc up -d app
wait_for_app
echo "Restore-Test (Konsole) erfolgreich: $(grep -c '|' "$WORKDIR/before.txt") Tabellen identisch."

# ---------- Phase 2: dieselbe Sicherung und Wiederherstellung über die API, wie die App sie benutzt ----------
# Die Wiederherstellung läuft hier bei laufender App (die API erzwingt sie, die Verbindungen werden getrennt).
api() { # GET: api /pfad   POST: api /pfad '{"json":…}'
    path=$1; shift
    dc exec -T backup wget -q -O - --header "Authorization: Bearer $BACKUP_API_TOKEN" --header 'Content-Type: application/json' ${1:+--post-data "$1"} "http://127.0.0.1:8080$path"
}
wait_idle() { # nach dem Start einer Operation warten, bis der Dienst wieder frei ist
    for _ in $(seq 1 120); do
        case "$(api /status)" in *'"running":false'*) return 0 ;; esac
        sleep 2
    done
    echo "Backup-Dienst wurde nicht frei." >&2
    exit 1
}
TARGET='{"repository":"/repo","password":"'"$RESTIC_PASSWORD"'"}'

log "API: Dienst starten, Anmeldung prüfen"
dc up -d --wait backup
if dc exec -T backup wget -q -O - http://127.0.0.1:8080/status > /dev/null 2>&1; then
    echo "Die API antwortet ohne Token." >&2
    exit 1
fi
dc exec -T backup wget -q -O - http://127.0.0.1:8080/health > /dev/null
api /test "{\"target\":$TARGET}" | tee "$WORKDIR/test.json"
grep -q '"Ready"' "$WORKDIR/test.json"

log "API: Sichern"
api /backup "{\"target\":$TARGET}" > /dev/null
wait_idle
api /status | tee "$WORKDIR/api-status.json"
grep -q '"kind":"Backup","succeeded":true' "$WORKDIR/api-status.json"
api /snapshots "{\"target\":$TARGET}" | tee "$WORKDIR/api-snapshots.json"
[ "$(grep -o '"shortId"' "$WORKDIR/api-snapshots.json" | wc -l)" -ge 2 ]

log "Daten verändern (Zeile löschen, Datei entfernen)"
sql -c "DELETE FROM stored_files"
sh_backup 'find /data -type f -delete'
fingerprint "$WORKDIR/changed.txt"
if diff -q "$WORKDIR/before.txt" "$WORKDIR/changed.txt" > /dev/null; then
    echo "Die Änderung wurde nicht erkannt, der Test wäre wirkungslos." >&2
    exit 1
fi

log "API: Wiederherstellen bei laufender App"
api /restore "{\"target\":$TARGET,\"snapshot\":\"latest\"}" > /dev/null
wait_idle
api /status | tee "$WORKDIR/api-restore.json"
grep -q '"kind":"Restore","succeeded":true' "$WORKDIR/api-restore.json"
dc restart app
wait_for_app

fingerprint "$WORKDIR/after-api.txt"
log "Datenstand nach API-Wiederherstellung vergleichen"
# Die App lief weiter und hat nach dem Neustart wieder Jobs ausgeführt: Protokoll und Zustand des Schedulers ändern sich von selbst.
# degree_day_values: filled by a background job (weather data download) while the app is running
volatile_rows() { grep -v -e '^public\.job_runs|' -e '^public\.qrtz_' -e '^public\.degree_day_values|' "$1"; }
volatile_rows "$WORKDIR/before.txt" > "$WORKDIR/before-stable.txt"
volatile_rows "$WORKDIR/after-api.txt" > "$WORKDIR/after-api-stable.txt"
if ! diff -u "$WORKDIR/before-stable.txt" "$WORKDIR/after-api-stable.txt"; then
    echo "Über die API wiederhergestellter Datenstand weicht ab." >&2
    exit 1
fi

log "API: falsches Passwort wird erkannt"
api /test '{"target":{"repository":"/repo","password":"falsch"}}' | grep -q '"WrongPassword"'

echo "Restore-Test erfolgreich (Konsole und API): $(grep -c '|' "$WORKDIR/before.txt") Tabellen identisch."
