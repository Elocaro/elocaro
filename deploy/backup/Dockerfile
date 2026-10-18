# MietHahn-Backup: pg_dump (passend zur Server-Version) + restic, Zeitplan per Shell-Schleife.
FROM restic/restic:0.19.0 AS restic

FROM postgres:18-alpine
# openssh-client für SFTP-Ziele; restic ist statisch gelinkt und wird nur kopiert.
RUN apk add --no-cache openssh-client-default
COPY --from=restic /usr/bin/restic /usr/local/bin/restic
COPY --chmod=755 miethahn-backup.sh /usr/local/bin/miethahn-backup

ENV TZ=Europe/Berlin \
    PGHOST=postgres \
    PGUSER=miethahn \
    PGDATABASE=miethahn \
    PGGSSENCMODE=disable \
    RESTIC_CACHE_DIR=/cache

ENTRYPOINT ["miethahn-backup"]
CMD ["zeitplan"]
