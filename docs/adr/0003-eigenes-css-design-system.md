# ADR 0003: Eigenes CSS-Design-System statt CSS-Framework

- Status: angenommen
- Datum: 2026-10-18

## Kontext

Blazor-Vorlagen bringen Bootstrap mit, Alternativen wären Pico.css oder Tailwind. MietHahn hat wenige, wiederkehrende Bausteine: Formulare, Tabellen, Statusanzeigen, Karten, Navigation. Es wird statisch gerendert (kein JS-Framework) und soll mobil gut bedienbar sein.

## Entscheidung

Ein **eigenes, schlankes Design-System** in `wwwroot/app.css`:

- Farben, Abstände und Radien als **CSS-Variablen**, mit **Dark Mode** über `prefers-color-scheme`.
- Mobil zuerst: Touch-Ziele ≥ 44 px, Navigation ohne JavaScript (`<details>`), Tabellen in `.table-wrapper` scrollen horizontal statt der Seite.
- Klassen auf Englisch, passend zum Code (`.button`, `.field`, `.message`, `.data-table`, `.status-ok`).
- Komponenten-spezifisches CSS bei Bedarf als CSS-Isolation (`*.razor.css`).

## Begründung

- Keine Abhängigkeit und kein Build-Schritt (Tailwind bräuchte Node), kleine Datei (< 15 KB).
- Bootstrap brächte viel Ungenutztes und eigene Klassennamen-Konventionen mit; Pico gestaltet Elemente global und erschwert Abweichungen.
- Volle Kontrolle über Barrierearmut (Kontraste, Fokus) und Druckansichten.

## Konsequenzen

Neue UI-Bausteine werden im Design-System ergänzt, nicht ad hoc in Seiten. Die Übersicht der Klassen steht in `docs/entwicklung.md` → Oberfläche.
