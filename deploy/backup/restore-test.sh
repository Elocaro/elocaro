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
export MIETHAHN_IMAGE="${1:-miethahn:lokal}"
export MIETHAHN_BACKUP_IMAGE="${2:-miethahn-backup:lokal}"
export MIETHAHN_DOMAIN=localhost ACME_EMAIL=test@example.de POSTGRES_PASSWORD=restoretest
export MAIL_SENDER=test@example.de SMTP_HOST=localhost RESTIC_PASSWORD=restoretest
WORKDIR=$(mktemp -d)
export BACKUP_LOCAL_PATH="$WORKDIR/repo" BACKUP_SSH_PATH="$WORKDIR/ssh"

dc() { docker compose -p miethahn-restoretest -f docker-compose.yml "$@"; }
log() { printf '\n==> %s\n' "$*"; }
cleanup_files() {
    rc=$?
    dc down -v --remove-orphans > /dev/null 2>&1 || true
    # The repository is owned by root (from the container); without root privileges (CI runner) delete it inside the container.
    docker run --rm --entrypoint sh -v "$WORKDIR:/cleanup" "$MIETHAHN_BACKUP_IMAGE" -c 'rm -rf /cleanup/repo /cleanup/ssh' > /dev/null 2>&1 || true
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
echo "Restore-Test erfolgreich: $(grep -c '|' "$WORKDIR/before.txt") Tabellen identisch."
