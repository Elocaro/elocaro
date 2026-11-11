# syntax=docker/dockerfile:1
# Elocaro backup sidecar: small ASP.NET Core API (src/Elocaro.Backup) around pg_dump, restic and rclone.
# Build context: repository root (docker build -f deploy/backup/Dockerfile .)

# ---------- Build ----------
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /quelle
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig BannedSymbols.txt ./
COPY src/Elocaro.Backup/Elocaro.Backup.csproj src/Elocaro.Backup/
ARG TARGETARCH
RUN case "$TARGETARCH" in arm64) echo linux-arm64 ;; *) echo linux-x64 ;; esac > /tmp/rid \
 && dotnet restore src/Elocaro.Backup/Elocaro.Backup.csproj -r "$(cat /tmp/rid)"
COPY src/Elocaro.Backup/ src/Elocaro.Backup/
ARG VERSION=0.0.0-dev
RUN dotnet publish src/Elocaro.Backup/Elocaro.Backup.csproj -c Release -o /app \
      -r "$(cat /tmp/rid)" --self-contained false /p:UseAppHost=false /p:DebugType=none /p:Version="$VERSION" /p:InformationalVersion="$VERSION"

# restic is statically linked and is only copied (version pinned here).
FROM restic/restic:0.19.1 AS restic

# ---------- Runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:11.0-alpine
# postgresql18-client: pg_dump/pg_restore/psql matching the server (postgres:18); rclone: Google Drive and other targets;
# openssh-client: SFTP; tzdata: schedule in local time.
RUN apk add --no-cache postgresql18-client rclone openssh-client-default tzdata
COPY --from=restic /usr/bin/restic /usr/local/bin/restic
COPY --from=build /app /app
# Console entry point: docker compose run --rm backup restore (also the entry point of the container).
RUN printf '#!/bin/sh\nexec dotnet /app/Elocaro.Backup.dll "$@"\n' > /usr/local/bin/elocaro-backup && chmod 755 /usr/local/bin/elocaro-backup

ENV TZ=Europe/Berlin \
    ASPNETCORE_HTTP_PORTS=8080 \
    PGHOST=postgres \
    PGUSER=elocaro \
    PGDATABASE=elocaro \
    PGGSSENCMODE=disable \
    RESTIC_CACHE_DIR=/cache

HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD wget -q -O /dev/null http://127.0.0.1:8080/health || exit 1

ENTRYPOINT ["elocaro-backup"]
CMD ["serve"]
