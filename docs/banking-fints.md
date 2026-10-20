# Banking: FinTS-Direktabruf & Produktregistrierung

Gehört zu Issue „FinTS/HBCI-Direktabruf der Kontoumsätze“ (Epic M2).

## Warum eine Produktregistrierung?

Seit 2019 akzeptieren deutsche Banken FinTS-Verbindungen nur noch von **registrierten Kundenprodukten**. Jede Software, die per FinTS mit einer Bank spricht, muss beim Verbindungsaufbau (Dialoginitialisierung, Segment `HKVVB`, Feld „Produktbezeichnung“) eine **Produktregistrierungsnummer** mitschicken. Ohne sie lehnen die meisten Banken die Verbindung ab, oder sie funktioniert nur eingeschränkt bzw. mit Warnhinweis.

Herausgeber ist **Die Deutsche Kreditwirtschaft (DK)**. Die Registrierung ist für Entwickler **kostenlos**, auch für private oder Open-Source-Projekte.

> Die libfintx-Beispiel-IDs oder fremde IDs dürfen nicht verwendet werden: Die Nummer ist an Hersteller und Produkt gebunden.

## Ablauf

1. **Antrag stellen** auf <https://www.fints.org/de/hersteller/produktregistrierung>.
   - Angaben (Stand der Recherche, auf der Seite prüfen): Hersteller (Name/Anschrift, als Privatperson der eigene Name), Ansprechpartner + E-Mail, Produktname („MietHahn“), Produktart/Einsatzzweck (Kontoumsatzabruf für die private Immobilienverwaltung, Self-hosted), unterstützte FinTS-Version (3.0) und PIN/TAN-Verfahren.
   - Nutzungsbedingungen der DK akzeptieren.
2. **Nummer erhalten**: Laut Erfahrungsberichten kommt sie per E-Mail nach wenigen Tagen bis ca. **10–15 Werktagen**.
3. **Verteilung an die Banken abwarten**: Die DK verteilt die Registrierung an die Bankrechenzentren (Sparkassen/Finanz Informatik, Atruvia für Volks- und Raiffeisenbanken, Privatbanken). Das kann **0 bis über 8 Wochen** dauern. Bis dahin können Abrufe mit „Produkt nicht registriert“ abgelehnt werden.
4. **In MietHahn eintragen**: Die Nummer kommt als Secret in `deploy/.env` (nicht ins Repo, `chmod 600`), danach den Dienst neu starten (`docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d`):
   ```env
   FINTS_PRODUCT_ID=<Registrierungsnummer>
   # optional, höchstens 5 Zeichen, Standard 1.0
   FINTS_PRODUCT_VERSION=1.0
   ```
   `docker-compose.yml` reicht beides als `FinTs__ProductId` und `FinTs__ProductVersion` an die App weiter (Konfigurationsabschnitt `FinTs`). Die App sendet sie bei jeder Dialoginitialisierung (libfintx: `FinTsGlobals.ProductId` und `Version`). Ohne Nummer zeigt die Kontoseite nur den Hinweis, dass der Direktabruf noch nicht freigeschaltet ist; der Datei-Import funktioniert unabhängig davon.
5. **Bei Versionssprüngen** prüfen, ob die DK eine Meldung neuer Hauptversionen verlangt (Bedingungen beim Antrag lesen).

## Bedienung

1. **Konto bearbeiten** (Bank → Konten → Konto → „Angaben“ → „Direktabruf per FinTS“): FinTS-URL (https), Bankleitzahl (8 Ziffern) und Benutzerkennung eintragen. Alle drei oder keins.
2. Auf der Kontoseite unter **„Per FinTS abrufen“** die PIN und den Zeitraum eingeben (Standard: die letzten 90 Tage) und „Umsätze abrufen“ wählen.
3. Verlangt die Bank eine **TAN**, erscheint ihre Anweisung mit einem Eingabefeld. Bei **Decoupled**-Verfahren (pushTAN, SecureGo plus, BestSign) erst in der Banking-App freigeben und dann „Freigabe erteilt“ wählen. Verfahren mit Bild (photoTAN, QR) werden noch nicht angezeigt; dafür bitte ein Decoupled-Verfahren nutzen oder die Datei importieren.
4. Die Umsätze erscheinen in der **Import-Vorschau** (Format „FinTS-Abruf“) und werden erst nach Bestätigung übernommen. Schon vorhandene Umsätze werden wie beim Datei-Import übersprungen (Duplikaterkennung), ein Abruf mit überlappendem Zeitraum ist also unkritisch.

