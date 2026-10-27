# Änderungen

Format nach [Keep a Changelog](https://keepachangelog.com/de/1.1.0/), Versionen nach [SemVer](https://semver.org/lang/de/).

## [Unveröffentlicht]

### Behoben
- Fehlendes „Bearbeiten“ ergänzt: Eigentümer (Anteil, Zeitraum), Bankverbindungen einer Person, Sondertilgungen und Finanzierungskosten eines Kredits, nachträgliche Kosten und bewegliche Wirtschaftsgüter der Abschreibung sowie eigene CSV-Profile der Bank.
- Vertrag: Einträge der Miete (Betrag, Name, Zeitraum) und der Personenanzahl lassen sich bearbeiten oder löschen, nicht nur neu ab Stichtag anlegen; die Monatsmieten werden danach neu berechnet.
- Mehrzimmerverträge nennen alle Zimmer: Vertragsseite, Vertragsliste, Mieterkonto und der Zimmervertrag (PDF, mit Fläche und Gemeinschaftsanteil aller Zimmer); als „getrennt vermietet“ erscheinen nur die übrigen Zimmer.
- Die Vertragsseite eines Zimmervertrags warf beim Öffnen eine Exception (Auswahl „Weiteres Zimmer“ ohne Vorauswahl).

### Geändert
- Bewegung: Seitenwechsel blenden den Inhalt weich über, während Navigation und Kopfleiste stehen bleiben; langsame Wechsel zeigen einen dünnen Fortschrittsbalken, der Ladetext erscheint erst nach 300 ms, aufklappende Bereiche gleiten ein. Die Schrift wird vorgeladen, damit die Seiten beim Laden nicht mehr springen (Layout-Sprung der Belegliste von 0,125 auf 0,001). Bei „reduzierte Bewegung“ entfällt alles.
- Handy und Material 3: Kartenlisten wachsen mit dem Inhalt statt in einem kleinen Fenster zu scrollen, Seitentitel brechen um, die Einheitentabelle des Objekts ist eine Kartenliste, der Info-Button steht in der Titelzeile; Touch-Ziele (Buttons, Sub-Tabs, Aufklapper) sind mindestens 44 px groß. Buttons haben im Ruhezustand keinen Schatten und werden beim Drücken eckiger. Auf dem Desktop stehen die Aktionen im Seitenkopf rechts neben dem Titel, die Einstellungs-Tabs brechen um, Faktenlisten (Abrechnungsassistent, Anlage V, AfA) sind gegliedert. Ein Browser-Test prüft die Bedienelemente auf dem Handy. Textfelder haben 4-px-Ecken, Sub-Tabs sind Chips mit 8 px, und Bottom Sheets auf dem Handy zeigen einen Ziehgriff (Material 3 Expressive).
- Objektseiten: Name, Anschrift und Kennzahlen stehen als Kopf über den Objekt-Tabs und bleiben auf jedem Tab gleich; der Inhalt ist nicht mehr zentriert, sondern fluchtet links mit Ortszeile und Tabs.
- Abrechnungs-PDF (geprüft an der Abrechnung 2025): Anschrift und Mietobjekt nennen alle Zimmer eines Vertrags, Rechenzähler stehen mit ihrem errechneten Verbrauch in den Zählerständen, die Einheiten (€, kWh, %) stehen hinter den Ergebnissen der Heizkostenanlage, der Hinweis auf eine Korrektur folgt dem Einleitungssatz (Closes #135).
- Mehrzimmerverträge: Belegungsprüfung der Abrechnung, Vermietungsstand und Kennzahlen zählen alle Zimmer des Vertrags; ein später gelöschter Vertrag lässt eine freigegebene Abrechnung weiter anzeigen.
- Zahlenfelder verstehen Punkt und Komma als Dezimalzeichen (0.090527 und 0,090527 sind gleich); ein einzelner Punkt vor genau drei Ziffern (1.000) gilt weiter als Tausendertrenner (Closes #139).
- Nebenkosten- und Heizkostenabrechnung: Der gemessene Verbrauch einer WG-Wohnung wird nur noch auf vermietete Zimmer verteilt; ein leeres Zimmer nimmt keinen Verbrauch auf, Flächenanteile (Grundsteuer, Versicherung, Grundkosten) bleiben beim Vermieter.
- Orientierung in der Verwaltung: Eine Ortszeile über den Tabs nennt die Namen (Objekt › Einheit › Zimmer, Vertrag mit Objekt und Mieter, Person) und verlinkt jede Ebene. Einheit, Zimmer, Zähler und Abrechnung behalten die Tabs ihres Objekts, ein Darlehen nennt sein Objekt. „Kassenbuch importieren“ steht im Menü der Objektseite statt im Seitenkopf.
- Navigation gebündelt: sechs Bereiche statt vierzehn (Bestand, Kommunikation, Geld statt je einzelner Seiten), höchstens zwei Tab-Zeilen, neue untere Leiste auf dem Handy (Aufgaben, Bestand, Geld, Kommunikation, Menü). Die Seitenadressen bleiben gleich.

### Hinzugefügt
- Vermieter im Zeitverlauf: Je Objekt lässt sich eintragen, ab welchem Tag welches Vermieterprofil (Briefkopf, Bankverbindung, Absender) auf Dokumenten gilt, z. B. nach einem Eigentümerwechsel (Objekt → Eigentümer). Vorher gilt das am Objekt gewählte Profil.
- Vorlage „Information Vermieterwechsel (§ 566 BGB)“ für den Eigentumsübergang: neuer Vermieter, neue Bankverbindung, übernommene Kaution (§ 566a BGB), mit Absender aus dem Vermieterprofil.
- Versorgungsverträge: Die Vorschau stellt die berechneten Kosten den gezahlten Abschlägen gegenüber und nennt die voraussichtliche Nachzahlung oder das Guthaben beim Versorger (Refs #137; Staffelpreise und getrennte Steuersätze stehen noch aus).
- Kassenbuch-Import: Eine CSV-Datei mit Datum, Gruppe, Empfänger, Betrag und Info wird zu Belegen mit je einer Kostenposition; die Gruppen ordnet man in der Vorschau Kostenarten zu (Vorschlag aus dem Gruppennamen), bereits erfasste Zeilen werden erkannt (Objekt → „Kassenbuch importieren“, Closes #131). Zahlungen der Mieter kommen weiterhin über den Bankimport.
- Belegung: Schematische Haus-Ansicht „Haus heute“ mit Geschossen, Wohnungen und WG-Zimmern, farbig nach vermietet, Mietende absehbar, Rückstand und leer; ein Klick führt zur Einheit (Closes #127).
- Übersicht: Verlaufsgrafik für Soll und Eingang der letzten zwölf Monate mit der Linie des offenen Saldos sowie Balken für die Kosten der Belege je Jahr (Closes #128).
- Vorlage „Änderung des Umlageschlüssels (§ 556a Abs. 2 BGB)“ unter den Standardvorlagen, z. B. für Müll nach Personen; die Erklärung muss dem Mieter vor Beginn des Abrechnungszeitraums in Textform zugehen. Mit dem Personenschlüssel zählt jede Person im Haus (mehrere Zimmer eines Vertrags einmal), ein leeres Zimmer als eine Person zulasten des Vermieters (Closes #132).
- Fristen: Bei Auszug und Einzug erinnert MietHahn an die Zwischenablesung der Zähler der Einheit (Link in die Ablesung mit Einheit und Datum), damit der Verbrauch nicht interpoliert werden muss (Closes #136).
- Umlageschlüssel: Je Kostenart lässt sich einstellen, ob leere WG-Zimmer nicht angesetzt werden (die vermieteten Zimmer tragen den ganzen Anteil der WG) statt beim Vermieter zu bleiben; mit Hinweis auf die rechtliche Folge (Closes #133).
- WG: Der Anteil der Zimmer an der Gemeinschaftsfläche wird aus Wohnfläche minus Zimmerflächen vorgeschlagen (gleichmäßig oder nach Zimmerfläche) und mit einem Klick übernommen (Einheit → Zimmer, Closes #134).
- Gas: Brennwert und Zustandszahl je Abrechnungsjahr in den Heizungseinstellungen (kWh je m³ = Brennwert × Zustandszahl) ersetzen den festen Energieinhalt; ohne Jahreswert gibt die Vorschau einen Hinweis (Closes #130).
- Gas aus dem Versorgungsvertrag: Die CO₂-Menge wird aus kWh × Emissionsfaktor berechnet (Standard Erdgas 0,2016 kg/kWh); mit CO₂-Preis je Tonne für das Jahr werden die im Gaspreis enthaltenen CO₂-Kosten abgetrennt und nach CO2KostAufG aufgeteilt (Closes #129).
- Zähler: Die Messeinheit lässt sich ändern (ohne Umrechnung der Stände), und versehentlich angelegte Zähler lassen sich löschen, solange kein anderer Zähler, Versorgungsvertrag oder die Heizkosteneinstellungen sie verwenden; das Löschen steht im Verlauf (Closes #138).
- Nebenkosten-Vorschau: Balkendiagramm je Kostenart mit Anteil von Mieter und Vermieter und Strich für den Vorjahreswert ; dazu „Zähler und Verbrauch“ mit Verbrauch je Wohnung als Balken und einer Ampel für die Abweichung zwischen Hauptzähler und Zählern der Wohnungen (Closes #126).
- Ein Mietvertrag über mehrere WG-Zimmer: beim Anlegen oder später unter „Zimmer“ im Vertrag weitere Zimmer hinzufügen; eine Miete, eine Kaution, Verbrauchsanteile und Personen werden nur einmal gezählt, ein Zimmer kann nicht doppelt vermietet werden (Closes #125).
- Belegungsplan: Zeitleiste je Wohnung und WG-Zimmer mit Verträgen, Mieterwechseln und Leerstand im Jahresverlauf (Objekt → Belegung, Closes #124).
- Bearbeiten statt nur Löschen: Mietzeit (Beginn, Befristung), noch nicht übernommene Staffeln, manuelle Zahlungen und Forderungen, Kautionsbuchungen, Belegpositionen, Zählerstände sowie Versorgungsverträge und Tarife lassen sich im Dialog „Bearbeiten“ korrigieren (Closes #116).
- Zählerabweichung: Die Vorschau prüft beim Versorgungsvertrag, ob der Hauptzähler und die Summe der Zähler der Wohnungen zusammenpassen (Hinweis ab 10 % Differenz). Im Vertrag lässt sich wählen, ob die Differenz auf die Wohnungen nach Verbrauch verteilt wird oder der Vermieter ihren Anteil trägt (Closes #114).
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
