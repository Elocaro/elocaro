# Produkt-, UX- und Vertriebsbewertung (Stand 24.10.2026, Commit 59527c2)

Ergänzt die [Marktanalyse](marktanalyse.md). Sicht des UX-Experten und des Produktmanagers. Ziel: herausfinden, was fehlt, damit MietHahn in irgendeiner Form vertrieben werden kann.

## 0. Methodik und Grenzen

| Quelle | Umfang |
|---|---|
| **Eigener Usability-Test** | Frische Installation (Build aus Commit 59527c2, PostgreSQL/Mailpit/Gotenberg per `docker-compose.dev.yml`), bedient per Playwright-Chromium als „neuer Vermieter“ (Desktop 1440 px und Handy 390 px). Ein Prüfer, kein Nutzerpanel, Debug-Build. |
| **Automatische Prüfung** | Alle 36 erreichbaren Verwaltungsseiten: Statuscode, Konsolenfehler, Barrierefreiheit (axe-core), horizontaler Überlauf auf dem Handy. |
| **Code und Doku** | README, `docs/`, Navigationsmodell, Dockerfile, Compose, CI, Lizenzlage, offene Issues. |
| **Marktrecherche** | Trustpilot, Test- und Vergleichsportale, Capterra/GetApp. **Dünne Datenlage:** wenige Bewertungen je Produkt, viele Portale verdienen an Vermittlung (trusted.de, softwareabc24), Capterra-Bewertungen sind teils vom Anbieter angeworben. Zu Immoware24, Rentila und Hausverwalter Pro fanden sich keine Nutzerberichte. Reddit war nicht zugänglich. |

Was der Test **nicht** belegt: Verhalten echter Erstnutzer (Zielgruppe), Lastverhalten, Bankabruf per FinTS (laut Doku nur gegen Beispieldaten getestet), Browser außerhalb von Chromium, Screenreader-Nutzung. Die Zeitangaben stammen aus einem Debug-Build.

**Korrektur zur ersten Fassung dieses Berichts:** Der Einrichtungsassistent (Commit 6155d81 ff.) war beim ersten Durchgang noch nicht im Stand. Die Aussage „kein Erststart“ gilt so nicht mehr; sie gilt weiter für die **Daten**-Einrichtung (siehe 3.2).

## 1. Management Summary

1. **Funktional und im Detail ist MietHahn auffallend reif.** Alle 36 geprüften Verwaltungsseiten antworten fehlerfrei, ohne Konsolenfehler; die Oberfläche läuft auf dem Handy ohne horizontales Scrollen; Vertrags-PDF, Einladungsmail, Mieterportal, CSV-Import und Zuordnungsvorschläge funktionierten im Test. Die Umsatzzuordnung mit Begründung je Vorschlag ist besser als alles, was die recherchierten Wettbewerber beschreiben.
2. **Das größte UX-Problem sind versteckte Voraussetzungen.** Bis ein einfacher Mietvertrag als PDF vorliegt, muss man ein Vermieterprofil, Standardvorlagen, Wohnfläche und Buchhaltungsbeginn anlegen. Das System sagt das erst *nach* dem Fehlversuch. Der Einrichtungsassistent fragt nur Module ab (19 Schritte), nicht diese Daten.
3. **Vertrieb scheitert heute nicht an Funktionen, sondern an Grundlagen:** Das Repository ist privat, es gibt **keine Lizenz**, keinen Release-Tag, kein öffentliches Image, und `global.json` verlangt ein .NET-11-Preview-SDK, das die dokumentierte Docker-/CI-Build-Kette sehr wahrscheinlich bricht (siehe 6.1).
4. **Zielgruppenrealität:** Self-Hosting auf einem VPS schließt die breite Masse der Kleinvermieter aus. Der realistische Weg ist eine gestaffelte Strategie: erst Open Source für Selbsthoster (Reichweite, Feedback), dann ein verwaltetes Hosting (Einnahmen), flankiert von Installationshilfen.
5. **Rechtliches Risiko ist ein Vertriebsthema:** Die App erzeugt Verträge, Abrechnungen, Mieterhöhungen und Mahnungen, deren Vorlagen und Tabellen sie selbst als „vor dem Einsatz prüfen“ kennzeichnet. Für Dritte braucht es Haftungsklarheit, juristische Prüfung der Standardtexte und einen Pflegeprozess für Rechtsstände.

## 2. Marktüberblick

| Produkt | Modell / Preis (laut Quellen) | Stärken | Bekannte Schwächen |
|---|---|---|---|
| **immocloud** | SaaS, 9,99 € (5 WE) bis 39,99 € (50 WE) netto/Monat, 45 Tage Test, keine Gratisversion | Moderne UI, Bankabgleich, Mieterportal, DATEV, Dashboard, App, Support per Telefon/WhatsApp/Chat | Teuer für die Wohnungszahl, Einarbeitung anfangs komplex, App (Google Play 2,9/5) eingeschränkt, Postversand und digitale Verträge extra |
| **objego** | SaaS, Basis kostenlos; Verwaltung 7,95 €/Monat; NK-Modul 0,95 €/WE | Einfach, NK-Abrechnung in 4 Schritten, 31 Kostenarten | Keine Bankanbindung, Umlageschlüssel nicht nachträglich änderbar, Zähler nicht mit Schlüsseln verknüpft, schwache App |
| **vermietet.de** (Scout24) | SaaS, kostenloser Basisaccount, Module | Früher gutes Preis-Leistungs-Verhältnis | Trustpilot **1,4/5 bei 71 Bewertungen** (66 % Ein-Stern): gestrichene Funktionen nach Übernahme, Preiserhöhung („Daten weg“), Fehler in NK-Abrechnungen, defekter Bankabgleich, Chatbot-Support, Abo-/Kündigungsstreit |
| **WISO Vermieter/Hausverwalter** | Desktop + Web, 34,99 €/Jahr (bis 5 WE), max. 20 WE | Centgenaue Abrechnung, günstig, offline | Veraltete Optik, „gewöhnungsbedürftig“, Web-Version abgespeckt, 20 Minuten für 4 WE, keine Bankverknüpfung ohne Zusatzabo, kein Portal |
| **hellohousing** | SaaS | Capterra 4,6/5 (47 Bewertungen), NK/Heizkosten, Bank, Funkzähler/UVI | Navigation „teils umständlich“, Menü unübersichtlich, starre Anschreiben, Heizkosten nur Premium |
| **Vermieten+** | SaaS | Einstieg | Kein Portal, kein DATEV, Objekt-Wizard mühsam |
| **MicroRealEstate** | Open Source, self-hosted | Portal, Vorlagen, Docker | Kein deutsches Recht |
| **Excel** | frei | Vertraut (Finanztip-Forum 2025: weiter erste Empfehlung) | Fehleranfällig, keine Fristen/Kommunikation |

