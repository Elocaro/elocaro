# Anwenderhandbuch

Dieses Handbuch ist für Vermieter, die Elocaro benutzen. Betrieb und Installation stehen in [betrieb.md](betrieb.md). Dieselben Anleitungen, dazu häufige Fragen und ein Glossar, stehen in der Anwendung selbst: Benutzermenü → **Hilfe** (`/verwaltung/hilfe`). Mieter finden ihre Hilfe im Portal unter `/portal/hilfe`.

## Erste Schritte

1. **Vermieterprofil** (Einstellungen → Vermieter): Name, Anschrift, Bankverbindung. Sie stehen auf allen Schreiben.
2. **Objekt und Einheiten** (Bestand → Objekte → Neu): das Haus, dann die Wohnungen mit Wohnfläche.
3. **Mieter und Vertrag** (Bestand → Verträge → Neu): Mieter, Mietbeginn, Kaltmiete, Betriebskostenvorauszahlung, Kaution.
4. **Zahlungen** (Bank): Konto anlegen, Umsätze importieren; Elocaro ordnet sie den Forderungen zu.
5. **Mieterzugang** (Personen → Mieter → Portalzugang): damit der Mieter Konto, Dokumente und Meldungen selbst sieht.

Der Einrichtungsassistent auf der Übersicht führt durch die ersten Schritte. Wer schon Daten in einer Tabelle hat, nutzt Einstellungen → Datenimport; zum Ausprobieren gibt es Beispieldaten (ebenda).

## Aufgaben

| Aufgabe | Wo | Kurz |
|---|---|---|
| Mieter anlegen | Bestand → Personen → Neu | Name, Anrede, Anschrift, E-Mail; danach im Vertrag wählen |
| Zahlung zuordnen | Geld → Bank → Zuordnen | Vorschlag bestätigen; mit Regeln geht es künftig automatisch. Barzahlungen im Mieterkonto des Vertrags |
| Beleg erfassen | Geld → Belege → Beleg erfassen | Rechnung hochladen, Lieferant, Datum und Brutto eintragen und gleich unter „Zuordnung“ Kostenart und Objekt wählen; bei mehreren Kostenarten den Betrag aufteilen. Die Anzeige unter der Zuordnung nennt, was noch fehlt |
| Nebenkosten abrechnen | Belege, dann Objekt → Abrechnung | Zeitraum, Kostenarten, Verteilung prüfen; Zugang beim Mieter binnen zwölf Monaten nach Ende des Zeitraums |
| Belege für Mieter freigeben | Abrechnung (Liste der Abrechnungen → Abrechnung öffnen) → Reiter „Belege für Mieter“ | Standard: nichts ist freigegeben. Haken setzen und „Auswahl speichern“; die Freigabe lässt sich jederzeit zurücknehmen. Belege können Daten Dritter enthalten, es wird nichts geschwärzt |
| Miete erhöhen | Vertrag → Vertrag → Mieterhöhung | Verfahren wählen; Fristen und Grenzen werden gerechnet, das Schreiben entsteht als PDF |
| Zählerstände | Objekt → Zähler | Ablese-Runde, CSV-Import oder Meldung der Mieter im Portal prüfen und übernehmen |
| Mieterwechsel | Vertrag → Ein- & Auszug → Mieterwechsel | Die Leiste oben zeigt den Stand: Kündigung, Auszug, Zählerstände, Übergabe, Kaution, Bescheinigung, Folgevertrag, Abrechnung |
| Bescheinigungen | Kommunikation → Dokumente → Schreiben an Mieter | Wohnungsgeberbestätigung, Mietbescheinigung, Mietschuldenfreiheit, Kautionsabrechnung |
| Notfallmappe erzeugen | Einstellungen → System → Notfallmappe | Verschlüsselte Datei für Angehörige: Mieter, Konten, offene Posten, Fristen und Ihre Hinweise. Passwort zweimal wählen und getrennt von der Datei aufbewahren; es gibt keine Wiederherstellung |

## So sieht es aus

Umsätze zuordnen (Geld → Bank → Zuordnen): Vorschläge mit Begründung, bestätigen oder ändern.

![Umsätze zuordnen](bilder/umsaetze-zuordnen.png)

Nebenkostenabrechnung (Bestand → Objekt → Nebenkosten → Abrechnungs-Assistent): Schritt für Schritt mit Prüfhinweisen. Wasser, Abwasser, Strom und Gas rechnet Elocaro nach Verbrauch und Tarif (Objekt → Nebenkosten → Versorgungsverträge): Preis je Einheit und Grundpreis eintragen, die Kosten ergeben sich aus den Zählerständen. Abschlagsrechnungen zählen nicht; die Jahresabrechnung des Versorgers erfassen Sie später als Beleg mit der „Art der Rechnung“ „Jahresabrechnung des Versorgers“, sie ersetzt die Berechnung.

![Abrechnungs-Assistent](bilder/abrechnungs-assistent.png)

**Prüfhinweise vor der Freigabe:** Neben den Plausibilitätsprüfungen (Sprünge gegenüber dem Vorjahr, fehlende Kostenarten, Vorauszahlungen) nennt der Assistent, was Mieter erfahrungsgemäß nachfragen: Kostenpositionen ohne Belegdatei und Positionen, die als nicht umlagefähig markiert sind und in der Abrechnung fehlen. Diese Hinweise sind Anregungen zum Prüfen, keine Rechtsaussage, und sperren die Freigabe nicht.

