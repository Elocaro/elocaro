# Architektur

## Leitentscheidungen

| Thema | Entscheidung | Begründung |
|---|---|---|
| Laufzeit | **.NET 10 (LTS)**, C# | Vorgabe, LTS bis Nov. 2028 |
| UI | **Blazor Web App, Static SSR** (Enhanced Navigation/Forms, Streaming Rendering). **Kein React**, kein WASM. Interaktive Server-Komponenten nur als begründete Ausnahme (ADR) | Vorgabe, einfaches Hosting, wenig JS, gut für mobile Erfassung |
| Datenbank | **PostgreSQL** via EF Core 10 + **Npgsql** (siehe [ADR 0002](adr/0002-ef-core-provider.md)) | EF-Support ab dem ersten Tag, Zeitraum-Constraints, transaktionale Migrations |
| Architekturstil | **Modularer Monolith**, ein Deployable | Kleine Nutzerzahl, einfacher Betrieb |
| Auth | ASP.NET Core Identity + TOTP-2FA, Magic Link für Mieter/Handwerker | Vorgabe |
| PDF | **Scriban**-HTML-Vorlagen → **Gotenberg** (Docker, Chromium) | Vorlagen ohne Code editierbar, ein Mechanismus für alle Dokumente |
| Jobs | **Quartz.NET** (persistenter Store in PostgreSQL) | Fristen, Sollstellungen, FinTS-Abruf, Mails |
| Banking | Parser für CSV/CAMT.053/MT940; FinTS via **libfintx** (LGPL-3.0, als NuGet unverändert eingebunden) | Vorgabe: Import und Direktabruf |
| OCR/KI | Interface `IBelegExtraktor` mit Providern **Claude API**, **Ollama** (lokal), **Keiner** | Konfigurierbar, Datenhoheit wählbar |
| Mail | **MailKit** (SMTP) | Standard |
| Dateien | Lokales Volume, Content-Addressed (SHA-256) | Einfach, backupbar |
| Hosting | **Docker Compose** auf VPS: `app`, `postgres`, `gotenberg`, `caddy` (HTTPS/Let's Encrypt) | Vorgabe |

## Lösungsstruktur

```
src/
  MietHahn.Domain/          Entitäten, Value Objects (Geld, Zeitraum, Fläche), Domänenregeln, reine Rechenkerne
  MietHahn.Application/     Use Cases (Commands/Queries), Interfaces (IBankImporter, IBelegExtraktor, IPdfRenderer, IMailer)
  MietHahn.Infrastructure/  EF Core (PostgreSQL), Migrations, Banking-Parser, FinTS, OCR-Provider, Gotenberg, MailKit, Quartz
  MietHahn.Web/             Blazor SSR: Areas /verwaltung, /portal (Mieter), /service (Hausmeister/Handwerker)
tests/
  MietHahn.Domain.Tests/        Rechenkerne (NK, HK, CO₂, AfA, Index) mit Golden-Master-Fällen
  MietHahn.Integration.Tests/   Testcontainers PostgreSQL, Gotenberg
  MietHahn.Web.Tests/           bUnit + Playwright E2E
deploy/
  docker-compose.yml, Caddyfile, backup/
```

**Abrechnungs-Rechenkerne** (NK, Heizkosten, CO₂, AfA, Mieterhöhung) sind reine Funktionen in `Domain` ohne DB-Zugriff. Sie liefern ein nachvollziehbares Rechenprotokoll, das im PDF und bei Prüfungen angezeigt wird.

## Querschnitt

- **Mandantenfähigkeit:** nicht nötig (eine Installation pro Vermieter). Alle Daten hängen aber an `ObjektId`, damit später Eigentümer je Objekt möglich sind.
- **Historisierung:** Gültigkeitszeiträume statt Überschreiben, Audit-Log (wer, wann, was) für alle Änderungen.
- **Sicherheit:** HTTPS über Caddy, Antiforgery (SSR-Forms), Rate-Limiting für Login/Magic Link, Secrets über Umgebungsvariablen bzw. Docker Secrets, FinTS-PIN wird nie gespeichert, Upload-Validierung (Typ/Größe).
- **Backups:** nächtlich `pg_dump` + Datei-Volume, verschlüsselt (restic), Restore-Test dokumentiert.
- **Lokalisierung:** nur Deutsch, Formate de-DE, Zeitzone Europe/Berlin.
- **Barrierearmut & Mobile:** responsive Layout, Erfassungsmasken (Zähler, Tickets) für Smartphones.
- **CI:** GitHub Actions: Build, Tests (inkl. Testcontainers), Format/Analyzer, Docker-Image nach GHCR.

## Meilensteine

| # | Meilenstein | Inhalt |
|---|---|---|
| M0 | Fundament | Repo, Solution, CI, Docker, Auth, Rollen, Layout, Audit |
| M1 | Stammdaten | Objekte, Einheiten, Zimmer, Mieter, Verträge, Mietmodelle, Zähler, Dokumentablage |
| M2 | Finanzen & Bank | Sollstellungen, Konten, CSV/CAMT/MT940, FinTS, Zuordnung, Offene Posten, Mahnwesen, Belege + OCR |
| M3 | Nebenkosten & Heizkosten | Kostenerfassung, Umlageschlüssel, Rechenkern BetrKV, HeizkostenV, CO₂, PDF, Versand, VZ-Anpassung |
| M4 | Steuer | AfA, Darlehen, Erhaltungsaufwand, Anlage V, DATEV-Export |
| M5 | Dokumente & Fristen | Vorlagen-Engine, Verträge, Mieterhöhung, Protokolle, Fristen-Erinnerungen |
| M6 | Portal & Service | Mieterportal, Tickets, Aushänge, Hausmeister/Handwerker |
| M7 | Später | ERiC, Eigentümer je Objekt, Funkzähler/UVI |