### Was Kunden anderswo bemängeln (Muster)

1. **Support/Verlässlichkeit** (vermietet.de dominant): Chatbots, unbeantwortete Mails, Fehler wochenlang offen.
2. **Lock-in, Preise, Abo-Fallen:** Preiserhöhungen, Bindung, Kündigungsprobleme, Datenverlust.
3. **Funktionsabbau und Zusatzkosten.**
4. **Fehlende Automatisierung** (keine Bankanbindung, manuelle Zähler).
5. **Starre Abrechnung** (Schlüssel fix, Anschreiben fix).
6. **Bedienung:** komplex, veraltet oder unübersichtlich.
7. **Schwache Mobile-App.**

MietHahn adressiert 2, 3, 4, 5 strukturell; 6 und 7 sind offen und hängen an der UX (Abschnitt 3).

## 3. UX-Bewertung auf Basis des Tests

### 3.1 Stärken (im Test belegt)

| Befund | Beleg |
|---|---|
| **Robuste Technik** | 36/36 Seiten Status 200, keine Konsolenfehler, Seitenwechsel im Mittel unter einer Sekunde (Debug-Build), Mieter-Magic-Link ohne Passwort funktioniert. |
| **Handy-Layout** | Auf 12 geprüften Seiten kein horizontales Scrollen; Bottom-Bar mit 4 Hauptbereichen + Menü; Formulare einspaltig. Konzeptionell stärker als die Apps von immocloud/objego (2,9/5 bzw. „nur Grundfunktionen“). |
| **Umsatzzuordnung** | Karte je Umsatz mit Vorschlag, Konfidenz und Begründung („Betrag entspricht der Monatsmiete · ‚Wohnung 1‘ im Verwendungszweck · Zeitraum 11/2026“), Aktionen Bestätigen/Ändern/Aufteilen/Ignorieren. Erklärbar, schnell, nachvollziehbar. |
| **Lenkung durch Warnungen** | Hinweise stehen an der richtigen Stelle („Bitte prüfen: Wohnung 1: Keine Wohnfläche zum 24.10.2026“, „Noch kein Buchhaltungsbeginn … Jetzt festlegen“, Frist-Banner im Abrechnungs-Assistenten „noch 68 Tage“). |
| **Seiteninfos** | „Info“-Dialog je Seite mit Hintergrund und Rechtsgrundlage (neu in 59527c2), z. B. Mieterkonto: Verrechnungsreihenfolge, Saldovortrag. Senkt die Einarbeitungskritik anderer Produkte. |
| **Schnellsuche** | Strg+K findet Person und Vertrag nach dem Teilnamen „anna“ in unter einer Sekunde. |
| **Leere Zustände mit Anleitung** | „Noch keine Objekte. Lege zuerst ein Haus an …“ |
| **Inline-Anlegen** | „Neue Person anlegen“ direkt im Vertragsformular. |
| **Fachliche Tiefe sichtbar** | Frist § 556 Abs. 3 BGB, Kappungsgrenze § 558, Anlage-V-Zeilennummern, Rechenprotokoll, Rolle Eigentümer. |
| **Sicherheit sichtbar** | 2FA-Pflicht mit Wiederherstellungscodes, strenge CSP (blockierte sogar das Testwerkzeug), Rückfrage-Dialoge. |
| **Barrierefreiheit** | axe-core: keine kritischen Verstöße; verbleibend je Seite 2–3 „serious“-Befunde (Linkname der Hell/Dunkel-Schaltflächen, ein Listenaufbau, ein unbenanntes Filterfeld auf Listenseiten) und ein Link-im-Fließtext-Kontrast auf *Module*. Sehr gut im Marktvergleich. |
| **Fertiges Vertrags-PDF** | Dreiseitiger Mietvertrag mit Kopf/Fuß, § 1–§ 4, Mietbestandteilen und BetrKV-Katalog aus den Stammdaten, Erzeugung in rund vier Sekunden. |

### 3.2 Schwächen und Reibungspunkte (nach Schwere)

