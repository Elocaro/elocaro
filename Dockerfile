# syntax=docker/dockerfile:1

# ---------- Build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /quelle

# Copy only the project files first: the restore layer stays cached as long as no packages change.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/MietHahn.Domain/MietHahn.Domain.csproj src/MietHahn.Domain/
COPY src/MietHahn.Application/MietHahn.Application.csproj src/MietHahn.Application/
COPY src/MietHahn.Infrastructure/MietHahn.Infrastructure.csproj src/MietHahn.Infrastructure/
COPY src/MietHahn.Web/MietHahn.Web.csproj src/MietHahn.Web/
# Publish only for the target architecture (amd64 or arm64) so that no foreign native libraries
# (e.g. SkiaSharp for Windows/macOS) end up in the image.
ARG TARGETARCH
RUN case "$TARGETARCH" in arm64) echo linux-arm64 ;; *) echo linux-x64 ;; esac > /tmp/rid \
 && dotnet restore src/MietHahn.Web/MietHahn.Web.csproj -r "$(cat /tmp/rid)"

COPY src/ src/
RUN dotnet publish src/MietHahn.Web/MietHahn.Web.csproj -c Release -o /app --no-restore \
      -r "$(cat /tmp/rid)" --self-contained false /p:UseAppHost=false /p:DebugType=none

# Prepare the data directory; the runtime image has no shell for mkdir/chown.
RUN mkdir -p /data/files

# ---------- Runtime ----------
# chiseled: no shell or package manager, runs as non-root (UID 1654); "extra" contains ICU and time zone data
# (German formatting, Europe/Berlin).
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS laufzeit
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
    CMD ["dotnet", "MietHahn.Web.dll", "healthcheck"]

ENTRYPOINT ["dotnet", "MietHahn.Web.dll"]
