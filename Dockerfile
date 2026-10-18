# syntax=docker/dockerfile:1

# ---------- Build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /quelle

# Zuerst nur Projektdateien kopieren: der Restore-Layer bleibt gecacht, solange sich keine Pakete ändern.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/MietHahn.Domain/MietHahn.Domain.csproj src/MietHahn.Domain/
COPY src/MietHahn.Application/MietHahn.Application.csproj src/MietHahn.Application/
COPY src/MietHahn.Infrastructure/MietHahn.Infrastructure.csproj src/MietHahn.Infrastructure/
COPY src/MietHahn.Web/MietHahn.Web.csproj src/MietHahn.Web/
# Nur für die Zielarchitektur veröffentlichen (amd64 oder arm64), damit keine fremden nativen Bibliotheken
# (z. B. SkiaSharp für Windows/macOS) im Image landen.
ARG TARGETARCH
RUN case "$TARGETARCH" in arm64) echo linux-arm64 ;; *) echo linux-x64 ;; esac > /tmp/rid \
 && dotnet restore src/MietHahn.Web/MietHahn.Web.csproj -r "$(cat /tmp/rid)"

COPY src/ src/
RUN dotnet publish src/MietHahn.Web/MietHahn.Web.csproj -c Release -o /app --no-restore \
      -r "$(cat /tmp/rid)" --self-contained false /p:UseAppHost=false /p:DebugType=none

# Datenverzeichnis vorbereiten; das Laufzeit-Image hat keine Shell für mkdir/chown.
RUN mkdir -p /daten/dateien

# ---------- Laufzeit ----------
# chiseled: ohne Shell und Paketmanager, läuft als Non-Root (UID 1654); „extra“ enthält ICU und Zeitzonendaten
# (deutsche Formatierung, Europe/Berlin).
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS laufzeit
WORKDIR /app
COPY --from=build /app .
# Besitzer = App-Benutzer, damit ein neues Docker-Volume beschreibbar initialisiert wird.
COPY --from=build --chown=$APP_UID:$APP_UID /daten /daten

ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=0 \
    TZ=Europe/Berlin \
    Dateiablage__Pfad=/daten/dateien \
    Betrieb__HttpsUmleitung=false

# Dateiablage als Volume (Eigentümer: app-Benutzer des Images)
VOLUME ["/daten"]
EXPOSE 8080
USER $APP_UID

HEALTHCHECK --interval=30s --timeout=6s --start-period=30s --retries=3 \
    CMD ["dotnet", "MietHahn.Web.dll", "healthcheck"]

ENTRYPOINT ["dotnet", "MietHahn.Web.dll"]
