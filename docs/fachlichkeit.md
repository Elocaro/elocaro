# Fachlichkeit & Anforderungen

Ergebnis des Anforderungsinterviews (Oktober 2026). Rechtliche Hinweise sind Umsetzungshinweise, **keine Rechtsberatung**.

## Ausgangslage

- 1 Mehrfamilienhaus mit 4 Wohneinheiten, eine davon ist eine **WG mit Einzelverträgen je Zimmer**. Weitere Häuser müssen verwaltbar sein.
- Alleineigentum. Eigentümer je Objekt mit Anteilen kommt **später**.
- Keine WEG, keine Gewerbeeinheiten.
- **Heizung zentral (Gas/Öl)**, Warmwasser zentral, **Wärmemengenzähler je Wohnung**, Abrechnung **selbst** (kein Messdienst).
- Zähler: Wasserzähler je Wohnung, Hauptzähler, Allgemeinstrom. Keine Funkzähler.
- WG-Zimmer zahlen eine **Pauschale** und bekommen keine NK-Abrechnung.
- Keine Altdaten-Migration, Start im aktuellen Jahr.

## Nutzer & Rollen

| Rolle | Zugriff | Login |
|---|---|---|
| **Admin** (Eigentümer) | Alles | Lokales Konto + 2FA (TOTP) |
| **Verwalter** (Partner/Familie) | Alles außer Benutzer-/Systemverwaltung (konfigurierbar) | Lokales Konto + 2FA |
| **Hausmeister** | Tickets, Aufgaben, Objekte (ohne Finanzen/Mieter-Personendaten außer Kontakt) | Lokales Konto oder Magic Link |
| **Handwerker** | Nur die ihm zugewiesenen Tickets | Magic Link (zeitlich begrenzt) |
| **Mieter** | Eigene Verträge, Dokumente, Tickets, Aushänge des Hauses | Magic Link per E-Mail |

## Domänenmodell (Kern)

```
Objekt (Haus) 1─n Einheit (Wohnung) 1─n Zimmer (optional, für WG)
      │                 │                    │
      │                 └──── Mietobjekt ────┘   (Einheit ODER Zimmer ist vermietbar)
      │                           │
      │                      Mietvertrag n─m Mieter
      │                           ├─ Mietbestandteile (Kaltmiete, NK-VZ, HK-VZ, Pauschale, Stellplatz) mit Gültigkeit
      │                           ├─ Mietmodell: Standard | Index | Staffel
      │                           ├─ Personenanzahl (zeitabhängig)
      │                           └─ Kaution (nur Betrag + Notiz)
      ├─ Zähler (Haupt-/Unter-/Wärmemengen-/Allgemeinstrom) ─ Zählerstände (mit Foto)
      ├─ Kostenposition (BetrKV-Kostenart, Betrag, Zeitraum, Beleg, §35a-Anteil)
      ├─ Darlehen (Tilgungsplan), AfA-Basis, Erhaltungsaufwand
      └─ Dokumente, Tickets, Aushänge
Konto ─ Umsatz ─ Zuordnung ─ Sollstellung (Offener Posten)
```

- Flächen: Wohnfläche je Einheit/Zimmer und Gemeinschaftsflächen. Heizfläche kann abweichen.
  - Objekt: Gesamtfläche und Gemeinschaftsfläche (Treppenhaus, Keller …).
  - Einheit: Wohnfläche (WoFlV, Umlage nach Fläche) und Heizfläche (Grundkosten Heizung; leer = Wohnfläche).
  - WG-Zimmer: Zimmerfläche und Anteil an den Gemeinschaftsflächen der WG (Küche, Bad, Flur); Umlagefläche = Summe.
  - Plausibilität (Hinweis, kein Fehler): Σ Wohnflächen + Gemeinschaftsfläche ≤ Gesamtfläche; Σ Zimmer-Umlageflächen ≤ Wohnfläche der WG.
