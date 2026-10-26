# Änderungen

Format nach [Keep a Changelog](https://keepachangelog.com/de/1.1.0/), Versionen nach [SemVer](https://semver.org/lang/de/).

## [Unveröffentlicht]

### Geändert
- Nebenkosten- und Heizkostenabrechnung: Der gemessene Verbrauch einer WG-Wohnung wird nur noch auf vermietete Zimmer verteilt; ein leeres Zimmer nimmt keinen Verbrauch auf, Flächenanteile (Grundsteuer, Versicherung, Grundkosten) bleiben beim Vermieter.
- Navigation gebündelt: sechs Bereiche statt vierzehn (Bestand, Kommunikation, Geld statt je einzelner Seiten), höchstens zwei Tab-Zeilen, neue untere Leiste auf dem Handy (Aufgaben, Bestand, Geld, Kommunikation, Menü). Die Seitenadressen bleiben gleich.

### Hinzugefügt
- Rechenzähler: Ein Zähler, der für mehrere Wohnungen zählt, lässt sich mit anderen Zählern verrechnen (addieren/abziehen), um den Verbrauch je Wohnung zu ermitteln, z. B. Kaltwasser 1. OG aus dem Zähler „1. und 2. OG“ minus dem Zähler „2. OG“ (Closes #113).
- Wohnungen: Kostenarten markieren, die die Mieter selbst bezahlen (z. B. Müll): Die Wohnung nimmt dann an der Umlage dieser Kostenart nicht teil, weder Mieter noch Vermieter tragen einen Anteil (Closes #117).
- Heizkosten: Basis der Aufteilung Heizung/Warmwasser (§ 9 HeizkostenV) wählbar, entweder Brennstoffenergie oder Summe der Wärmezähler (Wohnungszähler plus Warmwasser-Wärmezähler).
- Versorgungsverträge: Kosten für Wasser, Abwasser, Strom und Gas werden aus Zählerverbrauch und Tarif (Preis, Grundpreis, mehrere Tarife mit Stichtag) berechnet statt aus Abschlägen oder verspäteten Rechnungen. Beleg-Markierung „Jahresabrechnung des Versorgers“ ersetzt die Berechnung; ohne Verbrauch oder Tarif gelten weiter die Belege.
- Datenimport: optionale Spalten für Mietmodell (Staffel, Index), Indexbasis, Staffeln, Befristung und Personenzahl; bestehende Verträge lassen sich damit vollständiger übernehmen.
- Rechtsstände: Prüfvermerk (wer, wann, gegen welche Quelle); die CO₂-Stufentabelle gilt mit Vermerk ein Jahr als geprüft.
- Kontoauszug-CSV: Profile für Commerzbank, Deutsche Bank und N26.
- Web-Push für die installierte Web-App (VAPID, leere Push-Nachricht, Inhalte vom eigenen Server), je Gerät abschaltbar; Service Worker mit Offline-Hinweis.
- Digitale Unterschrift für Mietverträge: persönlicher Link je Unterzeichner, Name und optionale gezeichnete Unterschrift, Protokollseite mit Zeitpunkt, IP-Adresse und Prüfsumme; Einschätzung der rechtlichen Grenzen in `docs/digitale-unterschrift.md`.
- Belege per E-Mail: Anhänge aus einem IMAP-Postfach werden als offene Belege (Betrag fehlt) abgelegt; Absenderfreigabe, Größenlimit, optional Belegauslesung.
- Mieterhöhung nach Modernisierung (§§ 559 bis 559c BGB) im Mieterhöhungs-Assistenten: Umlage, Kappungsgrenze, vereinfachtes Verfahren, Fristen, Vorlage mit Härtehinweis.
- Hilfe in der Anwendung (Benutzermenü): Erste Schritte, Aufgaben, häufige Fragen und Glossar für Vermieter, eigene Hilfe für Mieter im Portal; Anwenderhandbuch `docs/handbuch.md`.
- Schreiben: Vorlagen „Mietbescheinigung“, „Mietschuldenfreiheitsbescheinigung“ und „Kautionsabrechnung und -rückzahlung“; das Briefmodell kennt Miete, Nebenkosten, Wohnfläche, Kaution und offene Forderungen.
- Mieterportal: Mieter melden Zählerstände selbst (Seite „Zählerstände“), der Vermieter prüft und übernimmt sie; jährliche Erinnerung ab 15. Dezember.
- Standardvorlagen „Wohnungsgeberbestätigung Einzug/Auszug (§ 19 BMG)“ und „Bestätigung der Kündigung“; Hinweis auf der Übergabeprotokoll-Seite.
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
- Barrierefreiheit: gültige Struktur der Kennzahlen und der Seitenleiste, Namen von Schnellsuche und Benutzermenü enthalten den sichtbaren Text, Links im Fließtext sind unterstrichen.
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
