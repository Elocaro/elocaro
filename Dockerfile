# syntax=docker/dockerfile:1

# ---------- Build ----------
# Runs on the build host (no emulation); dotnet publish targets amd64 or arm64 through the runtime identifier below.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /quelle

# Copy only the project files first: the restore layer stays cached as long as no packages change.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig BannedSymbols.txt ./
COPY src/Elocaro.Domain/Elocaro.Domain.csproj src/Elocaro.Domain/
COPY src/Elocaro.Application/Elocaro.Application.csproj src/Elocaro.Application/
COPY src/Elocaro.Infrastructure/Elocaro.Infrastructure.csproj src/Elocaro.Infrastructure/
COPY src/Elocaro.Web/Elocaro.Web.csproj src/Elocaro.Web/
# Publish only for the target architecture (amd64 or arm64) so that no foreign native libraries
# (e.g. SkiaSharp for Windows/macOS) end up in the image.
ARG TARGETARCH
# Release version (tag without "v"); shown on the "Über Elocaro" page
ARG VERSION=0.0.0-dev
RUN case "$TARGETARCH" in arm64) echo linux-arm64 ;; *) echo linux-x64 ;; esac > /tmp/rid \
 && dotnet restore src/Elocaro.Web/Elocaro.Web.csproj -r "$(cat /tmp/rid)"

COPY src/ src/
# No --no-restore: the restore above ran before the .razor files were copied, so the Blazor scripts of the framework
# (_framework/blazor.web.js, 404 without them: no interactive page works) are missing from the published assets.
# The second restore is quick, the packages are already in the layer cache.
RUN dotnet publish src/Elocaro.Web/Elocaro.Web.csproj -c Release -o /app \
      -r "$(cat /tmp/rid)" --self-contained false /p:UseAppHost=false /p:DebugType=none /p:Version="$VERSION" /p:InformationalVersion="$VERSION"

# OAuth client for the sign-in at Google (backup to Google Drive): only in images that the release pipeline builds with the secrets
# (BuildKit secrets, so that the values appear neither in the layers' commands nor in the build arguments). Without them the file is
# missing and every installation sets up its own client in the app or in the environment.
# The cache does not know the contents of a secret: the pipeline passes the commit as a build argument so that this layer is rebuilt
# (and a rotated secret takes effect) with every release.
ARG GOOGLE_CLIENT_REV=
RUN --mount=type=secret,id=google_client_id --mount=type=secret,id=google_client_secret \
    : "$GOOGLE_CLIENT_REV" && \
    if [ -s /run/secrets/google_client_id ] && [ -s /run/secrets/google_client_secret ]; then \
      printf '{"clientId":"%s","clientSecret":"%s"}' "$(cat /run/secrets/google_client_id)" "$(cat /run/secrets/google_client_secret)" > /app/google-client.json; \
    fi

# Prepare the data directory; the runtime image has no shell for mkdir/chown.
RUN mkdir -p /data/files

# ---------- Runtime ----------
# chiseled: no shell or package manager, runs as non-root (UID 1654); "extra" contains ICU and time zone data
# (German formatting, Europe/Berlin).
FROM mcr.microsoft.com/dotnet/aspnet:11.0-resolute-chiseled-extra AS laufzeit
WORKDIR /app
COPY --from=build /app .
# Owner = app user so that a new Docker volume is initialized writable.
COPY --from=build --chown=$APP_UID:$APP_UID /data /data

ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=0 \
    TZ=Europe/Berlin \
    FileStore__Path=/data/files \
    Hosting__HttpsRedirection=false

# File storage as a volume (owner: the image's app user)
VOLUME ["/data"]
EXPOSE 8080
USER $APP_UID

HEALTHCHECK --interval=30s --timeout=6s --start-period=30s --retries=3 \
    CMD ["dotnet", "Elocaro.Web.dll", "healthcheck"]

ENTRYPOINT ["dotnet", "Elocaro.Web.dll"]
