# Änderungen

Format nach [Keep a Changelog](https://keepachangelog.com/de/1.1.0/), Versionen nach [SemVer](https://semver.org/lang/de/).

## [Unveröffentlicht]

### Hinzugefügt
- Lizenz AGPL-3.0, Seite „Über MietHahn“ (`/ueber`) mit Version, Lizenz und Quellcode-Link.
- README mit Screenshots, Zielgruppe und Grenzen; `SECURITY.md`; Issue-Vorlagen.
- `deploy/install.sh`: Installation in einem Schritt (auch `--lokal` zum Ausprobieren), prüft in der CI die Konfiguration.
- Version der Anwendung aus dem Release-Tag (`VERSION`-Build-Argument im Dockerfile).

### Geändert
- Einladung an Mieter und Handwerker: Rückfrage vor dem Versand, Statusanzeige (versendet, nicht zugestellt, angemeldet), „Einladung erneut senden“, Mail nennt den Vermieter.
- Person anlegen: nur Anrede, Name, E-Mail und Telefon sichtbar, alles Weitere unter „Weitere Angaben“.
- Datums- und Monatsfelder: Formathinweis, Eingabe auch ohne Punkte, verständliche Fehlermeldung; „Buchhaltung ab (Monat)“ mit Monatsauswahl.
- Einheit anlegen: Wohnfläche und Gültigkeitsdatum direkt im Dialog.
- Portal: Verträge mit künftigem Mietbeginn zeigen die Miete ab Beginn statt 0 €.
- Texte durchgängig in Sie-Form (mit Architekturtest), klarere Hinweise zu Buchhaltungsbeginn und Vorjahresabweichung.
- `global.json` zurück auf .NET SDK 10.

## [0.1.0] – noch nicht veröffentlicht

Erste lauffähige Fassung: Objekte, Mieter, Verträge, Mieterkonto, Bankabgleich (Datei, FinTS), Belege, Nebenkosten- und Heizkostenabrechnung,
Anlage V, DATEV-Export, Dokumente und Vorlagen, Mieterportal, Tickets, Fristen, Eigentümer je Objekt, Zählerimport und Verbrauchsinformation.
