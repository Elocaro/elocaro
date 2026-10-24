# Digitale Unterschrift für Mietverträge

MietHahn bietet eine **einfache elektronische Unterschrift**: Mieter (und auf Wunsch der Vermieter) lesen den Vertragsentwurf über einen persönlichen Link und unterschreiben am Bildschirm. Dieses Dokument beschreibt Ablauf, Nachweise und rechtliche Grenzen. Es ist eine Einschätzung für den Betrieb und **keine Rechtsberatung**; vor dem breiten Einsatz sollte sie juristisch geprüft werden (Issue #86).

## Ablauf

1. Vertrag → Vertragsdokument: Entwurf als PDF erstellen.
2. „Zur Unterschrift senden“: Jeder Mieter bekommt einen Link per E-Mail (14 Tage gültig). Mieter ohne E-Mail-Adresse und der Vermieter (Option „Ich unterschreibe auch“) erhalten den Link zur Weitergabe, er wird einmalig angezeigt. „Neuer Link“ ersetzt einen verlorenen.
3. Der Unterzeichner liest das PDF, gibt seinen vollständigen Namen ein, bestätigt das Lesen und kann zusätzlich seine Unterschrift zeichnen.
4. Haben alle unterschrieben, legt MietHahn den Vertrag mit einer **Protokollseite** als unterschrieben bei den Dokumenten des Vertrags ab (im Mieterportal freigegeben). Schlägt das Ablegen fehl (z. B. PDF-Dienst nicht erreichbar), bleiben die Unterschriften erhalten; „Vertrag jetzt ablegen“ holt es nach.

## Was festgehalten wird

- eingegebener Name und optional die gezeichnete Unterschrift (PNG)
- Zeitpunkt und IP-Adresse des Zugriffs, Browserkennung
- SHA-256-Prüfsumme des Dokuments, das der Unterzeichner gesehen hat; alle Unterschriften müssen dieselbe Prüfsumme tragen, sonst wird nicht abgelegt
- Das Token im Link wird nur als Hash gespeichert und ist für jeden Unterzeichner einzeln.

## Rechtliche Einschätzung

- **Keine qualifizierte elektronische Signatur.** Die Unterschrift erfüllt weder die Schriftform des § 126 BGB (eigenhändige Unterschrift) noch § 126a BGB (qualifizierte elektronische Signatur), sondern allenfalls die **Textform** (§ 126b BGB).
- **Unbefristeter Wohnraummietvertrag:** Er ist formfrei (§ 550 BGB gilt nur für Verträge über ein Jahr hinaus). Die digitale Unterschrift dient hier als **Nachweis** des Vertragsschlusses und des Inhalts. Der Beweiswert ist die freie richterliche Beweiswürdigung (§ 286 ZPO); Protokoll, Prüfsumme und E-Mail-Zustellung sprechen für den Unterzeichner.
- **Befristeter Vertrag über ein Jahr:** Ohne Schriftform gilt der Vertrag als auf unbestimmte Zeit geschlossen (§ 550 Satz 1 BGB); die Befristung ist also unwirksam. Hierfür eine **eigenhändige Unterschrift auf Papier** verwenden (oder eine qualifizierte Signatur, die MietHahn nicht bietet).
- **Vertragsklauseln:** Enthält der Vertrag eine Schriftformklausel (z. B. für Änderungen), gilt diese auch für die digitale Fassung. Die Standardvorlagen enthalten keine.
- **Kündigungen** des Vermieters verlangen weiterhin die Schriftform (§ 568 BGB); sie sind nicht Gegenstand dieser Funktion.
- **Datenschutz:** IP-Adresse und Browserkennung werden zur Beweissicherung gespeichert (Art. 6 Abs. 1 lit. f DSGVO); darüber ist in der Datenschutzinformation zu informieren.

## Technik

- `IContractSignatures` (`Infrastructure/Leases/ContractSignatures.cs`), Entität `SignatureRequest` (Tabelle `signature_requests`, ohne Änderungsprotokoll: sie ist selbst der Nachweis).
- Öffentliche Seite `/unterschrift/{token}` (statisches Formular, funktioniert ohne JavaScript; `wwwroot/js/contract-signature.js` ergänzt das Zeichenfeld), PDF unter `/unterschrift/{token}/dokument`.
- Protokollseite: PDF-Vorlage `Pdf/Templates/signature-protocol.sbn`, zusammengefügt mit dem Entwurf über Gotenberg (`/forms/pdfengines/merge`).
- Mailvorlage `signature-request.sbn`.