- Vermietbar ist eine Einheit ohne Zimmer oder ein einzelnes WG-Zimmer. Hat eine Einheit Zimmer, wird sie nur noch zimmerweise vermietet.
- **Mietvertrag:** 1..n Mieter (gesamtschuldnerisch) und optional Bürgen; Beginn, Befristung (§ 575 BGB) oder Ende nach Kündigung; Fälligkeit am n-ten Werktag (Standard 3., § 556b), Zahlungsart, Kaution (Betrag + Notiz).
- **Kündigung (§ 573c BGB):** Zugang bis zum 3. Werktag zählt den Monat mit; Mieter 3 Monate (vertraglich änderbar), Vermieter 3/6/9 Monate nach 0/5/8 Jahren. Werktage: Mo–Fr ohne bundesweite Feiertage (Samstag zählt nicht).
- **Mieterwechsel:** Auszug (letzter Tag, neue Anschrift der Mieter ab dem Folgetag) → Zählerstände → neuer Vertrag ab dem Folgetag. Zeiten ohne Vertrag sind Leerstand.
- Alle Werte mit **Gültigkeitszeitraum** (Fläche, Miete, Personen, Schlüssel), damit Abrechnungen historisch korrekt sind.
- Geld als `decimal`, Rundung kaufmännisch auf Cent erst am Ende einer Position.

## Mietmodelle

- **Standardmiete:** Erhöhung auf die ortsübliche Vergleichsmiete (§558 BGB). Prüft 15 Monate Wartezeit und 1 Jahr seit der letzten Erhöhung, Kappungsgrenze 20 % bzw. 15 % (konfigurierbar je Gemeinde) in 3 Jahren. Begründung: Mietspiegel oder 3 Vergleichswohnungen. Zustimmung des Mieters nötig, mit Fristen.
- **Indexmiete (§557b):** Anpassung nach VPI (Destatis, Basis 2020=100), frühestens 12 Monate nach der letzten Anpassung, Erklärung in Textform. Wirksam ab dem übernächsten Monat. Neue Miete = Miete × VPI neu ÷ VPI alt, auf Cent gerundet; die Prozentänderung wird mit einer Nachkommastelle angezeigt.
- **Staffelmiete (§557a):** Staffeln mit Datum und Betrag, werden automatisch zu Sollstellungen.
- **Pauschale (WG-Zimmer):** Betriebskostenpauschale. Erhöhung nur, wenn sie vertraglich vorbehalten ist (§560 Abs. 1), und nur bei gestiegenen Kosten. MietHahn stellt intern dennoch **Ist-Kosten der WG-Wohnung den Pauschaleinnahmen gegenüber**.

> ⚠️ **Hinweis Heizkosten bei WG-Pauschale:** Die HeizkostenV hat Vorrang vor vertraglichen Vereinbarungen (§2 HeizkostenV). Eine Pauschale, die Heizung und Warmwasser abdeckt, ist bei zentraler Heizung grundsätzlich nicht zulässig. Mieter können dann die Heizkosten um 15 % kürzen (§12). Ausnahme ist nur das selbst bewohnte Zweifamilienhaus. **Empfehlung:** rechtlich prüfen lassen. Eine Alternative wäre eine verbrauchsabhängige Abrechnung der WG-Wohnung über deren Wärmemengenzähler, verteilt auf die Zimmer nach Fläche. MietHahn soll beide Varianten abbilden können.

## Betriebskostenabrechnung (BetrKV, §556 BGB)

- Kostenarten nach **§2 BetrKV** (Nr. 1–17): Grundsteuer, Wasser, Entwässerung, Heizung, Warmwasser, verbundene Anlagen, Aufzug, Straßenreinigung/Müll, Gebäudereinigung/Ungeziefer, Gartenpflege, Beleuchtung (Allgemeinstrom), Schornsteinfeger, Sach-/Haftpflichtversicherung, Hauswart, Antenne/Breitband (Umlage seit 1.7.2024 nur noch eingeschränkt), Wäschepflege, Sonstige (vertraglich benannt).
- **Umlageschlüssel** je Kostenart und Objekt: Wohnfläche, Personen (zeitanteilig), Einheiten, Verbrauch (Zähler), direkte Zuordnung.
- **Zeitanteilig** nach Tagen bei Mieterwechsel. **Leerstand trägt der Vermieter.**
- **Nicht umlagefähige Kosten** (Verwaltung, Instandhaltung) werden erfasst, aber nicht umgelegt (sie gehen in die Steuer).
- **Abrechnungsinhalt:** Gesamtkosten je Kostenart, Verteilerschlüssel mit Erläuterung, Anteil des Mieters, Vorauszahlungen, Saldo. Dazu der **§35a-EStG-Ausweis** (Arbeitskostenanteile haushaltsnaher Dienstleistungen/Handwerker).
- **Frist:** 12 Monate nach Ende des Abrechnungszeitraums (§556 Abs. 3). Danach sind Nachforderungen ausgeschlossen, daher Erinnerungen.
- Nach der Abrechnung: **Vorschlag zur Anpassung der Vorauszahlung** (§560 Abs. 4).
- **Plausibilitätsprüfung** vor dem Versand: Summe der Anteile plus Leerstand = Gesamtkosten, Zähler vollständig, Zeiträume lückenlos.

