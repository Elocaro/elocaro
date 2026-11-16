# Datenschutz und technische Maßnahmen (Entwurf)

Arbeitsgrundlage für Betreiber von Elocaro: Wer mit Elocaro Mieterdaten verarbeitet, ist **Verantwortlicher** im Sinne der DSGVO und braucht ein Verzeichnis der Verarbeitungstätigkeiten (Art. 30 DSGVO, bei privaten Vermietern mit wenigen Mietern oft entbehrlich, aber hilfreich) und eine Beschreibung der technischen und organisatorischen Maßnahmen (TOM, Art. 32). Dieses Dokument beschreibt, was Elocaro dafür **technisch** bietet, abgeglichen mit dem Code (Stand der Vorlage: Oktober 2026). Es ist **keine Rechtsberatung** und noch nicht juristisch geprüft. Wer Elocaro für andere betreibt (Hosting), ist Auftragsverarbeiter und braucht zusätzlich einen Vertrag nach Art. 28 DSGVO (siehe unten).

## Verarbeitungstätigkeiten (Muster)

| Tätigkeit | Daten | Betroffene | Zweck / Grundlage (Vorschlag) | Empfänger | Löschfrist |
|---|---|---|---|---|---|
| Mietverwaltung | Name, Anschrift, Kontakt, Geburtsdatum (optional), Vertrag, Miete, Kaution | Mieter, Bürgen | Vertragserfüllung, Art. 6 Abs. 1 lit. b | Steuerberater (Export), ggf. Behörden | Aufbewahrung nach HGB/AO, im Standard 10 Jahre nach Ende des Bezugs, danach Anonymisierung (`Privacy:RetentionYears`) |
| Zahlungsverkehr | IBAN, Kontoumsätze, Verwendungszweck | Mieter, Lieferanten | Vertragserfüllung, rechtliche Pflicht | Bank (bei FinTS-Abruf) | wie oben |
| Mieterportal | Konto, Nachrichten, Meldungen mit Fotos, Zählerstände | Mieter | Vertragserfüllung | – | wie oben |
| Benachrichtigungen | E-Mail-Adresse, Gerätekennung für Push | Mieter, Benutzer | berechtigtes Interesse / Einwilligung (Push) | SMTP-Anbieter, Push-Dienst des Browsers (ohne Inhalt) | bis Widerruf |
| Ausgehende E-Mails | Empfänger, Betreff, Text, Zustellstatus | Mieter, Benutzer, Dienstleister | Vertragserfüllung, Nachweis des Versands | SMTP-Anbieter | Text von Mails mit geheimen Links (Einladung, Anmeldung, Passwort, Signatur) bis zur Zustellung; alle Mails 3 Jahre (`Mail:RetentionDays`). Mails, auf die ein Abrechnungsversand oder eine Mahnung verweist, bleiben danach ohne Text als Nachweis, siehe `docs/betrieb.md` |
| Belege | Rechnungen, Lieferantendaten | Lieferanten | rechtliche Pflicht | optional KI-Anbieter, wenn die Belegauslesung mit einem Cloud-Anbieter aktiv ist (nur nach ausdrücklicher Einwilligung der Betreiber, siehe `docs/betrieb.md`) | Aufbewahrungsfristen |
| Digitale Unterschrift | Name, IP-Adresse, Zeitpunkt, Prüfsumme | Mieter | Beweissicherung, Art. 6 Abs. 1 lit. f | – | mit dem Vertrag |
| Betrieb | Zugriffs- und Änderungsprotokoll, Logs | Benutzer | IT-Sicherheit, Art. 6 Abs. 1 lit. f | – | Protokoll nach `Audit`-Einstellung, Job-Protokoll begrenzt. Das Änderungsprotokoll zeigt vertrauliche Felder maskiert; für „Rückgängig“ liegt zusätzlich ein Vorher-Bild der geänderten Felder (auch vertrauliche, unmaskiert) in der Datenbank, wird nirgends angezeigt und folgt der Aufbewahrung des Änderungsprotokolls (ADR 0007) |
| Neuvermietung (Modul, standardmäßig aus) | Name, E-Mail, Telefon, Notizen, Besichtigungstermin | Interessenten | vorvertragliche Maßnahmen, Art. 6 Abs. 1 lit. b | – | drei Monate nach Absage oder Übernahme als Mieter, dann physisch gelöscht samt Änderungsprotokoll (Job `prospect-retention`); wer Mieter wird, steht danach als Person im Bestand |

## Technische und organisatorische Maßnahmen

**Zugangskontrolle**
- Anmeldung mit Passwort oder Anmeldelink; Zwei-Faktor-Anmeldung (TOTP) mit Wiederherstellungscodes, für Admins und Verwalter Pflicht; Sperre nach 5 Fehlversuchen für 15 Minuten; Ratenbegrenzung der Anmelde-Anfragen je IP-Adresse.
- Rollen und Rechte (Admin, Hausmeister, Mieter, Eigentümer, Handwerker-Links) mit getrennten Richtlinien; Mieter sehen nur die eigenen Verträge, Eigentümer nur eigene Objekte.

**Weitergabe und Transport**
- TLS und HSTS über den Reverse-Proxy (Caddy); CSP, `X-Frame-Options: DENY`, `nosniff`, Referrer- und Permissions-Policy.
- Links für Handwerker, Unterschriften und Anmeldung enthalten geheime Token; bei den Unterschriftslinks wird nur der Hash gespeichert, und sie laufen nach 14 Tagen ab.
- Push-Nachrichten sind leer; Titel und Text holt das Gerät nach Anmeldung vom eigenen Server.

