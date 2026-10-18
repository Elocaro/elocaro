# MietHahn 🐓

Self-hosted Verwaltung für private Vermieter: Objekte, Mieter, Mieteingänge, Nebenkosten- und Heizkostenabrechnung, Steuer (Anlage V/DATEV), Dokumente und Mieterportal.

**Stack:** .NET 10 · Blazor Static SSR · MariaDB · Docker Compose

## Dokumentation

- [Marktanalyse](docs/marktanalyse.md): was bestehende Software gut und schlecht macht
- [Fachlichkeit & Anforderungen](docs/fachlichkeit.md): Ergebnis des Anforderungsinterviews
- [Architektur](docs/architektur.md): Technologie, Struktur, Meilensteine
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
