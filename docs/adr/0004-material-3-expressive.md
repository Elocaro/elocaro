# ADR 0004: Material 3 Expressive als Gestaltungsgrundlage

- Status: angenommen
- Datum: 2026-10-20

## Kontext

ADR 0003 legt ein eigenes CSS-Design-System fest, aber nicht, wie es aussieht und nach welchen Regeln Seiten es nutzen. Im Bestand weichen Seiten voneinander ab (nicht definierte Klassen, Meldungen und leere Zustände als Rohmarkup, Löschen mal mit, mal ohne Rückfrage). Zukünftige Funktionen brauchen eine verbindliche Vorgabe.

## Entscheidung

- Die Gestaltung folgt **Material 3 Expressive** (Farbrollen, Formskala, Typoskala, Pill-Buttons, tonale Flächen), umgesetzt als **eigene CSS-Tokens** in `wwwroot/app.css`. Die Material-Web-Komponenten (JavaScript) werden **nicht** eingebunden.
- Farben werden ausschließlich als Rollen (`--md-*`) verwendet; Hell und Dunkel stehen je Rolle in `light-dark()`.
- Dark Mode folgt dem System und lässt sich im Benutzermenü auf Hell oder Dunkel festlegen (Cookie, kein JavaScript).
- Der verbindliche Leitfaden ist [docs/ui.md](../ui.md). Abweichungen davon sind Fehler, neue Muster kommen zuerst dort hinein.
- Schrift und Icons werden lokal ausgeliefert, nicht von einem CDN geladen (Self-Hosting, Datenschutz).

## Begründung

- M3 liefert ein ausgearbeitetes, barrierearm erprobtes Vokabular statt eigener Ad-hoc-Entscheidungen.
- Die Material-Web-Komponenten brauchen JavaScript und Web Components, was zu Static SSR (ADR 0001) und Navigation ohne JS (ADR 0003) nicht passt; nur die Gestaltung wird übernommen.
- `light-dark()` hält beide Themes in einer Definition und macht den Umschalter über `color-scheme` möglich.

## Konsequenzen

- Bestehende Seiten werden schrittweise auf die Bausteine umgestellt (Meldungen, Filter, leere Zustände, riskante Aktionen).
- Die Aliase `--color-*` und `--radius` bleiben, bis alle Seiten migriert sind.
- Ein Test soll Rohmarkup, das die Bausteine umgeht, beim `dotnet test` melden.
