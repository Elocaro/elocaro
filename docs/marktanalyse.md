# Marktanalyse: Software für private Vermieter (Stand Oktober 2026)

Ziel: Verstehen, was bestehende Lösungen gut und schlecht machen, und daraus den Funktionsumfang von MietHahn ableiten.

## Betrachtete Lösungen

| Lösung | Typ | Stärken | Schwächen |
|---|---|---|---|
| **immocloud** | SaaS, ab ca. 10 €/Monat | Starkes Mieterportal, solide Finanzverwaltung, DATEV-Export, NK-Abrechnung, Grundsteuer-Hilfe | Nicht auf viele Objekte ausgelegt, Daten liegen beim Anbieter, Abo |
| **objego** | SaaS, Basis kostenlos | Anlage-V-Unterstützung, schnelle Betriebskostenabrechnung, einfach | Kein Mieterportal, keine Kommunikation, Funktionen stark eingeschränkt |
| **hellohousing** | SaaS | Fokus NK-/Heizkostenabrechnung, Bankanbindung mit automatischer Mieteingangskontrolle, Funkzähler/UVI | Einsteigerfreundlich, aber Funktionslücken jenseits der Abrechnung |
| **vermietet.de** (Scout24) | SaaS | Gutes Preis-Leistungs-Verhältnis, Zeitersparnis bei NK | Kostenpflichtige Zusatzfunktionen, Beschwerden über Support und Abo-Abschluss |
| **Vermieten+** | SaaS | Einstieg für Kleinvermieter | Begrenzter Umfang |
| **WISO Hausverwalter / Lexware** | Desktop/Abo | Großer Funktionsumfang, Steuerbezug | Gewöhnungsbedürftige UI, Funktionen schwer auffindbar (z. B. Heizkostenabrechnung), kein Mieterportal |
| **Excel-Vorlagen** | Tabellen | Kostenlos, flexibel | Fehleranfällig (verschachtelte Formeln), Formfehler machen Nachzahlungen undurchsetzbar, keine Fristen/Kommunikation |
| **MicroRealEstate** | Open Source, self-hosted | Mieterportal, Dokumentvorlagen, Mietverfolgung, Docker | Nicht auf deutsches Recht ausgelegt (keine BetrKV/HeizkostenV/CO₂-Aufteilung/Anlage V) |

## Was gut läuft (übernehmen)

1. **Automatischer Mieteingangsabgleich** über Bankumsätze mit Offene-Posten-Liste.
2. **Geführte NK-Abrechnung** mit BetrKV-Kostenarten, Umlageschlüsseln und zeitanteiliger Verteilung bei Mieterwechsel.
3. **Mieterportal** als zentraler Kommunikationskanal (Dokumente, Schadensmeldungen, Aushänge) statt WhatsApp/E-Mail.
4. **Steuer-Auswertung** je Objekt/Jahr (Anlage V) und **DATEV-Export** für Steuerberater.
5. **Vorlagen** für wiederkehrende Schreiben (Mieterhöhung, Mahnung, Protokolle).

## Was schlecht läuft (vermeiden)

1. **Lock-in & Datenhoheit:** Daten beim SaaS-Anbieter, Export schwach. → MietHahn ist self-hosted, alle Daten exportierbar.
2. **Paywalls / Zusatzmodule:** Kernfunktionen (Heizkosten, Portal) kosten extra. → Alles inklusive.
3. **Überladene UIs:** Funktionen schwer zu finden. → Aufgabenorientierte Oberfläche („Was ist zu tun?“-Dashboard).
4. **Lücken bei der Heizkostenabrechnung:** Eigenabrechnung nach HeizkostenV inkl. CO₂-Kostenaufteilung ist selten gut gelöst, meist wird ein Messdienst vorausgesetzt. → MietHahn rechnet selbst.
5. **Kein Sonderfall WG mit Einzelzimmerverträgen:** Zimmer als Untereinheiten fehlen in den meisten Tools.
6. **Formfehler in Abrechnungen** (Fristen, Ausweis der Gesamtkosten und Schlüssel, §35a). → Plausibilitätsprüfungen vor dem Versand.

## Notwendige Features (abgeleitet)

**Muss:** Objekte/Einheiten/Zimmer, Mieter und Verträge (Standard-, Index-, Staffelmiete), Sollstellungen und Offene Posten, Bankimport (CSV/CAMT/MT940 und FinTS), Belege mit OCR, NK- und Heizkostenabrechnung (BetrKV, HeizkostenV, CO₂KostAufG, §35a), Fristen-Erinnerungen, Mahnwesen, Anlage-V-Auswertung inkl. AfA und Darlehen, DATEV-Export, PDF-Vorlagen, Mieterportal (Dokumente, Tickets, Aushänge), Rollen für Partner und Hausmeister/Handwerker, Backup/Export.

**Später:** ELSTER-Direktübermittlung (ERiC), Eigentümer je Objekt mit Anteilen, Funkzähler/UVI, Gewerbe/USt.

## Quellen

- [trusted.de: Hausverwaltungssoftware-Vergleich](https://trusted.de/hausverwaltungssoftware) (Affiliate-Seite)
- [trusted.de: vermietet.de Bewertung](https://trusted.de/vermietet-de-bewertung)
- [softwareabc24.de: vermietet.de](https://www.softwareabc24.de/hausverwaltung-software/vermietet-de)
- [softwareabc24.de: Nebenkostenabrechnung-Software](https://www.softwareabc24.de/nebenkostenabrechnung-software/)
- [erfahrungenscout.de: vermietet.de](https://erfahrungenscout.de/dienstleistungen/vermietet-bewertungen)
- [heise: Hausverwaltungssoftware im Vergleich](https://www.heise.de/download/specials/Hausverwaltungssoftware-im-Vergleich-7151831)
- [heise: objego im Test](https://www.heise.de/download/specials/objego-im-Test-Immobilien-einfach-verwalten-und-vermieten-9163013)
- [Buhl: WISO Hausverwalter Rezensionen](https://www.buhl.de/c-j/wiso-hausverwalter-365-start/?review=10)
- [L-IZ: Vermieter rechnen, Mieter kontrollieren – und Excel spielt nicht immer mit](https://www.l-iz.de/wirtschaft/verbraucher/2025/06/vermieter-rechnen-mieter-kontrollieren-und-excel-spielt-nicht-immer-mit-627055)
- [whk-controlling: Hausverwaltung mit Excel](https://www.whk-controlling.de/wissen/hausverwaltung-excel)
- [drweb.de: Buchhaltungssoftware für Immobilienverwaltung](https://www.drweb.de/buchhaltungssoftware-fuer-immobilienverwaltung-vergleich/)
- [MicroRealEstate (ecosyste.ms)](https://awesome.ecosyste.ms/projects/2447168)
