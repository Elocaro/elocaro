# Feature-Sponsoring

Wer eine Funktion früher oder überhaupt haben möchte, kann ihre Umsetzung finanzieren. Der Code bleibt unter der AGPL-3.0 und steht danach allen zur Verfügung.

## Ablauf

1. **Issue anlegen** mit der Vorlage „Funktion finanzieren“ (oder ein bestehendes Issue wählen). Beschrieben wird die Aufgabe, nicht die Lösung: Was soll möglich sein, wie wird es heute gelöst, wie groß ist der Bestand?
2. **Einschätzung:** Der Betreiber antwortet mit Aufwand (Tage), einem Festpreis und dem Umfang, der damit abgedeckt ist. Ist die Funktion für das Projekt nicht passend (z. B. widerspricht sie dem Datenschutzversprechen), steht das dort ebenfalls.
3. **Zusage:** Bei Einigung wird das Label `sponsoring` gesetzt und das Issue mit dem Preis und dem Zahlungsweg bestätigt. Gezahlt wird nach Abschluss des vereinbarten Umfangs; bei größeren Funktionen in Teilen (Meilensteine).
4. **Umsetzung** wie jede andere Funktion: mit Tests, Dokumentation unter `docs/` und Eintrag im Changelog.
5. **Abnahme:** Der Auftraggeber prüft die Funktion mit eigenen Daten (oder den Beispieldaten). Fehler im vereinbarten Umfang werden ohne Aufpreis behoben.

## Regeln

- **Kein Anspruch auf Annahme.** Der Betreiber darf Anfragen ablehnen oder umformulieren.
- **Mehrere Auftraggeber:** Dieselbe Funktion können mehrere gemeinsam finanzieren (Beträge im Issue offenlegen, Gesamtpreis teilen).
- **Offener Code:** Die Funktion wird Teil des Hauptprojekts, nicht eines privaten Zweigs. Eine Sonderversion für einen einzelnen Kunden gibt es nicht.
- **Keine Garantien über Recht und Steuer:** Funktionen mit Rechtsbezug (Abrechnung, Mieterhöhung, Anlage V) werden nach bestem Wissen gebaut und mit Quellen belegt; die rechtliche Prüfung bleibt Sache des Auftraggebers oder seines Beraters.
- **Datenschutz:** Echte Mieterdaten gehören nicht in Issues oder Anhänge.

## Preisrahmen (Vorschlag, noch nicht beschlossen)

| Größe | Beispiel | Aufwand | Richtwert (netto) |
|---|---|---|---|
| klein | Spalte im Import, Textbaustein, Filter, Export-Feld | bis 1 Tag | 150–300 € |
| mittel | neue Auswertung, neue Vorlage mit Prüfung, Portal-Ansicht | 2–5 Tage | 400–1.200 € |
| groß | neues Fachmodul (z. B. Kautionskonto mit Zinsberechnung, weitere Bankverfahren) | 1–3 Wochen | 1.500–4.000 € |

Die Richtwerte sind Annahmen und gehören vor dem Start an den eigenen Stundensatz und an den Markt angepasst. Wer gleich ein Angebot will, schreibt das Issue.

## Voraussetzungen, bevor Geld fließt

- Konto und Rechnungsweg stehen (siehe [Unterstützen](foerderung.md), Hinweise für den Betreiber).
- AGB mit Haftungsbegrenzung liegen vor (Issue #101).
- Die Funktionsliste ist öffentlich in den Issues; geplante Funktionen werden nicht versprochen.
