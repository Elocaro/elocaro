# Bankanbindung: Alternativen zum FinTS-Direktabruf

Stand der Bewertung (Issue #107). Preise und Bedingungen der Anbieter ändern sich; vor einer Entscheidung auf den Seiten der Anbieter prüfen.

## Heute

- **CSV/CAMT/MT940-Import** aus dem Onlinebanking: immer verfügbar, unabhängig von Registrierung und Bank.
- **FinTS-Direktabruf** (libfintx): ohne Dritte, aber nur gegen Beispieldaten getestet; photoTAN/QR-Verfahren werden nicht angezeigt (Decoupled-Verfahren wie pushTAN, SecureGo plus, BestSign funktionieren vorgesehen). Bei PSD2 verlangt die Bank etwa alle 90 Tage wieder eine starke Authentifizierung; MietHahn zeigt dann die TAN-Abfrage.

## Aggregatoren (optionale Anbindung)

| Anbieter | Eignung | Anmerkung |
|---|---|---|
| finAPI | Marktführer in Deutschland, von Wettbewerbern genutzt | Vertrag und Kosten je Nutzer/Konto; Zielgruppe Unternehmen; Auftragsverarbeitung nötig |
| GoCardless Bank Account Data (früher Nordigen) | europaweit, Kostenstufe je verbundenem Konto | richtet sich an Unternehmen; Bedingungen für neue Kunden vorab klären |
| Tink, Yapily, Salt Edge | europaweit | Verträge für Unternehmen, Abrechnung je Verbindung |

**Bewertung:** Ein Aggregator würde den Abruf für Nutzer deutlich einfacher machen, passt aber nicht zum Versprechen „Daten bleiben auf dem eigenen Server“: Kontoumsätze liefen über einen Dritten (Auftragsverarbeitung, Standortfragen), jeder Betreiber bräuchte einen eigenen Vertrag, und es entstünden laufende Kosten je Konto. Für ein Nebenprojekt ohne Hosting-Angebot ist das nicht sinnvoll.

**Empfehlung:** Bei FinTS und CSV bleiben. Erst wenn ein gehostetes Angebot geplant ist (siehe `docs/vertriebsplan.md`), eine Aggregator-Anbindung als **optionales Modul** bewerten, mit eigenem Vertrag des jeweiligen Betreibers und klarer Datenschutzinformation.

## Offene Punkte (brauchen echte Konten)

- Test mit mindestens drei echten Konten (Sparkasse, Volks-/Raiffeisenbank, DKB/ING) und Ergebnisse in `docs/banking-fints.md`
- photoTAN/QR-Bild anzeigen (libfintx liefert Bilddaten in der Matrix-Code-Antwort; Anzeige lässt sich ohne echte Bank nicht prüfen)