**Die Abrechnung im Mieterportal:** Sobald Sie eine Abrechnung an einen Mietvertrag versendet haben, sehen die Mieter dieses Vertrags sie im Portal unter **Abrechnungen** (auch auf der Startseite und bei den Dokumenten verlinkt). Balkendiagramme zeigen das Ergebnis (Anteil gegen Vorauszahlungen, mit Nachzahlung oder Guthaben samt Betrag), die Anteile je Kostenart und den Vergleich mit dem Vorjahr (Strich = Vorjahr). Die Seite erklärt jede Position in Klartext („1.240,00 € nach Wohnfläche: Ihr Anteil 248,00 €“), vergleicht die Anteile mit der Abrechnung des Vorjahres, die an denselben Vertrag ging, zeigt die von Ihnen freigegebenen Belege und bietet zu jeder Position „Frage zu dieser Position“ an. Die Frage öffnet eine neue Nachricht mit vorbelegtem Betreff; sie ersetzt keinen förmlichen Einwand, die Frist läuft weiter. Mieter sehen nur ihre eigene Abrechnung, nie Werte anderer Parteien. Eine neuere, noch nicht versendete Fassung bleibt verborgen. Im Abrechnungsschreiben steht ein QR-Code zum Portal, wenn unter `Application__BaseUrl` die öffentliche Adresse eingestellt ist und ein Mieter des Vertrags ein Portalkonto hat; der Code führt nur zur Anmeldeseite, er enthält keinen Zugangsschlüssel.

Auf dem Handy führt die untere Leiste zu Übersicht, Bestand, Geld und Kommunikation.

![Aufgaben auf dem Handy](bilder/handy-aufgaben.png)

## Notfallmappe

Was geschieht, wenn Sie ausfallen? Die Notfallmappe (Einstellungen → System → Notfallmappe, Recht *System*) ist **eine verschlüsselte Datei**, mit der Angehörige die Vermietung weiterführen können, auch wenn Elocaro oder der Server nicht mehr erreichbar sind.

**Erzeugen.** Tragen Sie unter „Ihre Hinweise“ ein, wen man anrufen soll und wo die Unterlagen liegen (dort **keine Passwörter**: der Text steht wie alle Daten unverschlüsselt in der Datenbank). Wählen Sie ein Passwort mit mindestens 12 Zeichen, geben Sie es zweimal ein und klicken Sie auf „Notfallmappe erzeugen“. Optional legen Sie die unterschriebenen Mietverträge der laufenden Mietverhältnisse bei (die Mappe darf insgesamt höchstens 100 MB enthalten). „Hinweise speichern“ sichert nur die Texte.

**Das Passwort wird nirgends gespeichert.** Wer es verliert, kann die Mappe nicht mehr öffnen; auch Elocaro kann das nicht. Bewahren Sie Datei und Passwort an **getrennten Orten** auf, etwa die Datei bei der Familie und das Passwort im Safe oder beim Notar.

**Öffnen.** Die Datei `elocaro-notfallmappe-JJJJ-MM-TT.html` öffnet sich per Doppelklick in einem aktuellen Browser (Firefox, Chrome, Edge, Safari), ohne Internet und ohne Elocaro. Nach Eingabe des Passworts erscheinen lesbare, druckbare Seiten:

- Erste Schritte: eine allgemeine Checkliste für die ersten 30 Tage (Orientierung, keine Rechtsberatung),
- Ihre Hinweise, Vermieterdaten, Objekte und Einheiten,
- laufende Mietverträge mit Mietern, Kontaktdaten, Miete und Kaution,
- offene Posten, Fristen der nächsten zwölf Monate und offene Aufgaben,
- Konten mit IBAN und Bank (nie Zugangsdaten, PINs oder Schlüssel),
- Dienstleister und Versicherungen aus den Belegen der letzten zwei Jahre (Name und Kostenart),
- Technik: Adresse von Elocaro, Stand der Datensicherung, Hinweise zu Wiederherstellung und Vollexport.

Die Mappe zeigt den **Stand beim Erzeugen**. Erzeugen Sie sie mindestens einmal im Jahr neu; die Startseite erinnert daran, wenn sie noch nie erzeugt wurde oder älter als zwölf Monate ist. Jede Erzeugung steht im Änderungsprotokoll (ohne Passwort).

## KI-Assistent nutzen (optional)

Wenn Ihr Betreiber die MCP-Schnittstelle eingeschaltet hat, können Sie einem KI-Assistenten (z. B. Claude oder GitHub Copilot) **lesenden** Zugriff geben und ihn fragen: „Welche Mieter sind im Rückstand?“ oder „Was steht diese Woche an?“. Unter *Konto & Sicherheit → Zugriffstoken für Integrationen* erstellen Sie dafür ein Token, wählen die Bereiche (Stammdaten, Finanzen) und kopieren es in den Assistenten. Der Assistent sieht nie mehr als Sie selbst und kann nichts ändern; Token lassen sich dort jederzeit widerrufen. Bedenken Sie, dass die gelesenen Daten an den Anbieter des Assistenten gehen. Die Einrichtung für die einzelnen Programme steht in [mcp.md](mcp.md).

## Begriffe

Sollstellung, Offener Posten, Saldovortrag, Umlageschlüssel und weitere Begriffe sind in der Hilfe der Anwendung (Abschnitt „Begriffe einfach erklärt“) beschrieben.

## Hinweis

Vorlagen, Berechnungen und Hinweise sind Arbeitshilfen und keine Rechts- oder Steuerberatung.
