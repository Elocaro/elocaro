# MietHahn 🐓

Self-hosted Verwaltung für private Vermieter: Objekte, Mieter, Mieteingänge, Nebenkosten- und Heizkostenabrechnung, Steuer (Anlage V/DATEV), Dokumente und Mieterportal.

**Stack:** .NET 10 · Blazor Static SSR · PostgreSQL · Docker Compose

## Dokumentation

- [Marktanalyse](docs/marktanalyse.md): was bestehende Software gut und schlecht macht
- [Fachlichkeit & Anforderungen](docs/fachlichkeit.md): Ergebnis des Anforderungsinterviews
- [Architektur](docs/architektur.md): Technologie, Struktur, Meilensteine
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

Voraussetzung: .NET SDK 10.0.1xx (siehe `global.json`).

```bash
dotnet build          # Warnungen gelten als Fehler
dotnet test           # Microsoft.Testing.Platform + xUnit v3
dotnet format --verify-no-changes
dotnet run --project src/MietHahn.Web
```

Paketversionen werden zentral in `Directory.Packages.props` gepflegt, gemeinsame Build-Einstellungen in `Directory.Build.props`.