## Heizkostenabrechnung (HeizkostenV) & CO₂-Kosten (CO₂KostAufG)

- **Verteilung Heizung:** 50–70 % nach Verbrauch (Wärmemengenzähler), Rest nach (Heiz-)Fläche. Satz je Objekt konfigurierbar.
- **Warmwasser (§9):** Aufteilung des Brennstoffverbrauchs der verbundenen Anlage in Heizung und Warmwasser, vorrangig über einen Wärmezähler im WW-Kreis. Sonst die Formel nach §9 Abs. 2 (2,5 kWh/(m³·K) × V × (t_w − 10 °C)), Verteilung nach WW-Verbrauch (Wasserzähler) und Fläche.
- **Kosten:** Brennstoff (Anfangsbestand + Lieferungen − Endbestand bei Öl), Betriebsstrom, Wartung, Schornsteinfeger/Immissionsmessung, Zählermiete/Eichung.
- **CO₂-Stufenmodell:** kg CO₂ pro m² Wohnfläche und Jahr (aus der Brennstoffrechnung) → 10 Stufen → Vermieteranteil 0–95 %. Der Vermieteranteil wird von den Mieter-Heizkosten abgezogen und gesondert ausgewiesen. Stufentabelle versioniert hinterlegen.
- **UVI (§6a)** entfällt, da die Zähler nicht fernablesbar sind. Bei Umrüstung auf Funkzähler wird das Pflicht.

> ❓ **Offen:** Gibt es einen separaten Wärmezähler für die Warmwasserbereitung? Werden Warm- und Kaltwasser je Wohnung getrennt gezählt? Das wird in Issue „Heizkostenabrechnung“ geklärt.

## Finanzen

- **Sollstellungen** monatlich aus den Mietbestandteilen. **Offene Posten** je Mieter.
- **Bankimport:** CSV (bankspezifische Profile), CAMT.053, MT940 und **FinTS/HBCI** (Abruf mit TAN/Decoupled).
- **Zuordnung:** Benutzerregeln (Gegen-IBAN, Name, Verwendungszweck → Kostenart/Objekt, Mietverhältnis oder „privat“) gelten vor der automatischen Bewertung (IBAN, Name, Betrag, Verwendungszweck mit Wohnung und Monat) → Vorschlag mit Begründung → Bestätigung, Änderung oder Aufteilung in der Eingangsliste „Umsätze zuordnen“.
  - **Eingänge:** Teilzahlungen bleiben offen, Überzahlungen werden Guthaben auf dem Mieterkonto, Sammelzahlungen für mehrere Monate gleichen die Forderungen der Reihe nach aus (oder bestimmte Forderungen gezielt).
  - **Ausgaben:** Umsatz → Kostenposition (Kostenart, Objekt, Leistungszeitraum, umlagefähig ja/nein) → Beleg (vorhandenen verknüpfen oder neu anlegen; „Zahlungen ohne Beleg“ zeigt, wo die Datei noch fehlt). Ein Umsatz lässt sich auf mehrere Kostenarten aufteilen; die Teile ergeben immer genau den Umsatzbetrag.
  - **Rückgängig machen** ist jederzeit möglich (Umsatz wird wieder offen, abgeleitete Zahlungen/Positionen entfallen); alles steht im Änderungsprotokoll.