| # | Befund | Schwere | Beleg und Wirkung |
|---|---|---|---|
| **P1** | **Voraussetzungen werden erst durch Fehler sichtbar** | Hoch | Weg zum ersten Vertrags-PDF: Vermieterprofil anlegen (Fehler erst nach Klick auf „Entwurf als PDF“: „Es ist noch kein Vermieterprofil hinterlegt“) → „Standardvorlagen anlegen“ (Seite „Vorlagen“, Hinweis erst auf der Vertragsseite) → Wohnfläche (Warnung erst auf dem Objekt) → Buchhaltungsbeginn (versteckt in *Forderungen → Einstellungen* zwischen 12 Mahnwesen-Feldern). Der Einrichtungsassistent (19 Schritte) behandelt nichts davon. |
| **P2** | **Dashboard sagt auf leerer Installation „Alles erledigt“** | Hoch | Nach Abschluss des Assistenten steht dort „Alles erledigt – es gibt gerade nichts zu tun.“, obwohl noch nichts angelegt ist. Erst nach dem ersten Vertrag erscheint eine Aufgabe („Buchhaltungsbeginn festlegen“). |
| **P3** | **Einrichtungsassistent ist lang und nicht entscheidungsfördernd** | Mittel | 19 Schritte (11 Modul-Fragen, 6 Profilfragen). Alles ist vorausgewählt: wer nur „Weiter“ klickt, schaltet alle Module, Mietmodelle, Gewerbe, Bürgen und Mahngebühren ein (Zusammenfassung im Test). Schritt 1 spricht von „11 Bereichen“, der Fortschrittsbalken zeigt „Schritt 1 von 19“, die Seitenleiste zeigt vier Phasen. Die Hauptnavigation bleibt während der Einrichtung sichtbar. |
| **P4** | **Wohnfläche fehlt im Dialog „Einheit hinzufügen“** | Mittel | Der Dialog hat Bezeichnung, Lage, Art. Die Fläche (für Umlage und Vertrag nötig) kommt erst über den Tab *Flächen*; direkt nach dem Anlegen warnt das Objekt für jede Einheit. Das Vertrags-PDF entstand im Test ohne Flächenangabe, ohne Warnung. |
| **P5** | **Datumsfelder ohne Formathinweis; „Monat“ ist ein Tageskalender** | Mittel | Mietbeginn akzeptierte „2026-11-01“ optisch, validierte aber „Bitte den Mietbeginn angeben“. Erst „01.11.2026“ funktionierte. „Buchhaltung ab (Monat)“ öffnet einen Tageskalender, „11.2026“ wurde stillschweigend nicht übernommen. Kein Platzhalter („TT.MM.JJJJ“). |
| **P6** | **Navigationstiefe: bis zu drei Tab-Reihen** | Mittel | Beispiel Mieterkonto: Hauptbereich *Verträge* → Tabs *Übersicht · Konto · Vertrag · Kommunikation · Ein- & Auszug* → Unterreiter *Mieterkonto · Vereinbarte Kostenarten* → Seitentabs *Buchungen · Zahlung & Forderung · Mahnwesen · Mieten je Jahr*. Das Objekt hat zwei Tab-Zeilen plus Gruppen (*Nebenkosten* = 5 Seiten). Das ist genau die Kritik an hellohousing/immocloud. 14 Bereiche in der Seitenleiste; auf dem Handy nur vier in der Bottom-Bar. |
| **P7** | **Personendialog zu schwergewichtig für den Normalfall** | Niedrig-Mittel | Der Dialog „Person anlegen“ zeigt 16 Felder (u. a. Titel, Firma, Kontaktart, Gewerk/Sparte, Geburtsdatum, Anschrift, Land) für den häufigsten Fall „Mieter mit Name und E-Mail“. |
| **P8** | **Mieterportal zeigt bei künftigem Mietbeginn „Miete 0,00 €“** | Mittel (Fehler) | Vertrag mit Beginn 01.11.2026, heute 24.10.2026: Verwaltung zeigt korrekt 980,00 €, das Portal „Miete 0,00 € im Monat“ und „Monatliche Miete Gesamt 0,00 €“. Typischer Fall beim Anlegen vor Einzug. |
| **P9** | **Portal zeigt keinen Zahlungsstand** | Mittel (Lücke) | Das Portal kennt Dokumente, Vertrag, Verbrauch, Nachrichten, Meldungen, Kontakt, aber weder Mieterkonto noch Salden oder Zahlungseingänge. Das ist der häufigste Mieter-Wunsch gegenüber Verwaltungen und ein Kernmerkmal von immocloud/hellohousing. |
| **P10** | **Einladung ohne Rückfrage und ohne Status** | Niedrig-Mittel | „Zugang einrichten und einladen“ sendet sofort; die Seite zeigt danach nur „Konto anna@…“ ohne „Einladung gesendet am …“ oder erneutes Senden. Die Mail lag erst nach einigen Minuten im Postfach (Warteschlange), ohne Hinweis. Die Mail kommt von „MietHahn“ ohne Namen des Vermieters: Mieter könnten sie für Phishing halten. |
| **P11** | **Unterstützende Texte uneinheitlich** | Niedrig | Leerzustände duzen („Lege zuerst …“, „Füge unten …“), alles andere siezt. „Monatsmieten aktualisieren“ meldet „Die Monatsmieten sind aktuell“, auch wenn kein Buchhaltungsbeginn gesetzt ist (irreführend). „Abweichung zum Vorjahr (± %)“ im Assistenten hat keine Erklärung. |
| **P12** | **CSV-Import erkennt Spalten nur teilweise** | Niedrig-Mittel | Mit der Kopfzeile „Auftraggeber/Empfänger“ blieb „Gegenseite“ leer; dadurch schlug die Zuordnung die Miete nur mit „48 % (niedrig)“ vor, obwohl Betrag, Wohnung und Zeitraum passten. Echte Bankexporte (z. B. Sparkasse) wurden nicht getestet; ein CSV-Profil lässt sich anlegen. |
| **P13** | **Handy: Info-Schaltfläche belegt eine ganze Zeile; Karten ohne einheitlichen Innenabstand** | Niedrig | Im Portal beginnt Fließtext links bündig vor der Überschrift („Keine ungelesenen Dokumente.“); lange Formulare ohne mitlaufende Speichern-Schaltfläche. |
| **P14** | **Kaution nur als Betrag und Notiz** | Lücke | Laut Assistent: „MietHahn verwaltet kein Kautionskonto.“ Anlage, Verzinsung, Rückzahlung und Verrechnung fehlen. |

