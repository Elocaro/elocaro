# Änderungen

Format nach [Keep a Changelog](https://keepachangelog.com/de/1.1.0/), Versionen nach [SemVer](https://semver.org/lang/de/).

## [Unveröffentlicht]

### Hinzugefügt
- MCP-Schnittstelle (`/mcp`, nur lesend, standardmäßig aus mit `Mcp__Enabled=true`): KI-Assistenten wie Claude, GitHub Copilot und Cursor können Objekte, Verträge, Mieterkonten, offene Posten, Belege, Umsätze, Zähler, Dokumente, Aufgaben und Fristen abfragen. Anmeldung über neue persönliche **Zugriffstoken** (*Konto & Sicherheit → Zugriffstoken*): nur der Hash wird gespeichert, mit Ablauf, widerrufbar, auf die Bereiche *Stammdaten* und *Finanzen* beschränkbar. Ein Token gibt nie mehr her als das Konto selbst: die Rechte werden bei jeder Anfrage neu bestimmt, Sperren, Rollenwechsel und entzogene Rechte gelten sofort; nur Administrator und Verwalter mit eingerichteter Zwei-Faktor-Anmeldung. Jeder Aufruf steht im Audit-Protokoll. Anleitungen für Claude, VS Code/Copilot, Cursor und ChatGPT in `docs/mcp.md`.
- Verbrauchsinformation (Mieterportal und Mail): Der Heizverbrauch wird zusätzlich witterungsbereinigt mit dem Vorjahresmonat verglichen (Verbrauch je Gradtag der gewählten Wetterstation). So lässt sich prüfen, ob der Verbrauch unabhängig vom Wetter gestiegen ist. Nur zur Anzeige. Im Mieterportal zeigt ein Säulendiagramm je Zählerart den Verbrauch der letzten Monate gegen den Vorjahresmonat, bei der Heizung mit der witterungsbereinigten Änderung als Pfeil.
- Heizkosten beim Mieterwechsel ohne Zwischenablesung: Statt der Platzhaltertabelle verteilt Elocaro den Heizverbrauch nach den Monatsgradtagen (VDI 3807) einer Wetterstation des Deutschen Wetterdienstes. Die Werte werden automatisch abgerufen (`DegreeDays__Enabled`), die Station wählt man je Objekt in den Heizungseinstellungen. Ohne Station gilt weiter der Platzhalter.

### Geändert
- Konto-Seiten überarbeitet: Übersicht mit Profil und Navigationszeilen, Zurück-Link oben, Logo führt zur App. Zugriffstoken werden in einem Dialog erstellt; das neue Token lässt sich mit einer Schaltfläche kopieren, die Liste zeigt Karten statt einer Tabelle.

### Sicherheit
- Die Zwei-Faktor-Pflicht galt nicht für Adressen mit Dateiendung: Ein Konto ohne eingerichtete Zwei-Faktor-Anmeldung konnte CSV-, DATEV-, Anlage-V- und PDF-Exporte abrufen. Ausgenommen sind jetzt nur noch echte statische Dateien.
- Benutzerverwaltung: Wer kein Administrator ist, kann sich und anderen nicht mehr die Rolle Administrator geben, niemanden als Administrator einladen, Administratorkonten weder ändern noch sperren oder entsperren, die eigene Rolle und die eigenen Rechte nicht ändern und keine Rechte vergeben, die er selbst nicht hat. Das prüft der Server, die Oberfläche bietet es nicht mehr an.
- Änderungen an Rolle oder Rechten melden das betroffene Konto ab, auch in offenen Seiten. Die neuen Rechte gelten ab der nächsten Anmeldung. Bisher galten entzogene Rollen in offenen Seiten weiter.
- Bilder über 50 Megapixel (`FileStore__MaxImageMegapixels`) werden beim Hochladen abgelehnt. Eine kleine Datei mit riesigen Bildmaßen konnte sonst den Speicher des Servers füllen. Dazu Speicherlimits für `app` und `gotenberg` in `deploy/docker-compose.yml` (`APP_MEM_LIMIT`, `GOTENBERG_MEM_LIMIT`).
- Ersteinrichtung nur noch mit Einrichtungscode: Bisher wurde Administrator, wer eine neue Installation zuerst aufrief. `deploy/install.sh` erzeugt den Code (`ELOCARO_SETUP_CODE` in `deploy/.env`) und zeigt ihn an; ist keiner eingetragen, schreibt die App beim Start einen ins Protokoll, solange es kein Konto gibt. Bestehende Installationen mit Konten sind nicht betroffen. Gleichzeitige Anfragen legen nicht mehr mehrere Administratoren an.
- Ausgehende Mails: Der Text von Einladungen, Anmelde-, Passwort- und Signatur-Links wird nach der Zustellung oder dem endgültigen Fehlschlag entfernt, auch rückwirkend; Empfänger, Betreff, Zeitpunkt und Status bleiben als Nachweis. Solche Mails lassen sich nicht erneut senden, Einladung oder Link werden neu ausgelöst.
- Der Vollexport enthält den Text ausgehender Mails nicht mehr, denn darin standen gültige Anmeldelinks. Der Versandnachweis bleibt im Export.
- Ausgehende Mails werden nach 3 Jahren gelöscht (`Mail__RetentionDays`), solche zu Abrechnungen und Mahnungen nur geleert. Das Anonymisieren einer Person bereinigt auch ihre Mails, die Auskunft nennt sie.
- Container: `no-new-privileges` und `cap_drop: ALL` für alle Dienste von `deploy/docker-compose.yml`; nur Postgres, Backup und Caddy behalten die Capabilities, die sie brauchen.
- `/health` nennt öffentlich nur noch den Gesamtstatus. Den Zustand einzelner Prüfungen (u. a. Datensicherung) sehen nur noch angemeldete Konten mit dem Recht System.
- Antiforgery-, Zwei-Faktor- und Status-Cookie sind jetzt `Secure`.
- Neue Aktion *Konto & Sicherheit → Auf allen Geräten abmelden*: beendet alle Sitzungen des Kontos und macht „Dieses Gerät merken“ ungültig.
- Das eigene Konto lässt sich auch serverseitig nicht mehr sperren, bisher verhinderte das nur die Oberfläche.
- Offene Seiten prüfen die Sitzung jede Minute statt alle 5 Minuten.
- Optional lassen sich neue Data-Protection-Schlüssel mit einem Zertifikat außerhalb der Datenbank verschlüsseln (`DataProtection__CertificatePath`, siehe `docs/betrieb.md`). Das Zertifikat muss getrennt gesichert werden.
- GitHub Actions sind auf Commit-Hashes festgelegt.
- Anonymisierte Mails erscheinen im Dashboard nicht mehr als „nicht zustellbar“.

### Geändert
- Paket-Updates: Quartz.NET 4.7 (neue Migration `UpgradeQuartzSchemaTo47` ergänzt die Spalte `fixed_delay`, ohne sie startet der Planer nicht), MudBlazor 9.12, Scriban 7.6, SkiaSharp 4.155; Test-Pakete bunit 2.14 und xunit.v3 4.0.3.
- Statische Analyse: Meziantou.Analyzer und Banned-API-Prüfung (`DateTime.Now` u. a. verboten, Zeit über `TimeProvider`), NuGetAudit für alle Pakete und ein CI-Schritt für anfällige Pakete. Details in `docs/entwicklung.md`.
- Einstellungen → Module: Die Auswahl innerhalb der Bereiche (z. B. Staffelmiete, DATEV-Export) steht jetzt direkt in der Kachel des Moduls statt auf einem zweiten Reiter.
- Heizkosten-Einstellungen für Laien: Oben steht eine Zusammenfassung („Aktuell gilt …“), die Fragen sind in einfacher Sprache gestellt (Energieträger, „Erzeugt dieselbe Anlage auch das Warmwasser?“, Anteil nach Verbrauch mit Schnellwahl 70/60/50 %), Selteneres (Energieinhalt, Emissionsfaktor, Wetterstation) steht aufklappbar darunter, und eine kurze Erklärung nennt, was für die Heizkosten gebraucht wird.
- Heizkosten: Die Wetterstation für die Gradtage wird nach der Postleitzahl des Objekts vorgeschlagen (nächste Station vorausgewählt, Liste nach Entfernung sortiert, mit Kilometerangabe). Die Positionen der Postleitzahlen liegen als Tabelle bei (GeoNames, CC BY 4.0), es wird nichts abgerufen.
- Einrichtungsassistent: Die Frage nach den Standardvorlagen entfällt, sie werden ohnehin beim Start angelegt.
- Darlehen, Wartung, Hausmeister-Aufgaben und CSV-Profile: „anlegen“ ist eine Schaltfläche im Seitenkopf mit Dialog; das Formular unter der Liste entfällt. Ebenso „Stand erfassen“ auf der Zählerseite (Dialog `ReadingEntryDialog` statt Formular auf der Seite, mit Foto und Bestätigung bei Auffälligkeiten).
- Umlageschlüssel (Festlegen und Beenden je Zeile), AfA-Anschaffung (Button im Seitenkopf, Zusammenfassung statt Formular) und Zählerliste („Stand eintragen“ je Zähler, ohne Ablese-Runde) arbeiten mit Dialogen.
- Vorlagen: Die Standardvorlagen werden beim Start automatisch angelegt (was fehlt, auch neue nach einem Update; bewusst Gelöschtes bleibt gelöscht). Die Schaltfläche „Standardvorlagen anlegen“ in der Vorlagenliste entfällt.
- Beispieldaten: Die Mietverträge beginnen am 1. Januar des vorletzten Jahres. Die Demo-Instanz zeigt im Mieterportal versendete Abrechnungen mit Rechenweg, Vorjahresvergleich und freigegebenen Beispielbelegen.

### Behoben
- Dashboard: Gemeldete Zählerstände von Mietern erschienen nicht als Hinweis, weil die Quelle (`ReadingReportTodos`) nie registriert war. Ein Architekturtest prüft jetzt, dass jede `ITodoSource` registriert ist.
- Abrechnung im Mieterportal auf dem Handy: Der Rechenweg steht als linksbündiger Block über die volle Breite der Karte, statt in schmale Spalten gequetscht zu werden. Die Summenzeile des Vorjahresvergleichs bricht um und lässt die Seite nicht mehr seitlich überstehen.
- Datenimport: Eine leerstehende Wohnung bekommt ihre Fläche ab dem frühesten Mietbeginn des Hauses. Bisher ließ sich eine Abrechnung für frühere Jahre wegen der fehlenden Fläche nicht freigeben.
- Handy: Die Reiter auf Detailseiten (z. B. Vertrag) springen beim Wischen nicht mehr auf den gewählten Reiter zurück.
- Belegung auf dem Handy: Beim Wechsel des Jahres springt die Seite nicht mehr nach oben, sondern bleibt an der Jahresleiste.
- Dokumentenliste auf dem Handy: Schlagworte brachen den Titel buchstabenweise um; Titel und Schlagworte stehen jetzt in einer Zelle und umbrechen sauber.
- Interaktive Seiten reagieren erst, wenn die Verbindung steht: Bis dahin nehmen Auswahlfelder, Schalter und Knöpfe keine Eingaben an und werden nach kurzer Zeit abgedunkelt, statt Tipps zu verschlucken (langsame Mobilfunkverbindung).
- `manifest.webmanifest`, Symbole und App-Icon wurden auf Unterseiten nicht gefunden (404, „manifest is not valid JSON“), weil `<base>` nach den Links stand.

### Hinzugefügt
- Mieterportal: Die Abrechnungsseite zeigt das Ergebnis, die Anteile je Kostenart und den Vergleich mit dem Vorjahr zusätzlich als Balkendiagramme. Eine Nachzahlung oder ein Guthaben steht mit Betrag im Text; die Tabellen bleiben.
- Mieterportal: Die versendete Betriebskostenabrechnung hat eine eigene Seite (Portal → Abrechnungen) mit dem Rechenweg je Position in Klartext, dem Vergleich mit der Abrechnung des Vorjahres (Beträge und Unterschied je Kostenart), den vom Vermieter freigegebenen Belegen und „Frage zu dieser Position“ (neue Nachricht mit vorbelegtem Betreff, mit Hinweis, dass eine Rückfrage keinen förmlichen Einwand ersetzt). Mieter sehen nur die Abrechnung ihres eigenen Vertrags und nur versendete Fassungen, nie Werte anderer Mieter.
- Belege für Mieter freigeben: Auf der Abrechnung gibt es den Reiter „Belege für Mieter“. Standard ist, dass nichts freigegeben ist; der Hinweis auf Daten Dritter steht dabei, Freigaben sind jederzeit zurücknehmbar. Der Abruf im Portal wird bei jedem Aufruf geprüft und im Zugriffsprotokoll festgehalten.
- Abrechnungsschreiben: QR-Code und Link zum Mieterportal, wenn die öffentliche Adresse (`Application__BaseUrl`) eingestellt ist und ein Mieter des Vertrags ein Portalkonto hat. Der Code enthält nur den Portal-Link, keinen Zugangsschlüssel.
- Abrechnungs-Assistent: Prüfhinweise zu typischen Einwänden (Kostenpositionen ohne Belegdatei, als nicht umlagefähig markierte Positionen). Sie sperren die Freigabe nicht.
- Notfallmappe (Einstellungen → System → Notfallmappe): eine verschlüsselte HTML-Datei, mit der Angehörige die Vermietung weiterführen können, auch wenn Elocaro oder der Server nicht erreichbar sind. Sie enthält eine Checkliste für die ersten 30 Tage, Ihre eigenen Hinweise, Objekte, laufende Mietverträge mit Kontaktdaten, Miete und Kaution, offene Posten, Fristen, Konten (ohne Zugangsdaten), Dienstleister und Versicherungen sowie Hinweise zur Wiederherstellung; auf Wunsch liegen die unterschriebenen Mietverträge bei. AES-256-GCM mit PBKDF2-SHA256 (600.000 Iterationen), entschlüsselt wird offline im Browser. Das Passwort wird nirgends gespeichert. Die Startseite erinnert an die Erneuerung nach zwölf Monaten.
- Lizenz geklärt: AGPL-3.0-or-later mit Urheberrechtsvermerk (README, „Über Elocaro“, Projektdateien) und Beitragsvereinbarung (`CLA.md`, Pull-Request-Vorlage, Hinweis in `CONTRIBUTING.md`) mit automatischer Prüfung als Action `CLA`.
- Abrechnungs-Assistent warnt, wenn Belege des Zeitraums noch nicht vollständig auf Kostenarten und Objekte aufgeteilt sind (mit offener Summe); sie fehlen sonst in der Abrechnung.
- Die Offline-Seite der installierten Web-App versucht es selbst erneut und öffnet Elocaro, sobald der Server wieder antwortet.
- „Jetzt aufteilen“ auf der Belegseite trägt den offenen Rest als Betrag vor.
- Beleg erfassen überarbeitet: Die Seite hat zwei Schritte (Rechnung, Zuordnung). Sichtbar sind nur Lieferant, Rechnungsnummer, Datum und Brutto; Leistungszeitraum, Netto, Fälligkeit und Notiz stehen unter „Weitere Angaben“. Die Zuordnung zu Kostenart und Objekt gehört gleich dazu, auch auf mehrere Kostenarten oder Objekte aufgeteilt; eine Statuszeile zeigt live, was noch zu verteilen ist. Speichern ohne vollständige Verteilung fragt nach.
- Brennstoff (nur Heizkosten) und „Art der Rechnung“ (Abschlag oder Jahresabrechnung des Versorgers, nur bei Wasser, Abwasser, Heizung und Allgemeinstrom) erscheinen nur noch, wenn die gewählte Kostenart es verlangt. Der Haken „Jahresabrechnung“ ist eine Auswahl mit Erklärung.
- Belegseite: Oben steht „Noch … nicht aufgeteilt“ mit dem Knopf „Jetzt aufteilen“. Die Aufgabe auf der Startseite („Belege ohne vollständige Kostenaufteilung“) ist jetzt „bald fällig“, nennt die Summe und führt bei einem Beleg direkt dorthin.
- Neue Installationsanleitung `docs/installation.md` (und Unterseite `installation.html` auf der Website): Wahl des Weges, Probelauf, Server mit Skript, von Hand, NAS und Plattformen, Checkliste nach der Installation, Fehlersuche.
- Abmelden im Benutzermenü ist abgesetzt und fragt „Wirklich abmelden?“.
- Die Mietzeit eines Vertrags wird in einem Dialog korrigiert („Mietzeit korrigieren“ neben der Laufzeit).
- Abrechnungs-Erinnerung: Im Abrechnungs-Assistenten lässt sich die Erinnerung für ein Objekt und Jahr dauerhaft ausblenden (und wieder einblenden), zum Beispiel wenn die Frist verstrichen ist und keine Abrechnung mehr entsteht. Die Abrechnung bleibt weiter möglich.
- Handy: Die Kopfleiste mit dem Namen Elocaro erscheint auch im Verwaltungsbereich. Verliert die Seite die Verbindung, versucht sie es im Hintergrund erneut, statt auf „Erneut versuchen“ zu warten.
- Demo-Modus (`Demo__Enabled`): legt beim ersten Start einen Demo-Benutzer ohne Zwei-Faktor an, lädt die Beispieldaten legt je Rolle einen Demo-Benutzer an (Verwalter, Hausmeister, Handwerker, Eigentümer, Mieter), sperrt Passwortänderung und Zwei-Faktor-Einrichtung, blendet die Sicherungsmeldung aus und zeigt die Zugangsdaten auf der Anmeldeseite; dazu `deploy/docker-compose.demo.yml`, `deploy/Caddyfile.demo` und `deploy/demo/reset.sh` für das nächtliche Zurücksetzen (siehe Betrieb → Demo-Instanz).

## [0.1.0] - 2026-10-29

Erste veröffentlichte Version.

### Geändert
- **Marke:** Neue Farbwelt in Waldgrün (Erfolg als gelbliches Grün, damit Status und Marke unterscheidbar bleiben) und neues Logo (Dach über E); eigene Symbole für maskierbare und monochrome App-Icons.
- **Umbenennung:** Das Produkt heißt jetzt **Elocaro** (bisher MietHahn): Projekte, Namensräume, Images, Umgebungsvariablen (`ELOCARO_*`), Datenbank, Oberfläche, Mails, PDF und Doku. Neues Logo und neue Icons (PNG für Installation und Apple-Touch). Bestehende Installationen ziehen per Sicherung und Wiederherstellung um: Alle Sitzungen enden, Push-Benachrichtigungen werden neu eingeschaltet, eine FinTS-Kennung ist neu einzugeben. Wiederherstellung mit nicht mehr lesbaren Push-Schlüsseln erzeugt jetzt neue Schlüssel statt abzubrechen.
- Modulzyklen Documents↔Finance und Documents↔Leases aufgelöst: Serienbriefe und Dokumentbereitschaft liegen im eigenen Modul `Correspondence`, die Dokumentenbibliothek fragt Vertragsbezüge über `IDocumentLeaseSource` ab, die Leases implementiert; es bleibt ein Zyklus (Leases↔Properties).
- Modulzyklus Leases↔Metering aufgelöst: Das Übergabeprotokoll (`Handovers`) verbindet Vertrag und Zähler und liegt deshalb in einem eigenen Modul `Handover`, das niemand sonst benutzt; die Baseline sinkt auf 3 Zyklen.
- Modulzyklus OperatingCosts↔Receipts aufgelöst: Die Kostengruppe gehört zur Kostenart (`Domain.Receipts`); die Baseline sinkt auf 4 Zyklen.
- Modulzyklen Bank↔Receipts und Bank↔Tax aufgelöst: CSV-Zerlegung und Zeichensatz-Erkennung der Importe liegen als Bausteine in Common; die Baseline sinkt auf 5 Zyklen.
- Modulzyklen Finance↔Properties und Leases↔Receipts aufgelöst: Die Beschreibung eines Mietverhältnisses (`LeaseDescriptions`) und der String-Helfer `Truncate` liegen jetzt bei Leases bzw. Common; die Baseline sinkt auf 7 Zyklen.
- Architekturtests für Zugriff und Module: Jede Seite nennt ihre Berechtigung ausdrücklich, anonyme Seiten stehen auf einer Liste, jeder Bereich verlangt seine Policy, Web nutzt Infrastructure nur in Start und Konto, und neue Modulzyklen in der Infrastructure werden abgelehnt (bestehende sind festgehalten). Startseite und „Konto & Sicherheit“ tragen jetzt ausdrücklich `[Authorize]`.
- „Bearbeiten“ bei Einheit, Zimmer und Bankkonto öffnet ebenfalls einen Dialog; der Tab „Angaben“ entfällt, „Einheit löschen“ und „Zimmer löschen“ stehen im Menü „Weitere Aktionen“.
- Seltsam abgerundeter linker Rand an Listeneinträgen (z. B. Vertragsparteien): Die farbige Kante gibt es nur noch bei Dringlichkeit und Notfall und folgt der Rundung der Karte; neutrale Einträge haben keine mehr.
- Lange Erklärabsätze (rund 40 Stellen, z. B. Kaution, Heizkosten, Anlage V, Importe) stehen jetzt in einklappbaren Abschnitten mit sprechender Überschrift statt als Textblock.
- Meldung bei verlorener Verbindung (vorher Blazors englischer Standardtext „Rejoining the server …“): deutscher Dialog im App-Stil mit „Erneut versuchen“ und „Seite neu laden“ (`reconnect.js`).
- Kalender der Datumsfelder, Auswahllisten und Menüs nutzen die Flächenfarbe der App statt reinem Weiß; der Kalenderkopf ist nicht mehr als Farbblock gestaltet.
- Handy: Die Sortierauswahl, die MudBlazor über Kartenlisten einblendet, entfällt; Listen sind immer nach ihrer ersten Spalte (z. B. Objekt) sortiert.
- Seitentitel, der den gewählten Tab wiederholt (z. B. „Objekte“ unter dem Tab „Objekte“, auch „Mietverträge“ unter „Verträge“ oder „Zuordnungsregeln“ unter „Regeln“), steht nur noch für Screenreader im Dokument und wird nicht doppelt angezeigt.
- „Bearbeiten“ bei Objekt und Person öffnet einen Dialog mit den Angaben, statt in einen anderen Tab zu springen; der Tab „Angaben“ entfällt (beim Objekt heißt der Rest „Foto“).
- Objekt-Kopf auf dem Handy: Wiederholt der Titel den Tab, entfallen Zurück-Pfeil und leere Titelzeile; die Aktionen stehen in einer schmalen, nicht festklebenden Zeile, der Abstand unter der Objekt-Kachel ist kleiner (rund 120 px mehr Platz für Inhalt).
- Vertragsliste: „Beendete anzeigen“ ist ein Filter-Chip (Material 3) statt eines Schalters.
- Startseite und Listen: Dringlichkeit zeigt ein farbiger Punkt vor dem Eintrag statt eines Randes mit Rundung; Notfallhinweis und interne Kommentare ohne Randstreifen.
- Verbindungsdialog überarbeitet (Expressive): geformtes Symbol, weicher Auftritt, volle Breite für „Erneut versuchen“.
- Handy: Die Tab-Leisten der Detailseiten (z. B. Mietvertrag) lassen sich wischen; vorher ging es nur über die Pfeiltasten. Der aktive Tab bleibt sichtbar.
- Tabellen: `MudTable` hat jetzt dieselbe Schriftgröße, denselben Zellabstand und dieselbe Kopfzeile wie die statischen Tabellen (vorher kleinere Schrift).
- Tests: Testlauf von rund 8,5 auf 2,5 Minuten verkürzt (Vorlagen-Datenbank statt Migrationen je Test, ein gemeinsames Chromium, die langen Seiten-Durchläufe auf sechs parallele Klassen verteilt, 10 statt 4 parallele Threads).
- Nebenkosten öffnen auf den Abrechnungen (dort startet „Neue Abrechnung“); der eigene Tab „Abrechnungs-Assistent“ entfällt. Versorgungsverträge, Umlageschlüssel und Heizkosten stehen als Einstellungen hinter einem Strich rechts.
- Hinzufügen- und Ändern-Formulare (Betrag ab Stichtag, Bestandteil beenden, Mieter, Personenanzahl, Staffel, Mietmodell, Zähler anlegen/tauschen/Angaben, AfA-Kosten und Wirtschaftsgüter) öffnen als Dialog statt als aufklappbarer Abschnitt (`FormDialog`).
- Weitere Hinzufügen-Formulare als Dialog: Sondertilgung, Finanzierungskosten und Jahreszinsbescheinigung (Darlehen), Kautionsbuchung, Zahlung, Sonstige Forderung und Mahnsperre (Mieterkonto), Wartung als erledigt eintragen, Indexwert und CSV-Import (VPI), Eigentümer, Unterart (Umlageschlüssel), Beleg-Position.
- Bank, Umsatz zuordnen: Die Teilbeträge stehen als Tabelle (Betrag, Zuordnung, Notiz) mit „Teil hinzufügen“, Bearbeiten und Entfernen je Zeile; die Felder eines Teils öffnen als Dialog statt als Stapel von Feldgruppen. „Zuordnen“ erscheint, sobald ein Teil vorhanden ist.
- DATEV: Sachkontenlänge und Beginn des Wirtschaftsjahrs sind eine Auswahl (Monatsname statt Zahl); Zeilen-Schaltflächen ohne eindeutigen Namen (Aufheben, Verwerfen, Neuer Link, Unterschrift leeren, Zimmer entfernen) haben ein `aria-label`.
- Jahresangaben (Abrechnungs-, Steuerjahr, Belegfilter, Bescheinigung) sind eine Auswahl (`AppYearSelect`) statt eines Zahlenfelds.
- Vergleichswohnungen (Mieterhöhung), Schlüsselarten (Übergabe) und Wohnungen im Einrichtungsassistenten stehen als Tabelle mit Bearbeiten/Entfernen; „… hinzufügen“ öffnet einen Dialog.
- Heizkosten: Brennwert und Zustandszahl je Jahr stehen als Tabelle mit Bearbeiten/Entfernen je Zeile; „Jahr hinzufügen“ öffnet einen Dialog statt loser Eingabefelder.

### Behoben
- Navigation: Eine Gruppe mit nur einer sichtbaren Seite (z. B. Verträge, solange der Verbraucherpreisindex ausgeschaltet ist) zeigt keine zweite Tab-Zeile mit einem einzelnen Tab mehr.
- Tabellen und Buttons blitzten beim Seitenwechsel kurz hell auf, bevor MudBlazor seine dunkle Palette setzte (beim Vorrendern lädt MudBlazor die helle Palette, solange nicht „Dunkel“ gewählt ist). Die Palette kommt jetzt aus den App-Tokens in `app.css` (`:root:root`, gespiegelt zu `M3Theme`) und gilt von der ersten Darstellung an.
- Versorgungsvertrag: Beim Anlegen wurde der Umfang der Wohnungszähler „Nur gleiche Art“ nicht gespeichert, die Datenbank setzte stattdessen „Mit Warmwasser“ (fehlender Sentinel-Wert in der Modellkonfiguration; EF-Warnung beim Start). Die Abfrage-Aufteilung von EF Core ist jetzt ausdrücklich eingestellt (keine Warnung mehr).
- Bausteine in Vorlagen: Der Inhalt eines mit `{{ include 'Name' }}` eingefügten Bausteins wurde HTML-maskiert, im Brief erschienen die Tags (`<p>`, `<br>`) als Text. Der Baustein-Text gilt jetzt als Teil der Vorlage, Werte darin (`{{ tenant_name }}`) bleiben maskiert; Bausteine, die im Kreis aufeinander verweisen, melden einen Fehler statt zu hängen.
- Docker-Image: Das Blazor-Skript des Frameworks (`/_framework/blazor.web.js`) fehlte im veröffentlichten Image (404), weil die Paketwiederherstellung vor dem Kopieren der Quelltexte lief; dadurch funktionierte keine interaktive Seite (z. B. der Einrichtungsassistent: Optionen und „Weiter“ blieben ohne Wirkung, der Browser meldete `ERR_HTTP_RESPONSE_CODE_FAILURE`).
- Info-Dialoge: Links, die die ID der Seite brauchen (z. B. beim Objekt zur Abrechnung), zeigten auf `…/objekte/pageId/…` und liefen ins Leere; Links auf Seiten, die in dieser Installation ausgeschaltet sind (z. B. Verbraucherpreisindex ohne Indexmiete), erscheinen nicht mehr.
- Nebenkosten-Vorschau: Die Kostenarten „heating-heat“ und „heating-hotwater“ stehen jetzt als „Heizkosten (Heizung)“ und „Warmwasserkosten (Warmwasser)“ im Anteil des Vermieters.
- Fehlendes „Bearbeiten“ ergänzt: Eigentümer (Anteil, Zeitraum), Bankverbindungen einer Person, Sondertilgungen und Finanzierungskosten eines Kredits, nachträgliche Kosten und bewegliche Wirtschaftsgüter der Abschreibung sowie eigene CSV-Profile der Bank.
- Vertrag: Einträge der Miete (Betrag, Name, Zeitraum) und der Personenanzahl lassen sich bearbeiten oder löschen, nicht nur neu ab Stichtag anlegen; die Monatsmieten werden danach neu berechnet.
- Mehrzimmerverträge nennen alle Zimmer: Vertragsseite, Vertragsliste, Mieterkonto und der Zimmervertrag (PDF, mit Fläche und Gemeinschaftsanteil aller Zimmer); als „getrennt vermietet“ erscheinen nur die übrigen Zimmer.
- Die Vertragsseite eines Zimmervertrags warf beim Öffnen eine Exception (Auswahl „Weiteres Zimmer“ ohne Vorauswahl).

### Geändert
- Startseite: Gleichartige Erinnerungen („Eichfrist abgelaufen: …“, gleiches Datum und gleicher Text) ab drei Stück als ein aufklappbarer Eintrag mit allen Links statt zwanzig fast gleicher Karten. Einstellungen: die vier Systemseiten (E-Mail-Versand, Hintergrundjobs, Datensicherung, Belegauslesung) stehen in einer Gruppe „System“, die Tabs passen in eine Zeile. Handy: der Aktionsknopf ist kompakter und weicht beim Hinunterscrollen aus; Tab-Leisten, die seitlich scrollen, blenden am Rand aus, wo weitere Tabs warten.
- Vorlagen und Schreiben bedienbarer: Der Vorlagen-Editor zeigt die Platzhalter, Hilfsfunktionen und Bausteine rechts neben dem Text; ein Klick fügt sie an der Cursorposition ein. Eine fehlende Standardvorlage ist eine Warnung mit einem Knopf statt eines roten Fehlers. „Schreiben an Mieter“ wählt bei nur einem Objekt dieses vor und beschreibt die gewählte Vorlage; das Übergabeprotokoll ist in Tabs gegliedert (Zustand, Schlüssel und Zähler, Bemerkungen); Formularfelder in einem `fieldset` ragen auf dem Handy nicht mehr über den Rand. Das Mieterkonto zeigt auf dem Handy je Buchung zwei kompakte Zeilen (Datum, Status, Text, Betrag, Saldo) statt einer Zeile je Spalte; die Seite ist dadurch etwa 60 % kürzer. Das Mahnungsdetail klappt die offenen Posten ab sechs Einträgen zu und nennt Anzahl und Summe in der Überschrift; die Vorlagenliste und die Jahrestabelle der Darlehen sind auf dem Handy deutlich kompakter.
- Objekt und Nebenkosten übersichtlicher: Der Objekt-Kopf zeigt nur Kennzahlen mit Wert; die Unter-Tabs der Nebenkosten trennen Einrichtung (Versorgungsverträge, Umlageschlüssel, Heizkosten) von der Durchführung (Vorschau, Abrechnungs-Assistent, Abrechnungen); der Umlageschlüssel hat Tabs (Schlüssel, Wohngemeinschaften, Unterarten), eine Aktion „Festlegen“/„Ändern“ je Kostenart und eine einklappbare Erklärung; die Vorschau nennt zuerst ihr Ergebnis, klappt die Grundlage und viele Hinweise ein, gliedert sich in die Tabs Kosten, Mieter und Verbrauch/Heizung (bei einem Haus mit vier Wohnungen von rund 13.600 auf 2.300 px) und zeigt je Mieter einen einklappbaren Block mit Saldo in der Überschrift; die Heizkosten sind in Tabs gegliedert (Verteilung, Brennwert je Jahr/Heizöl, CO₂-Kosten); der Erklärtext der Versorgungsverträge ist einklappbar. Die freigegebene Abrechnung hat die Tabs Versand, Kosten und Mieter, die Modulseite zwei Tabs mit Karten nebeneinander, die Fristen sind in „Überfällig“ und „Anstehend“ gegliedert (ein gleicher Hinweis steht einmal statt in jeder Zeile). Die Zählerseite zeigt die letzten zwölf Stände und klappt ältere ein. Auf dem Handy werden die Rechentabellen der Vorschau zu Karten, sodass Summen und Nachzahlung sichtbar bleiben.
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
- Vorlagen-Editor: Syntaxfarben für HTML und Platzhalter (`{{ … }}`, Schlüsselwörter, Kommentare), Werkzeugleiste (Fett, Kursiv, Unterstrichen, Absatz, Überschriften, Listen, Tabelle, Trennlinie, Seitenumbruch, Wenn/Wenn-Sonst/Je Eintrag, Baustein einfügen, Rückgängig/Wiederholen), Kürzel (Strg+B/I/U/S) und eine Live-Vorschau als Briefbogen mit Briefkopf, die sich beim Schreiben aktualisiert (ohne PDF-Dienst) und Fehler der Vorlage sofort zeigt; Größe der Vorschau einstellbar. Platzhalter und Hilfsfunktionen stehen in einem eigenen Tab.
- Vermieter im Zeitverlauf: Je Objekt lässt sich eintragen, ab welchem Tag welches Vermieterprofil (Briefkopf, Bankverbindung, Absender) auf Dokumenten gilt, z. B. nach einem Eigentümerwechsel (Objekt → Eigentümer). Vorher gilt das am Objekt gewählte Profil.
- Vorlage „Information Vermieterwechsel (§ 566 BGB)“ für den Eigentumsübergang: neuer Vermieter, neue Bankverbindung, übernommene Kaution (§ 566a BGB), mit Absender aus dem Vermieterprofil.
- Versorgungsverträge: Die Vorschau stellt die berechneten Kosten den gezahlten Abschlägen gegenüber und nennt die voraussichtliche Nachzahlung oder das Guthaben beim Versorger (Refs #137; Staffelpreise und getrennte Steuersätze stehen noch aus).
- Kassenbuch-Import: Eine CSV-Datei mit Datum, Gruppe, Empfänger, Betrag und Info wird zu Belegen mit je einer Kostenposition; die Gruppen ordnet man in der Vorschau Kostenarten zu (Vorschlag aus dem Gruppennamen), bereits erfasste Zeilen werden erkannt (Objekt → „Kassenbuch importieren“, Closes #131). Zahlungen der Mieter kommen weiterhin über den Bankimport.
- Belegung: Schematische Haus-Ansicht „Haus heute“ mit Geschossen, Wohnungen und WG-Zimmern, farbig nach vermietet, Mietende absehbar, Rückstand und leer; ein Klick führt zur Einheit (Closes #127).
- Übersicht: Verlaufsgrafik für Soll und Eingang der letzten zwölf Monate mit der Linie des offenen Saldos sowie Balken für die Kosten der Belege je Jahr (Closes #128).
- Vorlage „Änderung des Umlageschlüssels (§ 556a Abs. 2 BGB)“ unter den Standardvorlagen, z. B. für Müll nach Personen; die Erklärung muss dem Mieter vor Beginn des Abrechnungszeitraums in Textform zugehen. Mit dem Personenschlüssel zählt jede Person im Haus (mehrere Zimmer eines Vertrags einmal), ein leeres Zimmer als eine Person zulasten des Vermieters (Closes #132).
- Fristen: Bei Auszug und Einzug erinnert Elocaro an die Zwischenablesung der Zähler der Einheit (Link in die Ablesung mit Einheit und Datum), damit der Verbrauch nicht interpoliert werden muss (Closes #136).
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
- Lizenz AGPL-3.0, Seite „Über Elocaro“ (`/ueber`) mit Version, Lizenz und Quellcode-Link.
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