- **Mahnwesen halbautomatisch:** ein täglicher Job erkennt Rückstände je Mietverhältnis und **schlägt** die nächste Stufe vor (Zahlungserinnerung → 1. Mahnung → 2. Mahnung); Brief und E-Mail entstehen erst **nach Freigabe**. Eine Zahlung vor der Freigabe entfernt den Vorschlag. Karenztage, Wartezeiten, Zahlungsfrist, Mindestbetrag, Mahngebühren (nur tatsächliche Kosten) und Verzugszinsen (§ 288 Abs. 1 BGB: Basiszinssatz, von Hand gepflegt, + 5 Prozentpunkte) sind einstellbar. Je Mietverhältnis lässt sich eine **Mahnsperre** setzen (z. B. Ratenvereinbarung). Weist auf eine Kündigungsrelevanz hin (§ 543 Abs. 2 Nr. 3 BGB: Rückstand von zwei Monatsmieten bzw. über zwei Termine mehr als eine Monatsmiete), ohne zu automatisieren. Die Texte sind eine Basisvorlage, die rechtlich geprüft werden muss.
- **Belege:** Upload als PDF/Foto, **OCR/KI-Auslesung** (konfigurierbarer Provider: Keiner (Standard), Claude API oder lokal über Ollama; nur als Vorschlag, den der Nutzer bestätigt; Tesseract ist (noch) nicht angebunden), Zuordnung zu Kostenart/Objekt/Zeitraum, Verknüpfung mit dem Bankumsatz. Belege sind unveränderlich archiviert (SHA-256), Löschung nur logisch.

## Steuer

- **Anlage V** je Objekt und Jahr: Mieteinnahmen, vereinnahmte Umlagen, Werbungskosten (Schuldzinsen aus Tilgungsplan, AfA, Erhaltungsaufwand sofort oder verteilt §82b EStDV, Grundsteuer, Versicherung, Verwaltung …). Zeilennummern sind **je Steuerjahr** konfigurierbar. Zuflussprinzip (§11 EStG, 10-Tage-Regel).
- **AfA:** Bemessungsgrundlage (Kaufpreis minus Grund und Boden + Nebenkosten), Satz 2 / 2,5 / 3 %, zeitanteilig im Anschaffungsjahr.
- **Export:** PDF/CSV-Ausfüllhilfe zeilengenau, **DATEV-Buchungsstapel (EXTF)** mit konfigurierbarem Kontenmapping.
- **Später:** ELSTER-Direktübermittlung via ERiC (Hersteller-ID nötig).

## Dokumente (PDF aus Vorlagen)

NK-/Heizkostenabrechnung, Mietvertrag, Zimmervertrag (WG), Mieterhöhung (Vergleichsmiete/Index), Staffelinfo, Pauschalenanpassung, Zahlungserinnerung/Mahnung, Übergabeprotokoll Ein-/Auszug (mit Zählerständen und Fotos), Kautionsquittung. Vorlagen sind editierbar, mit Platzhaltern und versioniert.

## Kommunikation & Fristen

- **E-Mail (SMTP)** für Dokumentversand, Magic Links und Benachrichtigungen.
- **Fristen-Erinnerungen:** NK-Abrechnungsfrist, Vertragsende/Kündigungsfristen, Index-/Staffeltermine, Wartezeit Mieterhöhung, Rauchmelderwartung, Wartung Heizung, Schornsteinfeger, Eichfristen der Zähler (Wasser 6 J., Wärme 5 J.), Versicherungen.
- **Mieterportal:** Dokumente einsehen, Schadensmeldung/Tickets (mit Fotos, Status, Zuweisung an Hausmeister/Handwerker), Nachrichten/Aushänge.
- **Zählerablesung** macht der Admin mobil (responsive Erfassungsmaske mit Foto).

## Datenschutz (DSGVO)

Umsetzung in MietHahn: Auskunft je Person als JSON-Download (Personenakte → „DSGVO-Auskunft“), Zugriffsprotokoll beim Öffnen von Personenakten, Vorschlagsliste unter *Stammdaten → Datenschutz* (10 Jahre nach Ende des letzten Vertrags, Personen ohne Vertrag nach 12 Monaten), Anonymisierung entfernt die Daten endgültig, auch aus dem Änderungsprotokoll. Gelöscht wird nie automatisch.

Verarbeitungsverzeichnis-Vorlage, Datenexport je Person (Auskunft Art. 15), Löschkonzept (Fristen nach Vertragsende, Aufbewahrungspflichten beachten), Audit-Log für Zugriffe und Änderungen, verschlüsselte Backups.
