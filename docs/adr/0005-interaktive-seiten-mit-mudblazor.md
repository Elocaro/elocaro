# ADR 0005: Interaktive Seiten (Interactive Server) mit MudBlazor

- Status: angenommen
- Datum: 2026-10-20
- Ergänzt ADR 0001 (Static SSR) und ADR 0004 (Gestaltung)

## Kontext

ADR 0001 sah Interactive Server nur für Einzelfälle vor. Das Frontend soll aber durchgängig interaktiv werden: Live-Suche, Filter und Sortierung, Dialoge und Bearbeiten ohne Seitenwechsel, Assistenten (Abrechnung) und Dashboards mit Diagrammen. Das war in der ersten Konzeption missverstanden worden.

## Entscheidung

- **Render-Modus Interactive Server, schrittweise pro Seite.** Eine Seite wird mit `@rendermode InteractiveServer` interaktiv, alle anderen bleiben statisch und laufen unverändert weiter. Anmeldung, Magic Link und Abmeldung bleiben dauerhaft statisch (Cookies lassen sich nicht über eine Circuit setzen).
- **MudBlazor** liefert die interaktiven Bausteine (Tabellen, Dialoge, Snackbar, Auswahl, Datepicker, Diagramme). Es wird mit dem Theme `M3Theme` an das Design nach ADR 0004 angepasst.
- **Seiten dürfen `Mud*`-Komponenten direkt verwenden.** Es gibt keine Wrapper-Schicht auf Vorrat. Eigene Komponenten entstehen dort, wo MudBlazor etwas nicht oder schlecht kann. Sie haben **keine MudBlazor-Typen in ihrer Schnittstelle** (einfache Parameter, `EventCallback`s), damit sie sich später austauschen lassen. Beispiele: `InteractiveShell` (Rahmen mit Providern und Hell/Dunkel), `Notifier` (Rückmeldungen).
- Static-SSR-Seiten behalten ihre Bausteine aus `docs/ui.md`.
- **Interaktiv wird, was Eingaben oder Live-Verhalten hat.** Reine Anzeigeseiten (z. B. Hintergrundjobs, Datensicherung, Belegauslesung, Dashboard) bleiben statisch: schneller, ohne Verbindung pro Tab.

## Folgen für die Technik

- **Benutzer in der Circuit:** Es gibt keinen `HttpContext`. `CircuitUser` hält den Benutzer der Circuit, `HttpCurrentUser` liest ihn dort (sonst wären Audit-Felder „system“).
- **Sitzung prüfen:** `RevalidatingAuthenticationStateProvider` vergleicht jede Minute den Security-Stamp; gesperrte Konten und geänderte Rollen wirken auch in offenen Seiten.
- **URL und Zustand:** Der Router ist statisch. Query-Parameter liefern nur den Startwert einer interaktiven Seite; danach zählt ihr Zustand (kein URL-Update beim Filtern). Das ändert sich, wenn der Router später interaktiv wird.
- **Rückmeldungen:** Statt Status-Cookie nach Weiterleitung nutzen interaktive Seiten `Notifier` (Snackbar).
- **CSP:** `script-src 'self'` bleibt; die Import-Map bekommt ein Nonce pro Anfrage (`OperationsExtensions.CspNonceKey`). Der zusätzliche CSP-Header des Frameworks ist abgeschaltet.
- **Tests:** Interaktives Verhalten prüfen Browser-Tests (Playwright). Warten auf `[data-interactive=true]` (setzt `InteractiveShell`, sobald die Circuit verbunden ist). Die Testuhr ist ein `FakeTimeProvider`, deshalb muss der Test sie für Debounce-Timer vorstellen. HTTP-Tests über `TestBrowser` sehen nur den Prerender.
- **Eigenes CSS und MudBlazor:** Elementregeln in `app.css` (`button`, `input`, `select`, `textarea`) gelten nicht für Elemente mit `mud-`-Klassen.
- **Betrieb:** Jede offene Seite hält eine WebSocket-Verbindung (SignalR). Reverse Proxy (Caddy) muss WebSockets durchlassen; bei Neustart verbinden sich Seiten neu.

## Konsequenzen

- Für jede umgestellte Seite sind die Formulare auf `OnValidSubmit` ohne `[SupplyParameterFromForm]` umzustellen, Meldungen auf `Notifier`, und ihre HTTP-Tests auf Browser-Tests.
- Die Architekturregeln gegen Rohmarkup (`UiRulesTests`) gelten weiter für statische Seiten; für interaktive Seiten kommen eigene Regeln hinzu, sobald die Muster feststehen.

## Stand der Umstellung (2026-10-20)

Alle Bereiche der Verwaltung sind interaktiv: Personen, Objekte, Verträge, Dokumente, Benutzer, Einstellungen, Betrieb (Mail), Zähler, Bank, Finanzen, Belege, Steuer und Betriebskosten. Statisch bleiben die reinen Anzeigeseiten (Start, Hintergrundjobs, Datensicherung, Belegauslesung, Offene Posten), die Anmeldung und die Konto-Seiten sowie Service und Portale. Deren Layouts wurden nicht mit der interaktiven Oberfläche zusammen geprüft.
