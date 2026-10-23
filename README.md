# MietHahn 🐓

Self-hosted Verwaltung für private Vermieter: Objekte, Mieter, Mieteingänge, Nebenkosten- und Heizkostenabrechnung, Steuer (Anlage V/DATEV), Dokumente und Mieterportal.

**Stack:** .NET 10 · Blazor Static SSR · PostgreSQL · Docker Compose

## Dokumentation

- [Marktanalyse](docs/marktanalyse.md): was bestehende Software gut und schlecht macht
- [Fachlichkeit & Anforderungen](docs/fachlichkeit.md): Ergebnis des Anforderungsinterviews
- [Architektur](docs/architektur.md): Technologie, Struktur, Meilensteine
- [Entwicklung](docs/entwicklung.md): lokale Umgebung, Migrationen, Konventionen, Tests
- [Betrieb](docs/betrieb.md): Installation auf einem VPS, Update, Backup & Restore, Vollexport, Sicherheit
- [Mitarbeiten](CONTRIBUTING.md): Ablauf, CI, Releases
- [Banking & FinTS-Registrierung](docs/banking-fints.md)
- [Architekturentscheidungen (ADR)](docs/adr/)

## Umsetzungsplan

Der Plan liegt in den GitHub-Issues: ein Epic je Meilenstein, darunter Sub-Issues mit Aufgaben und Akzeptanzkriterien.

| Meilenstein | Epic |
|---|---|
| M0 Fundament (Solution, CI, Docker, Auth, PDF-Engine) | #1 |
| M1 Stammdaten (Objekte, WG-Zimmer, Verträge, Zähler) | #12 |
| M2 Finanzen & Bank (Sollstellungen, CSV/CAMT/MT940, FinTS, Mahnwesen, Belege + KI) | #19 |
| M3 Nebenkosten & Heizkosten (BetrKV, HeizkostenV, CO₂) | #27 |
| M4 Steuer (AfA, Darlehen, Anlage V, DATEV) | #35 |
| M5 Dokumente & Fristen (Vorlagen, Mieterhöhung, Protokolle) | #41 |
| M6 Mieterportal & Service (Tickets, Aushänge) | #47 |
| M7 Später (ERiC, Eigentümer je Objekt, Funkzähler) | #52 |

## Entwicklung

```bash
dotnet build && dotnet test   # Integrationstests brauchen Docker
dotnet run --project src/MietHahn.Web
```

Details zu lokaler Datenbank, Migrationen und Konventionen stehen in [docs/entwicklung.md](docs/entwicklung.md).

## Lizenz

MietHahn steht unter der [GNU Affero General Public License v3.0](LICENSE). Drittanbieter-Komponenten und ihre Lizenzen: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Version, Lizenz und Quellcode-Link zeigt auch die Seite „Über MietHahn“ (`/ueber`).
