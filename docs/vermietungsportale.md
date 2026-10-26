# Anbindung von Vermietungsportalen (WG-Gesucht u. a.)

Einschätzung zu Issue #115 („Anfragen/Daten in/aus WG-Gesucht.de, weitere Portale“). Stand: Oktober 2026. Das Papier hält fest, was heute machbar ist und was **vor** einer Umsetzung zu klären bleibt. Es wurde kein Code geändert.

## Was gewünscht ist

- Freie Zimmer und Wohnungen aus MietHahn auf Portalen veröffentlichen (Daten **aus** MietHahn).
- Anfragen von Interessenten aus den Portalen in MietHahn führen (Daten **in** MietHahn).
- Später weitere Portale (ImmoScout24, Immowelt, Kleinanzeigen).

## Was geprüft ist, und was nicht

- Eine öffentlich dokumentierte Schnittstelle für Privatvermieter ist für WG-Gesucht **nicht belegt**. Eine Websuche am 26.10.2026 lieferte dazu nichts. Eine Aussage über Partner- oder Gewerbezugänge bleibt offen und muss beim Anbieter erfragt werden.
- Anzeigen per Web-Automatisierung (Scraping, Bots) einzustellen oder Nachrichten auszulesen, widerspricht üblicherweise den Nutzungsbedingungen und kann zur Kontosperre führen. MietHahn baut das **nicht**.
- Große Portale (ImmoScout24, Immowelt) haben Partner-Schnittstellen für gewerbliche Anbieter mit Vertrag und Kosten. Für Selbsthoster ohne Partnerstatus sind sie nicht ohne Weiteres nutzbar.

## Was ohne Portalschnittstelle sinnvoll geht

1. **Inserat vorbereiten (Daten aus MietHahn).** Für ein freies Zimmer oder eine freie Wohnung erzeugt MietHahn einen Anzeigentext aus den Stammdaten: Lage, Fläche, Zimmerfläche, Ausstattung und Möblierungsliste, Warmmiete (Inklusivmiete bzw. Kalt- plus Nebenkosten), Kaution, Energieausweis-Angaben (Pflicht in der Anzeige nach EnEV/GEG), Verfügbarkeit. Der Text lässt sich kopieren und ins Portal einfügen. Das ist reine Textausgabe ohne Portalzugang.
2. **Anfragen erfassen (Daten in MietHahn).** Portale schicken Anfragen per E-Mail an den Vermieter. Eine Eingangsmailbox gibt es schon für Belege (IMAP, Absenderfreigabe). Dasselbe Muster kann Anfragen als „Interessenten“ ablegen: Name, Nachricht, Datum, Quelle, verknüpftes Zimmer, Status (neu, Besichtigung, Absage, Zusage). Der Mailtext bleibt unverändert, das Auslesen einzelner Felder ist optional.
3. **Vom Interessenten zum Vertrag.** Bei „Zusage“ legt MietHahn mit einem Klick die Person an und startet den Vertragsassistenten für das gewählte Zimmer (Zimmervertrag, Kaution, Übergabeprotokoll).

## Daten- und Rechtsfragen vor dem Bau

- **Datenschutz:** Interessenten sind Personen mit Kontaktdaten. Es braucht Löschfristen (nach Absage zeitnah), einen Hinweis in der Datenschutzinformation und die Einschränkung auf die erforderlichen Angaben. Selbstauskünfte und Gehaltsnachweise sind keine Pflichtfelder.
- **AGG:** Auswahlgründe nicht erfassen, die auf geschützte Merkmale zielen. Nur ein Freitext „Anmerkung“ und der Status.
- **Anzeigenpflichtangaben:** Energieausweiswerte (Art, Kennwert, Energieträger, Baujahr, Effizienzklasse) gehören in die Anzeige; die Einheit bringt sie mit.

## Vorschlag zur Umsetzung

| Schritt | Inhalt | Aufwand |
|---|---|---|
| 1 | Anzeigentext zum Kopieren aus Zimmer/Wohnung (Vorlage in „Dokumente & Schreiben“) | klein |
| 2 | Interessenten-Liste mit Status, manuell oder aus der Mailbox | mittel |
| 3 | Übernahme eines Interessenten in einen Vertragsentwurf | klein |
| 4 | Portal-Schnittstellen, falls ein Anbieter eine freigegebene Schnittstelle für Selbsthoster nennt | offen |

Vor Schritt 4 braucht es eine schriftliche Auskunft des Portals (Zugang, Nutzungsbedingungen, Kosten, Missbrauchsregeln); ohne sie bleibt MietHahn bei den Schritten 1 bis 3.

## Offene Fragen an den Projektinhaber

- Welche Portale nutzen Sie tatsächlich (nur WG-Gesucht)?
- Reicht der Anzeigentext zum Kopieren als erster Schritt, oder ist die Interessentenverwaltung wichtiger?
- Sollen Anfragen per Weiterleitung in die vorhandene Mailbox kommen?
