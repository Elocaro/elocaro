# Änderungen

Format nach [Keep a Changelog](https://keepachangelog.com/de/1.1.0/), Versionen nach [SemVer](https://semver.org/lang/de/).

## [Unveröffentlicht]

### Hinzugefügt
- Lizenz AGPL-3.0, Seite „Über MietHahn“ (`/ueber`) mit Version, Lizenz und Quellcode-Link.
- README mit Screenshots, Zielgruppe und Grenzen; `SECURITY.md`; Issue-Vorlagen.
- `deploy/install.sh`: Installation in einem Schritt (auch `--lokal` zum Ausprobieren), prüft in der CI die Konfiguration.
- Version der Anwendung aus dem Release-Tag (`VERSION`-Build-Argument im Dockerfile).

### Geändert
- `global.json` zurück auf .NET SDK 10.

## [0.1.0] – noch nicht veröffentlicht

Erste lauffähige Fassung: Objekte, Mieter, Verträge, Mieterkonto, Bankabgleich (Datei, FinTS), Belege, Nebenkosten- und Heizkostenabrechnung,
Anlage V, DATEV-Export, Dokumente und Vorlagen, Mieterportal, Tickets, Fristen, Eigentümer je Objekt, Zählerimport und Verbrauchsinformation.
