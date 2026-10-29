# Konzept: Rechtsstände-Abo

**Stand:** Konzept, nicht gebaut. Voraussetzungen siehe unten. Bezug: Issues #88 und #99.

## Idee

Elocaro stützt sich auf Werte aus Gesetz und Statistik (Seite *Einstellungen → Rechtsstände*): Basiszinssatz, Verbraucherpreisindex, CO₂-Stufentabelle, Gradtagszahlen und die Standardvorlagen. Heute pflegt der Betreiber der Installation sie von Hand. Das Abo liefert sie als **geprüfte, signierte Aktualisierung**: Die Installation erfährt, dass sich etwas ändert, prüft die Signatur und übernimmt die Werte mit einem Klick.

Verkauft wird die **Verantwortung und Pflege**, nicht der Zugriff auf öffentliche Zahlen. Basiszinssatz und Index sind öffentlich; wer sie selbst eintragen will, kann das weiter kostenlos.

## Was kostenlos bleibt und was im Abo liegt (Vorschlag)

| Inhalt | Frei (von Hand) | Abo |
|---|---|---|
| Basiszinssatz, VPI | ja, wie heute | als Paket zum 1.1./1.7. bzw. monatlich, mit Quelle und Prüfdatum |
| CO₂-Stufentabelle, Gradtagszahlen | mitgeliefert mit Stand-Hinweis | geprüfte Fassung mit Prüfvermerk und Änderungsprotokoll |
| Standardvorlagen (Mietvertrag, Mieterhöhung, Mahnung …) | mitgeliefert, „vor dem Einsatz prüfen“ | rechtlich geprüfte Fassungen mit Version und Prüfdatum, bei Rechtsprechungsänderungen aktualisiert |
| Hinweis bei Änderungen | Dashboard-Hinweis bei veralteten Werten (vorhanden) | Hinweis mit konkretem Änderungstext |

Ohne Abo bleibt Elocaro voll nutzbar; es gibt keine Sperre.

## Paketformat

Eine JSON-Datei je Ausgabe, z. B. `rechtsstaende-2026-2.json`:

```json
{
  "schema": 1,
  "ausgabe": "2026-2",
  "ausgestellt": "2026-07-01",
  "werte": {
    "basiszinssatz": { "prozent": 2.27, "gueltigAb": "2026-07-01", "quelle": "Deutsche Bundesbank" },
    "vpi": [ { "monat": "2026-05", "wert": 123.4, "quelle": "Destatis 61111-0002" } ],
    "co2Stufen": { "version": "CO2KostAufG-2023", "geprueft": "2026-06-20", "pruefer": "…", "stufen": [] },
    "gradtage": { "version": "…", "geprueft": "…", "promille": [170, 145, 120, 75, 30, 10, 0, 0, 25, 75, 140, 210] }
  },
  "vorlagen": [ { "name": "Mietvertrag Wohnraum", "version": 4, "geprueft": "2026-06-20", "inhalt": "…" } ],
  "hinweise": [ "Basiszinssatz zum 1.7.2026 angepasst." ]
}
```

- **Signatur:** abgetrennte Ed25519-Signatur (`….json.sig`). Der öffentliche Schlüssel ist im Programm eingebaut; der private liegt nur beim Herausgeber. Es gibt keine Rückfrage an einen Server.
- **Verteilung:** Zwei Wege, beide ohne Kundenkennung im Abruf: (a) Datei herunterladen und unter *Rechtsstände* hochladen, (b) optionaler Abruf einer öffentlichen URL durch die Installation (standardmäßig aus; Datenschutz: nur die Datei wird geholt, nichts wird gesendet).
- **Berechtigung:** Das Abo regelt, **wer das Paket bekommt** (Zugang zur Download-Adresse oder verschlüsselte Datei mit Kundenschlüssel), nicht ob das Programm läuft. Ein umgangenes Paket schadet nur dem Herausgeber, nicht dem Kunden.
- **Übernahme in der App:** Prüfen (Signatur, Ausgabe neuer als die installierte, Schema bekannt), Vorschau („Basiszinssatz 2,27 % ab 01.07.2026, 3 Vorlagen aktualisiert“), dann Übernehmen. Alte Fassungen bleiben erhalten (Abrechnungen verwenden den Stand ihres Zeitraums). Im Rechenprotokoll steht die verwendete Ausgabe.

## Was dafür gebaut werden müsste

1. **Prüfvermerk je Tabelle** im System (wer, wann, Quelle) statt des festen `Verified`-Flags im Code (#88).
2. **Import eines signierten Pakets** (Signaturprüfung, Vorschau, Übernahme, Protokoll) auf der Seite *Rechtsstände*.
3. **Versionierte Vorlagen mit Prüfdatum** (Vorlagenversion gibt es; Prüfdatum und Herkunft „Paket“ fehlen).
4. **Verwendeten Stand ins Rechenprotokoll** der Abrechnung schreiben.
5. **Herausgeber-Werkzeug** (getrennt, nicht Teil der App): Pakete aus gepflegten Quellen erzeugen und signieren.
6. Tests: manipulierte Datei, veraltete Ausgabe, unbekanntes Schema, Rollback bei Fehler.

Aufwand grob: 1–2 Wochen für 1 bis 5, dazu laufend etwa ein halber Tag je Halbjahr für die Pflege (ohne die rechtliche Prüfung der Texte).

## Voraussetzungen vor dem ersten Verkauf

- **Rechtliche Prüfung der Vorlagen** und Klärung, ob ein bezahlter Rechtstext-Dienst unter das Rechtsdienstleistungsgesetz fällt (Issue #86). Ohne geprüfte Inhalte hat das Abo keinen Wert.
- **Die Platzhalter ersetzen:** Gradtagszahlen und CO₂-Tabelle müssen geprüft sein (siehe `docs/vertriebsplan.md`).
- **AGB und Haftungsbegrenzung**, Gewerbe und Umsatzsteuer (Issues #91, #101).
- **Zahlungsweg:** Rechnung per Überweisung reicht für wenige Kunden; ab etwa 20 Kunden lohnt ein Zahlungsdienstleister, der die Umsatzsteuer übernimmt.
- **Preis:** Richtwert 20–40 € pro Jahr und Installation (Annahme; gegen den eigenen Aufwand und die Zahlungsbereitschaft prüfen, zum Vergleich kosten Komplettlösungen 10–40 € im Monat).

## Offene Fragen

- Soll der Herausgeber eine Person oder eine Firma sein (Haftung, Steuer)?
- Wie lange gilt eine zugesagte Aktualisierung bei einer Gesetzesänderung (Reaktionszeit)?
- Werden Aktualisierungen für bereits veröffentlichte Ausgaben nachgeliefert oder nur in der aktuellen?