**Speicherung und Vertraulichkeit**
- PostgreSQL nur im internen Docker-Netz erreichbar; Datei-Uploads begrenzt (Größe, Typ), Fotos werden bereinigt (Metadaten entfernt).
- FinTS: PIN wird nie gespeichert, Benutzerkennung mit Data Protection verschlüsselt, keine PIN/TAN in Logs.
- Backups verschlüsselt (restic) und dedupliziert, Restore-Test dokumentiert (`docs/betrieb.md`).

**Integrität und Nachvollziehbarkeit**
- Änderungsprotokoll für Stammdaten (wer, wann, alt/neu), Zugriffsprotokoll für Personenakten (höchstens ein Eintrag je 30 Minuten), unveränderliche Dateiablage mit SHA-256.
- Abgelegte PDFs (Schreiben, Verträge, Abrechnungen) werden nicht nachträglich verändert.

**Betroffenenrechte**
- Auskunft (Art. 15): JSON-Export je Person mit allen Daten, Protokoll und Modulabschnitten, auch den Mails an ihre aktuelle Adresse (ohne den Inhalt von Mails mit geheimen Links).
- Löschung/Einschränkung (Art. 17/18): Anonymisieren entfernt Name, Kontaktdaten, Anschriften und IBANs, in ausgehenden Mails an die aktuelle Adresse der Person auch Adresse, Betreff und Text (noch wartende Mails werden nicht mehr zugestellt); Buchungen bleiben für gesetzliche Aufbewahrung erhalten. Mails an eine früher hinterlegte Adresse erfasst das nicht, sie fallen mit der Frist weg. Vorschläge nach Fristende auf der Seite Datenschutz.
- Berichtigung: Stammdaten bearbeiten; Mieter können Kontaktänderungen im Portal beantragen.

**Organisatorisch (Betreiber ergänzen)**: Verpflichtung auf Vertraulichkeit, Umgang mit Zugangsdaten, Notfallplan, regelmäßige Updates (Hinweise im Dashboard bei fehlender Sicherung).

## Wenn Elocaro für andere betrieben wird (Hosting)

Zusätzlich nötig (nicht Teil der Software): Vertrag zur Auftragsverarbeitung (Art. 28), Verzeichnis der Verarbeitungstätigkeiten als Auftragsverarbeiter, Liste der Unterauftragnehmer (Rechenzentrum, Mail-Relay, Backup-Speicher), Löschkonzept bei Vertragsende (Vollexport, siehe `docs/betrieb.md`), Datenschutzerklärung der Website. Ein Anwalt sollte die Verträge erstellen; Muster (z. B. von Branchenverbänden) können als Ausgangspunkt dienen.

## Neuvermietung: Interessenten und Selbstauskunft

Das Modul ist standardmäßig aus. Es verarbeitet Daten Fremder, die noch keinen Vertrag haben.

- **Was wird gefragt:** Name und Kontakt (vom Vermieter erfasst), im Formular nur Zahl der Personen, Beruf, Netto-Haushaltseinkommen, Haustiere, gewünschter Einzug und eine freie Nachricht. Alles außer der Personenzahl ist freiwillig. Das Formular fragt nichts zu Herkunft, Religion, Gesundheit, Partei oder Familienplanung; der Hinweis steht im Formular. Ausweis, Schufa und Gehaltsnachweise gehören nicht hinein (erst vor Vertragsabschluss beim gewählten Bewerber).
- **Einwilligung:** Pflichtfeld im Formular, Zeitpunkt wird gespeichert (`ConsentAt`). Ohne Haken wird nichts gespeichert.
- **Link:** 256 Bit, nur der Hash liegt in der Datenbank, 14 Tage gültig, einmal nutzbar; ein neuer Link widerruft den offenen. Unbekannte, abgelaufene und benutzte Links sehen gleich aus.
- **Löschkonzept:** Eine Absage setzt die Löschfrist auf drei Monate, ebenso die Übernahme als Mieter (die Daten stehen dann in der Person). Der Job `prospect-retention` (täglich) löscht Interessent und Selbstauskunft physisch, nicht nur markiert, und entfernt deren Einträge im Änderungsprotokoll. Zurück bleibt ein Eintrag „Löschfrist abgelaufen“ ohne Personendaten als Nachweis. Der Job läuft auch bei ausgeschaltetem Modul. Interessenten ohne Entscheidung haben keine Frist: Bitte entscheiden Sie oder löschen Sie sie zeitnah, wenn die Wohnung vergeben ist (alle offenen Interessenten einer Einheit mit einer Absage zu schließen, ist die übliche Pflege).
- **Auskunft und Zugriff:** Das Ansehen einer ausgefüllten Selbstauskunft steht im Zugriffsprotokoll (Zweck „Selbstauskunft eines Interessenten angesehen“). Interessenten sind keine Personen im Bestand, solange sie nicht übernommen sind; Auskunftsersuchen (Art. 15) beantworten Sie aus der Interessentenliste.
- **Sicherungen:** Gelöschte Daten bleiben in älteren Datensicherungen bis zu deren Aufbewahrungsende.

**Exposé und Mietpreisbremse:** Das Exposé enthält keine personenbezogenen Daten von Interessenten, nur die Angaben zur Wohnung, das Foto des Hauses und die Kosten. Die Hinweise zur Mietpreisbremse sind eine Orientierung für den Vermieter und stehen nicht im Exposé; sie ersetzen keine Rechtsberatung. Ob die Mietpreisbremse am Ort gilt, hängt von einer Verordnung des Landes ab und wird nicht geprüft. Die Miete des Vormieters (Daten einer anderen Person, aber ohne Namen) wird nur gedruckt, wenn der Vermieter sie eingibt, weil er sie bei Berufung auf § 556e dem Mieter vor Vertragsschluss mitteilen muss.