### 3.3 Aufgabenorientierte Messung (Einzelprüfer, geschätzt)

| Aufgabe | Ergebnis | Reibung |
|---|---|---|
| Erstkonto + 2FA | geschafft, 3 Seiten | 2FA-Schlüssel nur als Text/QR; ohne Authenticator-App kein Weiterkommen (bewusst) |
| Einrichtungsassistent | geschafft mit 19 Klicks | P3 |
| Objekt anlegen | ohne Fehler | – |
| 2 Einheiten anlegen | ohne Fehler | P4 |
| Mieter anlegen + Vertrag | im zweiten Versuch | P5 (Datum); Person inline gut |
| Buchhaltungsbeginn setzen | gefunden über den Hinweis | P1, P5 |
| Vertrags-PDF erstellen | im dritten Versuch | P1 (Vermieterprofil, Vorlagen) |
| Mieter einladen, Portal ansehen | geschafft | P9, P10, P8 |
| Kontoauszug importieren, zuordnen | geschafft | P12; sonst sehr gut |
| Nebenkostenabrechnung | Assistent gestartet, nicht beendet | Es fehlen Belege, Flächen, Verträge im Abrechnungsjahr; die Vorschau meldet „keine umlagefähigen Kostenpositionen“ sachlich korrekt |

Nicht getestet: Heizkostenabrechnung, Mieterhöhung, Mahnlauf, Tickets/Handwerker, Hausmeister-Bereich, Eigentümer-Portal, Backup/Restore, FinTS.

### 3.4 Vergleich Oberfläche (Einschätzung)

| Kriterium | MietHahn | immocloud | objego | vermietet.de | WISO | hellohousing |
|---|---|---|---|---|---|---|
| Modernes Design | sehr gut | sehr gut | gut | mittel | schwach | gut |
| Orientierung/Navigation | mittel (P6) | gut | gut | mittel | schwach | mittel |
| Einstieg | mittel (P1–P3) | gut | sehr gut | mittel | mittel | gut |
| Handy | gut | mittel | schwach | mittel | schwach | mittel |
| Erklärbarkeit der Ergebnisse | sehr gut | unbekannt | gut | unbekannt | gut | gut |
| Barrierefreiheit | sehr gut | unbekannt | unbekannt | unbekannt | unbekannt | unbekannt |
| Support | nicht vorhanden | sehr gut | gut | schwach | mittel | mittel |
| Datenübernahme | schwach | gut | gut | gut | mittel | gut |

*Wettbewerberwerte aus Testberichten und Nutzerstimmen, nicht aus eigener Prüfung.*

## 4. Funktionsvergleich

● vorhanden · ◐ teilweise/unklar · ○ fehlt. MietHahn-Spalte aus Test, Doku und Code; Wettbewerber aus Anbieterangaben und Testberichten.

