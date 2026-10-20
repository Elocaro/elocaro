# ADR 0001: Blazor Static SSR als UI-Technologie

- Status: angenommen, teilweise abgelöst durch [ADR 0005](0005-interaktive-seiten-mit-mudblazor.md) (Interactive Server schrittweise pro Seite)
- Datum: 2026-10-18

## Kontext

Vorgabe: .NET mit Blazor SSR, kein React. Die Anwendung ist formularlastig (Stammdaten, Erfassung, Abrechnungen) und wird auch mobil genutzt (Zählerablesung, Tickets).

## Entscheidung

- Render-Modus **Static SSR** als Standard, mit Enhanced Navigation und Enhanced Form Handling sowie Streaming Rendering für langsame Seiten.
- **Interactive Server** nur für einzelne Komponenten, bei denen Formular-Roundtrips unzumutbar sind (Kandidat: Umsatz-Zuordnung), jeweils mit eigenem ADR.
- Kein WebAssembly, kein JS-Framework. Minimales eigenes JS nur für Kamera-Upload und Progressive Enhancement.
- CSS: schlankes eigenes Design-System (CSS-Variablen) oder Pico/Bootstrap. Entschieden in Issue „Layout & Design-System“.

## Konsequenzen

Einfaches Hosting ohne dauerhafte SignalR-Verbindungen, robuste mobile Nutzung. Interaktive UX (Drag & Drop, Live-Validierung) ist eingeschränkt und wird über Formular-Roundtrips gelöst.