> **Vor dem ersten produktiven Abruf in der Vorschau prüfen**, ob Eingänge positiv und Abbuchungen negativ sind und ob Gegenseite, IBAN und Verwendungszweck stimmen. Der Abruf ist ohne echte Bankverbindung nur gegen Beispieldaten getestet.

## Bankseitige Voraussetzungen (je Konto prüfen)

| Punkt | Was prüfen |
|---|---|
| FinTS freigeschaltet | Im Onlinebanking bzw. bei der Bank. Manche Banken verlangen eine Freischaltung von „Banking mit Finanzsoftware“ |
| FinTS-URL & BLZ | Je Bank unterschiedlich (z. B. Sparkassen: `https://banking-<rechenzentrum>.s-fints-pt-<x>.de/fints30`). Steht meist in der Hilfe der Bank oder in der FinTS-Institutsdatenbank |
| TAN-Verfahren | Bevorzugt **Decoupled** (pushTAN, SecureGo plus, BestSign): Freigabe in der App, MietHahn pollt. Alternativ chipTAN/photoTAN mit TAN-Eingabe |
| SCA-Intervall | PSD2: Umsatzabruf ohne TAN meist bis 90 Tage nach der letzten starken Authentifizierung. Danach TAN nötig → Erinnerung in MietHahn |
| Abrufformat | CAMT.052/.053 (bevorzugt) oder MT940. MietHahn nutzt dieselben Parser wie beim Datei-Import |

## Sicherheit

- **PIN wird nicht gespeichert.** Sie wird pro Abruf eingegeben und nur im Arbeitsspeicher gehalten. In Logs wird sie maskiert (Test sichert das ab).
- Nach **einem** Fehlversuch bricht MietHahn ab, um eine Sperre nach 3 Fehlversuchen zu vermeiden.
- Die Benutzerkennung (Login-Name/Legitimations-ID) wird mit ASP.NET DataProtection verschlüsselt gespeichert (Zweck `MietHahn.FinTs.UserId`). Die Schlüssel liegen in der Datenbank und gehören damit zur Datensicherung; ohne sie ist die Kennung nach einer Wiederherstellung unlesbar und muss neu eingetragen werden.
- libfintx bekommt kein Logging der Anwendung (`NullLoggerFactory`), damit weder PIN noch TAN in Logs landen. Bei Fehlern wird nur der Ausnahmetyp protokolliert.
- Ein Abruf dauert höchstens 5 Minuten (`FinTs__TanTimeoutSeconds`), danach bricht er ab. Je Konto läuft nur ein Abruf gleichzeitig.
- FinTS-Kommunikation läuft nur über HTTPS zur hinterlegten Bank-URL, Zertifikate werden geprüft.

## Lizenz libfintx

`libfintx.FinTS` steht unter **LGPL-3.0**. Bei unveränderter Nutzung als NuGet-Paket (dynamisches Linken) ist das mit MietHahn vereinbar. Pflichten: Lizenzhinweis und Copyright in `THIRD-PARTY-NOTICES.md`, Hinweis auf den Bezug des Quellcodes. Eigene Änderungen an libfintx müssten unter LGPL veröffentlicht werden (vermeiden, stattdessen Upstream-PR).

## Fallback

Solange die Registrierung oder die Bankfreischaltung fehlt, wird der **Datei-Import** genutzt (CAMT/MT940/CSV-Export aus dem Onlinebanking). Er ist von FinTS unabhängig und immer verfügbar.

## Quellen

- [fints.org: Produktregistrierung](https://www.fints.org/de/hersteller/produktregistrierung)
- [Volksbank Allgäu-Oberschwaben: Hinweise für Nutzer von FinTS (PDF)](https://www.volksbank-allgaeu-oberschwaben.de/content/dam/f0100-0/dateien_vbao/zahlungsverkehr/Download/Hinweise%20f%C3%BCr%20Nutzer%20von%20FINTS.pdf)
- [fints (Dart): Hinweise zur Registrierungsdauer](https://pub.dev/packages/fints)
- [mathiasarens/fints: Hinweise zur Verteilung an die Banken](https://github.com/mathiasarens/fints)
- [libfintx.FinTS auf NuGet (LGPL-3.0)](https://www.nuget.org/packages/libfintx.fints)