| Funktion | MietHahn | immocloud | objego | vermietet.de | WISO | hellohousing |
|---|---|---|---|---|---|---|
| Objekte/Einheiten/WG-Zimmer | ● | ● | ● | ● | ● | ● |
| Standard-/Index-/Staffelmiete | ● | ● | ◐ | ● | ● | ◐ |
| Bankabgleich | ● (Datei, FinTS ungetestet mit echter Bank) | ● | ○ | ● (Nutzer: defekt) | ○ | ● |
| Mahnwesen | ● | ◐ | ○ | ◐ | ◐ | ◐ |
| NK-Abrechnung | ● | ● | ● | ● | ● | ● |
| Heizkosten + CO₂ | ● | ◐ | ● | ◐ | ● | ● |
| Funkzähler/UVI | ◐ (CSV) | ◐ | ○ | ○ | ○ | ● |
| Anlage V/AfA/Darlehen | ● | ◐ | ● | ◐ | ◐ | ○ |
| ELSTER-Direktübermittlung | ○ (#53) | ○ | ○ | ○ | ○ | ○ |
| DATEV | ● | ● | ○ | ◐ | ○ | ◐ |
| Mieterportal | ● (ohne Konto/Salden) | ● | ○ | ◐ | ○ | ● |
| Tickets/Handwerker | ● | ● | ○ | ○ | ○ | ◐ |
| Belege + KI-Auslesung | ● (optional) | ◐ | ◐ | ○ | ○ | ● |
| Mieterhöhung | ● | ◐ | ◐ | ◐ | ◐ | ○ |
| Übergabeprotokolle | ● (mit Unterschrift) | ◐ | ○ | ○ | ◐ | ○ |
| Fristen/Wartung | ● | ◐ | ○ | ○ | ○ | ○ |
| Eigentümer je Objekt | ● (Anteile, Portal) | ◐ | ○ | ○ | ○ | ○ |
| Rollen, Audit-Log | ● | ◐ | ◐ | ◐ | ○ | ◐ |
| Vollexport | ● | ◐ | ◐ | ◐ | ● | ◐ |
| Module ein-/ausschaltbar | ● (neu) | ◐ | ● | ● | ◐ | ● |
| Native App | ○ (PWA) | ● | ● | ◐ | ○ | ◐ |
| Digitale Unterschrift Vertrag | ○ | ● (extra) | ○ | ◐ | ○ | ○ |
| Online-Zahlung/SEPA-Lastschrift | ○ | ○ | ○ | ○ | ○ | ○ |
| Kautionskonto | ○ | ◐ | ○ | ○ | ○ | ○ |
| Kennzahlen (Cashflow, Rendite) | ○ | ● | ◐ | ◐ | ○ | ○ |
| Import aus Fremdsystemen | ○ | ◐ | ◐ | ○ | ○ | ◐ |
| Inserate/Bewerber | ○ | ◐ | ○ | ◐ | ○ | ○ |

## 5. Fehlende Funktionen und Wünsche (priorisiert)

| Prio | Maßnahme | Bezug |
|---|---|---|
| **Vor Vertrieb** | Daten-Einrichtungsassistent (Vermieterprofil, Standardvorlagen automatisch, Objekt, Einheit mit Fläche, Mieter, Vertrag, Bankkonto, Buchhaltungsbeginn); Dashboard-Leerzustand mit „Nächster Schritt“; Voraussetzungen als Checkliste anzeigen | P1, P2, P4 |
| Vor Vertrieb | Datumsfelder: Platzhalter, deutsches Format, Monatsauswahl; Fehler an der richtigen Stelle | P5 |
| Vor Vertrieb | Portal-Fehler bei künftigem Mietbeginn beheben | P8 |
| Vor Vertrieb | Assistent kürzen (Modulwahl in eine Seite, sinnvolle Vorgaben nach Profilfrage „Wie viele Wohnungen?“), Navigation ausblenden | P3 |
| Kurzfristig | Import: Objekte, Mieter, Verträge, Salden aus Excel/CSV; robustere CSV-Erkennung; Beispieldaten auf Knopfdruck für Demo | P12 |
| Kurzfristig | Portal: Mieterkonto lesend (Saldo, Zahlungen), Einladungsstatus, Absender mit Vermieternamen | P9, P10 |
| Kurzfristig | Navigation: Hauptbereiche bündeln (etwa *Übersicht · Bestand · Verträge & Mieter · Geld · Abrechnung & Steuer · Kommunikation · System*), maximal zwei Tab-Reihen | P6 |
| Mittel | Jahresabrechnungs-Cockpit (Checkliste je Objekt/Jahr, Vorjahresvergleich je Kostenart) | Kernnutzen |
| Mittel | Kennzahlen (Cashflow, Mietrückstand, Leerstand, Kosten je m²) | immocloud |
| Mittel | Kautionskonto, Web-Push, Fotoeingabe für Zählerstand/Belege auf dem Handy | P14 |
| Später | FinTS mit photoTAN, wM-Bus-Gateway, ELSTER, digitale Unterschrift, Lastschriftdatei, Bewerberverwaltung | #53, #55 |

## 6. Vertrieb

### 6.1 Blocker (vor jeder Verbreitung zu klären)

| # | Befund | Folge | Aufwand |
|---|---|---|---|
| V1 | **Keine Lizenz.** Das Repository ist privat, hat keine `LICENSE`-Datei, keine Lizenzangabe auf GitHub. | Ohne Lizenz darf niemand die Software legal nutzen oder weitergeben. | Entscheidung + Datei, Stunden |
| V2 | **`global.json` verlangt `11.0.100-preview.7.26381.103`** (Commit c9ddd0f „Updates“, vorher `10.0.100`), Zielframework ist `net10.0`. Auf einem Rechner mit SDK 10.0.401 bricht jeder `dotnet`-Befehl im Repo mit „A compatible .NET SDK was not found“ ab (so im Test geschehen; der Test lief mit einer lokal korrigierten Kopie). Das Dockerfile kopiert `global.json` in das Image `sdk:10.0`, die CI nutzt `global-json-file`. Beides schlägt dadurch sehr wahrscheinlich fehl; ich habe `docker build` und die CI-Läufe selbst nicht ausgeführt. | Release-Image und Selbstbau nicht möglich. Vermutlich unbeabsichtigt. | Minuten, aber sofort |
| V3 | **Kein Release.** Keine Git-Tags, keine GitHub-Releases, GHCR-Paket nicht veröffentlicht (privat). Dokumentierter Install-Weg nutzt `ghcr.io/ther00st3r/miethahn:latest`. | Die Installationsanleitung läuft für Dritte ins Leere. | Stunden |
| V4 | **Haftung und Rechtsstände.** Die App kennzeichnet selbst: Vorlagen „Texte vor dem Einsatz prüfen“, CO₂-Tabellen und Gradtagszahlen „Stand/prüfen“, Mahnvorlage „rechtlich prüfen“, FinTS „nur gegen Beispieldaten getestet“. | Für Dritte brauchbar nur mit Haftungsausschluss, juristisch geprüften Standardtexten und einem Pflegeprozess (z. B. Basiszinssatz halbjährlich, CO₂-Stufen, VPI). | Wochen, fortlaufend |
| V5 | **Name und Marke.** „MietHahn“ ist nicht geprüft (DPMA/EUIPO), Domain/Handle unklar. FinTS-Registrierung ist für „MietHahn“ eingetragen. | Umbenennung nach Start wäre teuer (Registrierung, Image-Namen). | Tage |
| V6 | **Kein sichtbarer Auftritt.** README ohne Screenshots, Demo oder „Warum MietHahn“; keine Landingpage, keine Beispieldaten. | Interessenten können nicht ohne Installation beurteilen. | Tage |
| V7 | **Installationshürde.** VPS, Domain, DNS, Docker, SMTP, Backup-Ziel. Bei typischen Kleinvermietern nicht vorhanden. | Reichweite auf Technikaffine begrenzt. | siehe 6.3 |
| V8 | **Kein Support-Kanal.** Weder Forum noch Issue-Vorlagen noch Zusagen. | Wiederholt genau die Kritik an vermietet.de. | gering, aber laufend |

### 6.2 Zielgruppen

| Gruppe | Bedarf | Eignung heute |
|---|---|---|
| Kleinvermieter 1–5 WE | Abrechnung ohne Formfehler, günstig | **Gering:** Self-Hosting nicht zumutbar. Nur über verwaltetes Hosting oder fertige Appliance erreichbar. |
| Wachsende Privatvermieter 5–50 WE | Bankabgleich, Portal, Automatisierung, planbare Kosten | **Hoch**, wenn Betrieb abgenommen wird; hier leiden Kunden von immocloud/vermietet.de an Preis und Support. |
| Selbsthoster und Technikaffine | Datenhoheit, Anpassbarkeit | **Hoch heute**, nach V1–V3 sofort bedienbar; Multiplikator und Testgruppe. |
| WG-Vermieter, Familien-Eigentümergemeinschaften | Zimmerverträge, Anteile, Eigentümer-Portal | **Hoch**, Alleinstellungsmerkmal (Zimmer als Untereinheiten, Eigentümer je Objekt). |
| Kleine Hausverwaltungen, Steuerberater als Kanal | DATEV, Mehrobjekt | Mittel; Mandantenfähigkeit fehlt (eine Installation = ein Vermieter). |

### 6.3 Vertriebswege (Optionen)

| Weg | Idee | Voraussetzung | Chance | Risiko |
|---|---|---|---|---|
| **A. Open Source für Selbsthoster** | Öffentliches Repository, Release-Tags, Compose-Installation, Doku | V1–V3, V6, V8 | Reichweite, Feedback, Vertrauen (Datenhoheit, kein Lock-in) | Kein Umsatz; Supportaufwand; Nachbau als SaaS durch Dritte, falls Lizenz zu offen |
| **B. Verwaltetes Hosting (Einzelinstallation je Kunde)** | Betrieb, Updates, Backup, Domain, Mail im Abo; je Kunde ein eigener Compose-Stack | A, plus Betriebsautomation, Auftragsverarbeitungsvertrag, Support | Erreicht Gruppe 2 und Kleinvermieter; Preis-Anker: immocloud 9,99–39,99 €/Monat netto, objego ab 0 €, WISO 34,99 €/Jahr | Haftung für Mieterdaten, Support rund um die Uhr, Skalierung pro Kunde teuer, Datenschutzpflichten |
| **C. Installationshilfen** | Ein-Kommando-Installer, Vorlagen für Coolify/Portainer/CasaOS/Unraid/Synology, optional vorkonfigurierter VPS-Image | A | Senkt V7 deutlich, kostet wenig | Pflege der Vorlagen |
| **D. Open Core / Support-Abo** | Kern frei, bezahlt: Support, Rechtsstands-Updates (Mietspiegel, Basiszinssatz, CO₂-Tabellen), Onboarding-Service | A | Passt zur Stärke „fachliche Tiefe“; Einnahmen ohne Mandantenbetrieb | Tragfähigkeit unklar; Abgrenzung frei/bezahlt muss vor Veröffentlichung entschieden werden |
| **E. Multiplikatoren** | Steuerberater, Vermietervereine (Haus & Grund), Foren, Blogs | A, V6 | Zielgruppe direkt | Aufwand, Beratungshaftung |

**Empfehlung:** A + C jetzt vorbereiten (Blocker V1–V3, V6), parallel eine geschlossene Testgruppe von 5–10 echten Vermietern aufbauen, danach B als Pilot mit wenigen Kunden. D (bezahlte Rechtsstands-Pflege) prüfen, weil sie die eigentliche Dauerleistung ist, die Selbsthoster nicht selbst erbringen wollen.

**Lizenz (Entscheidung beim Eigentümer):** Für Software, die im Netz betrieben wird und nicht von Dritten als geschlossenes SaaS ausgeschlachtet werden soll, ist AGPL-3.0 das übliche Mittel; die genutzten Abhängigkeiten (u. a. MIT, Apache-2.0, BSD, libfintx LGPL-3.0 als unverändertes NuGet-Paket laut `THIRD-PARTY-NOTICES.md`) widersprechen dem nach erster Durchsicht nicht. Soll das Hosting dem Anbieter vorbehalten bleiben, wäre eine quelloffene, aber nutzungsbeschränkte Lizenz (z. B. Business Source License) die Alternative. Beides vor Veröffentlichung juristisch prüfen lassen; keine Rechtsberatung.

### 6.4 Positionierung (Vorschlag)

> **MietHahn: die Vermieterverwaltung, die Ihnen gehört.** Nebenkosten- und Heizkostenabrechnung nach deutschem Recht, Bankabgleich, Mieterportal und Steuerunterlagen, ohne Abo-Falle, mit allen Daten bei Ihnen und einem Export, der ohne MietHahn lesbar bleibt.

Belegbare Aussagen für die Außendarstellung: alle Daten exportierbar (Vollexport JSON/CSV/ZIP), keine Module hinter Bezahlschranke, Heizkosten-Eigenabrechnung mit CO₂-Aufteilung, WG-Zimmerverträge, Eigentümer je Objekt, 2FA-Pflicht, hohe Testabdeckung (165 Testdateien im Repository, fachliche Tests für Rechenkerne). Nicht behaupten, solange nicht belegt: „rechtssicher“, „FinTS für alle Banken“.

### 6.5 Fahrplan

| Phase | Inhalt | Ziel |
|---|---|---|
| **0 – Sofort (Tage)** | V2 `global.json` korrigieren und CI grün bestätigen; Lizenz wählen; Release `v0.1.0` mit öffentlichem Image; README mit Screenshots, „Für wen“, Demo-Hinweis | Dritte können installieren |
| **1 – Erstnutzung (2–4 Wochen)** | P1/P2/P4/P5/P8 beheben; Daten-Einrichtungsassistent; Beispieldaten-Knopf; Ein-Kommando-Installer; Issue-Vorlagen, Diskussionsforum | Neue Installation führt ohne Fehlversuch zum ersten Vertrag und zur ersten Abrechnung |
| **2 – Vertrauen (4–8 Wochen)** | Juristische Prüfung der Standardvorlagen, Haftungsausschluss, Pflegeprozess Rechtsstände; Test mit realen Bankexporten und FinTS-Pilotbanken; geschlossene Testgruppe mit echten Daten (Import!) | Belastbare Aussage über Qualität |
| **3 – Reichweite** | Import aus Excel/CSV, Portal-Mieterkonto, Navigation bündeln, Vorlagen für Coolify/Unraid/Synology, Vorstellung in Foren/Blogs | Wachstum der Selbsthoster |
| **4 – Einnahmen (Pilot)** | Verwaltetes Hosting mit Auftragsverarbeitungsvertrag oder bezahlte Rechtsstands-Pflege/Support | Erste zahlende Kunden |

## 7. Offene Entscheidungen für den Produktverantwortlichen

1. Lizenz und Geschäftsmodell (AGPL/Open Core/Hosting-Vorbehalt).
2. Primäre Zielgruppe: Selbsthoster oder wachsender Privatvermieter mit Hosting?
3. Wer pflegt Rechtsstände dauerhaft, und mit welcher Haftungsaussage?
4. Markenprüfung für „MietHahn“ vor dem öffentlichen Start.
5. Portalumfang: Mieterkonto lesend ja/nein; Zahlungen bewusst ausgeschlossen?

## 8. Methodenhinweis zur Aussagekraft

Die UX-Befunde P1–P14 sind reproduzierbar (Schritte oben), aber von **einem** Prüfer mit Entwicklerwissen erhoben. Für belastbare Zahlen (Aufgabenerfolg, Zeit, Fehler) empfiehlt sich ein Test mit fünf Vermietern ohne Vorkenntnis an den Aufgaben der Tabelle 3.3 (Think-aloud, Bildschirmaufzeichnung). Er ersetzt die geschätzten Werte dieses Berichts.

## 9. Zweite Prüfung vor der Veröffentlichung (24.10.2026)

Anlass: Ziel „die einfachste und vollständigste Software zur Unterstützung von Vermietern und Mietern“. Geprüft wurden der Stand nach den Umsetzungen der Phasen 1 bis 3, die Wettbewerber mit aktuellen Quellen und der Code gegen eine Funktionsliste.

### 9.1 Neuer Durchlauf (Stand a2ae2f1)

- **Technik:** Alle 32 Seiten der Verwaltung (mit Beispieldaten) antworten fehlerfrei und unter 2,5 s (Debug-Build). Gesamttestlauf zuvor grün (1407 Tests).
- **Gefunden und behoben:**
  1. Der Datenimport buchte keine eingegangene Kaution, das Dashboard meldete deshalb für jeden übernommenen Vertrag „Kaution offen“ (jetzt: Spalte „Kaution eingegangen“, ohne sie gilt die ganze Kaution als gezahlt).
  2. Der Objektschritt im Einrichtungsassistenten blockierte „Weiter“ mit Pflichtfeld-Fehlern (jetzt: Haken „Erstes Objekt jetzt anlegen“, ohne Haken wird übersprungen).
  3. Barrierefreiheit (axe-core): ungültige Struktur der Kennzahlen und der Seitenleiste, Namen von Schnellsuche und Benutzermenü ohne den sichtbaren Text, Textlinks ohne Unterstreichung. Alles behoben.
  4. **Lücke Wohnungsgeberbestätigung (§ 19 BMG):** Sie muss binnen zwei Wochen nach Ein- und Auszug ausgestellt werden (Ordnungswidrigkeit bis 1.000 €). Es gab keine Vorlage. Jetzt als Standardvorlagen für Einzug und Auszug, dazu „Bestätigung der Kündigung“ und ein Hinweis auf der Übergabeprotokoll-Seite.
- **Offen geblieben (klein):** Die Zeilen-pro-Seite-Auswahl der Tabellen hat keinen Namen für Screenreader (Komponente der Bibliothek); die Seitenleiste hat weiter zwölf Bereiche (#94).

### 9.2 Wo Wettbewerber heute besser sind

| Bereich | Wettbewerber | MietHahn | Wirkung auf das Ziel | Issue |
|---|---|---|---|---|
| **Betrieb ohne Technik** | SaaS, Anmeldung genügt | Selbst betriebener Server | größte Hürde für Kleinvermieter | Hosting-Pilot (#100), Skript (erledigt) |
| **Bankanbindung** | immocloud über finAPI, hellohousing | FinTS direkt, nur gegen Beispieldaten getestet, photoTAN fehlt; CSV-Import als Rückweg | Automatik ist ein Kernversprechen | #107 |
| **Native Apps und Push** | immocloud iOS/Android (Android laut Quelle schwächer bewertet), objego | Installierbare Web-App ohne Service Worker, keine Push-Nachrichten | Fristen und Nachrichten erreichen nur, wer die Seite öffnet | #105 |
| **Digitale Unterschrift** | immocloud (Zusatzoption) | Drucken, unterschreiben, hochladen | Medienbruch beim Vertrag | #106 |
| **Belege per E-Mail** | immocloud (ab Manager) | Hochladen oder fotografieren | Komfort bei vielen Belegen | #108 |
| **Support** | immocloud Telefon, WhatsApp, Chat | Community, kein Support-Kanal | Vertrauen bei Einsteigern | #102 |
| **Anwender-Hilfe** | Hilfeseiten, Changelogs | Seiteninfos in der App; Handbuch fehlt | „einfach“ braucht erklärte Abläufe | #111 |
| **Zähler: Mieter meldet selbst** | in den Quellen nicht belegt; üblich bei Versorgern | fehlt im Portal | größter manueller Aufwand vor der Abrechnung | #104 |

Was die Wettbewerber **nicht** besser machen (nach den Quellen): Mieterportal gibt es bei immocloud erst ab 15 Einheiten (hier immer), mehrere Benutzer erst ab 30 Einheiten (hier immer), Funktionsstufen mit harten Preissprüngen, Preise netto. Gleichauf oder besser: Indexmieten-Assistent, CO₂-Kostenaufteilung und Heizkosten in der Abrechnung (immocloud hat sie im Oktober 2026 nachgezogen), Eigentümergruppen, Mietzuordnung mit Begründung (immocloud kündigt KI-Vorschläge an).

### 9.3 Fachlich noch nicht vollständig

| Lücke | Hinweis | Issue |
|---|---|---|
| Mieterhöhung nach Modernisierung (§ 559 BGB) | häufiger Fall bei Sanierung; Vorlagen gibt es für §§ 557a, 557b, 558, 560 | #109 |
| Mietbescheinigung, Mietschuldenfreiheit, Kautionsrückzahlung | häufige Mieterwünsche; im Briefmodell fehlen Miete und Kaution | #110 |
| **Heizkosten: Platzhalter-Gradtagszahlen, ungeprüfte CO₂-Tabelle** | **Blocker für den Vertrieb der Heizkostenabrechnung** (siehe Abschnitt 6.1, Seite „Rechtsstände“) | #88 |
| Rechtlich geprüfte Vorlagen | bisher nur „vor dem Einsatz prüfen“ | #86 |
| Belegeinsicht für Mieter (§ 259 BGB) im Portal | kleine Ergänzung zum Konto | in #104 aufnehmen, wenn das Portal erweitert wird |

### 9.4 Einordnung zum Ziel

- **Vollständig:** Für private Vermieter mit Wohnraum deckt MietHahn den Ablauf von Einzug bis Auszug, Miete, Nebenkosten, Heizkosten, Kaution, Steuer und Mieterportal ab, mit mehr eingebauter Führung (Checkliste, Voraussetzungen vorab, Erklärungen je Seite) als die recherchierten Wettbewerber. Es bleiben die Lücken in 9.3 und die Bankanbindung.
- **Einfach:** Die Bedienung ist nach dem zweiten Durchlauf ohne Fehlversuch möglich; die größte Hürde ist nicht die Oberfläche, sondern der Betrieb (Server, Domain, Mailversand). Ob die Oberfläche für Erstnutzer einfach genug ist, belegt nur der Test mit echten Vermietern (#90).
- **Ehrlich zu sagen:** Funktionsumfang und Rechtstexte sind hinter ihren Anbietern bei Support, Apps und geprüften Texten. Das lässt sich im Nebenprojekt nur teilweise schließen; die Reihenfolge der Issues #104 bis #111 folgt dem Nutzen für das Ziel.

## Quellen

- Eigener Test (Playwright, axe-core 4.x), Commits 59527c2 und a2ae2f1
- [immocloud Changelog](https://www.immocloud.de/funktionen/changelog/), [immocloud Tarife und Schwächen](https://www.hausverwaltungschecker.de/immocloud/), [hellohousing Übersicht](https://trusted.de/hellohousing)
- [Haus & Grund: Wohnungsgeberbestätigung](https://www.hausundgrund.de/wohnungsgeberbestaetigung), [ImmoScout24: Wohnungsgeberbestätigung](https://www.immobilienscout24.de/wissen/vermieten/wohnungsgeberbestaetigung.html)
- [Trustpilot: vermietet.de](https://de.trustpilot.com/review/vermietet.de)
- [ohnehausverwaltung.de: immocloud](https://ohnehausverwaltung.de/immocloud-erfahrungen-kosten-funktionen-2026-lohnt-sich-die-software-fuer-vermieter/), [objego](https://ohnehausverwaltung.de/objego-erfahrungen-2025-test-der-vermieter-software/), [WISO Vermieter](https://ohnehausverwaltung.de/wiso-vermieter-erfahrungen-kosten-lohnt-sich-2026/), [vermietet.de](https://ohnehausverwaltung.de/vermietet-de-erfahrungen-kosten-lohnt-sich/)
- [softwareabc24.de: vermietet.de](https://www.softwareabc24.de/hausverwaltung-software/vermietet-de), [Vermieter-Software-Ranking](https://www.softwareabc24.de/vermieter-software/)
- [heise: objego im Test](https://www.heise.de/download/specials/objego-im-Test-Immobilien-einfach-verwalten-und-vermieten-9163013), [heise: Hausverwaltungssoftware im Vergleich](https://www.heise.de/download/specials/Hausverwaltungssoftware-im-Vergleich-7151831)
- [Capterra: hellohousing](https://www.capterra.com/p/253335/hellohousing/reviews/), [GetApp: hellohousing](https://www.getapp.de/software/2070744/hellohousing)
- [Finanztip-Forum: Nebenkostenabrechnung-Programme](https://www.finanztip.de/community/forum/thema/42783-wer-hat-erfahrung-mit-nebenkostenabrechnungsprogrammen-f%C3%BCr-ios-apple/)
- Intern: [Marktanalyse](marktanalyse.md), [UI-Leitfaden](ui.md), [Betrieb](betrieb.md), [FinTS](banking-fints.md), [Entwicklung](entwicklung.md), `THIRD-PARTY-NOTICES.md`
