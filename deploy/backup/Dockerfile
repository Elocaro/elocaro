# Elocaro backup: pg_dump (matching the server version) + restic, schedule via shell loop.
FROM restic/restic:0.19.0 AS restic

FROM postgres:18-alpine
# openssh-client for SFTP targets; restic is statically linked and is only copied.
RUN apk add --no-cache openssh-client-default
COPY --from=restic /usr/bin/restic /usr/local/bin/restic
COPY --chmod=755 elocaro-backup.sh /usr/local/bin/elocaro-backup

ENV TZ=Europe/Berlin \
    PGHOST=postgres \
    PGUSER=elocaro \
    PGDATABASE=elocaro \
    PGGSSENCMODE=disable \
    RESTIC_CACHE_DIR=/cache

ENTRYPOINT ["elocaro-backup"]
CMD ["schedule"]
