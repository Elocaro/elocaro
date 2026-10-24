# Änderungen

Format nach [Keep a Changelog](https://keepachangelog.com/de/1.1.0/), Versionen nach [SemVer](https://semver.org/lang/de/).

## [Unveröffentlicht]

### Hinzugefügt
- Unterstützung vorbereitet: `docs/foerderung.md`, Feature-Sponsoring (Ablauf, Regeln, Issue-Vorlage, Label `sponsoring`), Konzept Rechtsstände-Abo, `.github/FUNDING.yml` (inaktiv, bis ein Konto eingetragen ist).
- Kaution: Tab im Vertrag mit Eingängen, Zinsen, Rückzahlung und Verrechnung; Hinweise auf dem Dashboard bei offener Kaution und nach Mietende.
- Beispieldaten (Haus, vier Wohnungen, drei Mieter mit unterschiedlichem Zahlungsverhalten) für eine leere Probe-Installation.
- Anleitung für die Installation auf Portainer, Coolify, Synology, Unraid, Proxmox und ARM (ohne ungeprüfte Plattformvorlagen).
- Übersicht: Kennzahlen zu Mieteingang, Rückständen und Belegung.
- Datenimport aus einer Tabelle (CSV): Objekte, Einheiten mit Wohnfläche, Mieter, Verträge, Kaution und Saldovortrag mit Prüfung vorab; alles oder nichts, wiederholbar ohne Doppelungen.
- Mieterportal: Seite „Konto“ mit Saldo, Forderungen und Zahlungen der eigenen Mietverhältnisse.
- Seite „Rechtsstände“: Basiszinssatz, Verbraucherpreisindex, CO₂-Stufen und Gradtagszahlen mit Stand, Quelle und Zustand; Dashboard-Hinweis bei fehlenden oder veralteten Werten.
- Lizenz AGPL-3.0, Seite „Über MietHahn“ (`/ueber`) mit Version, Lizenz und Quellcode-Link.
- README mit Screenshots, Zielgruppe und Grenzen; `SECURITY.md`; Issue-Vorlagen.
- `deploy/install.sh`: Installation in einem Schritt (auch `--lokal` zum Ausprobieren), prüft in der CI die Konfiguration.
- Version der Anwendung aus dem Release-Tag (`VERSION`-Build-Argument im Dockerfile).

### Geändert
- Datenimport und Beispieldaten buchen die Kaution als eingegangen (Spalte „Kaution eingegangen“), damit das Dashboard nicht für jeden übernommenen Vertrag „Kaution offen“ meldet.
- Einrichtungsassistent: Das erste Objekt wird mit dem Haken „Erstes Objekt jetzt anlegen“ erfasst; ohne Haken überspringt „Weiter“ den Schritt, statt Pflichtfeld-Fehler zu zeigen.
- Zahlungszuordnung: Betrag, Mietobjekt und Zeitraum im Verwendungszweck ergeben auch ohne Namen der Gegenseite mindestens „mittel“.
- Einrichtungsassistent: höchstens sieben Schritte statt 19, Vorauswahl aus der Frage „Wie vermieten Sie?“, legt Vermieterprofil, erstes Objekt mit Wohnungen und Standardvorlagen an, blendet die Navigation aus.
- Dashboard auf neuen Installationen: Checkliste „So geht es los“ statt „Alles erledigt“.
- Vertragsdokument, Mieterhöhung und Schreiben zeigen fehlende Voraussetzungen (Vermieterprofil, Vorlagen, Wohnfläche) vorab; Standardvorlagen lassen sich dort mit einem Klick anlegen.
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
