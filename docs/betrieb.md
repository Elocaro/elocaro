# Betrieb (VPS mit Docker Compose)

## Überblick

```
Internet ──443/80──▶ caddy (TLS, Let's Encrypt) ──▶ app:8080 (MietHahn)
                                                      ├─▶ postgres:5432 (nur intern)
                                                      └─▶ gotenberg:3000 (nur intern)
backup (nachts) ──▶ postgres + Volume dateien ──▶ restic-Repository (S3/SFTP/…)
```

| Dienst | Image | Daten |
|---|---|---|
| `app` | `ghcr.io/ther00st3r/miethahn` (chiseled, Non-Root, ohne Shell) | Volume `files` → `/data` (Belege, Fotos, PDFs) |
| `postgres` | `postgres:18-alpine` | Volume `postgres` (Daten inkl. Anmelde-Schlüssel) |
| `gotenberg` | `gotenberg/gotenberg:8.38.1` | – |
| `backup` | `ghcr.io/ther00st3r/miethahn-backup` (pg_dump + restic) | Volumes `backup-status`, `backup-cache`; Repository extern bzw. `deploy/backup-repo` |
| `caddy` | `caddy:2.10-alpine` | Volumes `caddy-data`, `caddy-config` (Zertifikate) |

PostgreSQL und Gotenberg liegen im Netz `intern` ohne Internetzugang. Die App ist zusätzlich im Netz `extern` (ausgehend für SMTP, später FinTS und KI-API). Von außen erreichbar ist nur Caddy.

## Voraussetzungen

- VPS mit Linux, ≥ 2 GB RAM (Gotenberg/Chromium braucht ~500 MB), ≥ 20 GB Platte
- Docker Engine mit Compose-Plugin
- Domain mit A/AAAA-Eintrag auf den Server, Ports 80 und 443 offen
- SMTP-Zugang (z. B. vom Mailanbieter der Domain)

## Installation

**Mit Installationsskript (empfohlen):**

```bash
git clone https://github.com/TheR00st3r/MietHahn.git && cd MietHahn
./deploy/install.sh            # fragt Domain, Kontakt-Mail und SMTP ab, erzeugt die Passwörter, schreibt deploy/.env und startet alles
```

Das Skript prüft Docker und Compose, schreibt `deploy/.env` (Rechte 600, ein vorhandenes `.env` wird nur mit `--force` ersetzt und als `.env.bak` gesichert), startet den Stack und wartet, bis die Adresse antwortet. Optionen: `--lokal` (Probelauf auf `https://localhost` mit selbst signiertem Zertifikat, ohne Domain), `--nur-env` (nur die Datei schreiben). Alle Fragen lassen sich über Umgebungsvariablen vorab beantworten (`MIETHAHN_DOMAIN`, `ACME_EMAIL`, `MAIL_SENDER`, `SMTP_HOST`, `SMTP_PORT`, `SMTP_SECURITY`, `SMTP_USER`, `SMTP_PASSWORD`; mit `MIETHAHN_NONINTERACTIVE=1` wird nichts gefragt). Das Sicherungsziel steht danach auf „lokal“; ein externes Ziel richten Sie wie unter [Backup](#backup) beschrieben ein.

**Von Hand:**

```bash
git clone https://github.com/TheR00st3r/MietHahn.git && cd MietHahn
cp deploy/.env.example deploy/.env
nano deploy/.env        # Domain, Passwörter, SMTP eintragen (Datenbankpasswort: openssl rand -base64 32)
docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d
docker compose -f deploy/docker-compose.yml --env-file deploy/.env ps
```

Nach 1–2 Minuten ist MietHahn unter `https://<domain>` erreichbar. Beim ersten Aufruf führt die Seite durch die **Ersteinrichtung** (Admin-Konto, danach Zwei-Faktor-Anmeldung).

Anschließend:
1. Unter *Einstellungen → E-Mail-Versand* eine Testnachricht senden.
2. *Einstellungen → Vermieter* pflegen. Die Angaben erscheinen in Briefkopf und Fußzeile von Dokumenten.
3. Backup einrichten und eine erste Sicherung anstoßen (siehe [Backup](#backup)).

## Konfiguration

Alle Einstellungen kommen über Umgebungsvariablen in `deploy/.env` (eingetragen in `deploy/docker-compose.yml`). Weitere Schlüssel lassen sich im Abschnitt `environment` des Dienstes `app` ergänzen; `__` trennt Ebenen (z. B. `SignIn__RequestsPerMinute=20`).

| Variable | Bedeutung |
|---|---|
| `MIETHAHN_DOMAIN`, `ACME_EMAIL` | Domain und Kontakt für Let's Encrypt; daraus wird `Application__BaseUrl` (Links in Mails) |
| `POSTGRES_PASSWORD` | Datenbankpasswort |
| `MAIL_*`, `SMTP_*` | Absender und SMTP-Zugang (`SMTP_SECURITY`: `StartTls` für Port 587, `SslOnConnect` für 465) |
| `MIETHAHN_IMAGE` | Image-Tag, Standard `ghcr.io/ther00st3r/miethahn:latest`; empfohlen: feste Version, z. B. `ghcr.io/ther00st3r/miethahn:0.1.0` (Änderungen siehe `CHANGELOG.md`). Die laufende Version steht unter „Über MietHahn“ (`/ueber`) |

## Belegauslesung (optional, KI)

Beim Erfassen eines Belegs kann MietHahn aus dem hochgeladenen PDF bzw. Foto einen **Vorschlag** auslesen (Lieferant, IBAN, Rechnungsnummer und -datum, Leistungszeitraum, Brutto/Netto/USt, Kostenart, § 35a, Brennstoffmenge und CO₂). Der Vorschlag füllt nur das Formular vor; gespeichert wird erst, wenn Sie bestätigen. Unsicher erkannte Felder sind gelb markiert, Unstimmigkeiten (Netto + USt ≠ Brutto, ungewöhnlicher Steuersatz, ungültige IBAN, unmögliche Daten) stehen als Warnung darüber.

**Standard ist „Keiner“: Es verlässt kein Dokument den Server.** Der Anbieter wird ausschließlich über Umgebungsvariablen (`deploy/.env`) gewählt, ein Wechsel braucht keine Codeänderung:

| Variable (`deploy/.env`) | Konfigurationsschlüssel | Bedeutung |
|---|---|---|
| `RECEIPT_AI_PROVIDER` | `ReceiptExtraction__Provider` | `None` (Standard), `Claude` oder `Ollama`. Ein unbekannter Wert schaltet die Auslesung ab und wird unter *Einstellungen → Belegauslesung* gemeldet |
| `RECEIPT_AI_ACCEPT_EXTERNAL` | `ReceiptExtraction__AcceptExternalProcessing` | **Ausdrückliche Einwilligung** (`true`), dass Belege an einen externen Anbieter gehen. Ohne sie tut der Claude-Anbieter nichts, auch nicht mit Schlüssel |
| `ANTHROPIC_API_KEY` | `ReceiptExtraction__Claude__ApiKey` | API-Schlüssel. Nur hier; er wird nie in der Datenbank gespeichert und nie protokolliert |
| `RECEIPT_AI_CLAUDE_MODEL` | `ReceiptExtraction__Claude__Model` | Modellname, Standard `claude-sonnet-5-5` |
| – | `ReceiptExtraction__Claude__BaseUrl` | Standard `https://api.anthropic.com/` (z. B. für einen eigenen Proxy) |
| `OLLAMA_URL` | `ReceiptExtraction__Ollama__BaseUrl` | Adresse von Ollama, Standard in Compose `http://host.docker.internal:11434/` |
| `OLLAMA_MODEL` | `ReceiptExtraction__Ollama__Model` | Modell **mit Bildeingabe**, z. B. `qwen2.5vl:7b` oder `llama3.2-vision` (vorher `ollama pull …`) |
| – | `ReceiptExtraction__TimeoutSeconds` / `__MaxRequestMb` | Zeitlimit je Anfrage (120) und Obergrenze aller Seiten eines Belegs (20 MB) |

**Datenschutz**

- **Claude API:** Die Belege werden zum Auslesen an Anthropic übermittelt. Sie enthalten oft personenbezogene Daten (Mieter, Handwerker, Anschriften, IBANs). Vor dem Einschalten den Auftragsverarbeitungsvertrag des Anbieters abschließen und die Verarbeitung ins Verzeichnis von Verarbeitungstätigkeiten aufnehmen. Hinweis und Status zeigen die Belegseiten und *Einstellungen → Belegauslesung* (Recht *System*).
- **Ollama:** Läuft Ollama auf dem eigenen Server oder im eigenen Netz, bleiben die Belege dort. Ollama liest **nur Fotos (JPG, PNG), keine PDF-Dateien**; ein PDF muss als Foto/Scan hochgeladen werden. Die App erreicht Ollama über das Netz `extern`; der Compose-Dienst `app` kennt dafür `host.docker.internal`.
- Gesendet wird nur auf Klick auf **„Vorschlag aus Beleg auslesen“**, nie im Hintergrund. Jede Übergabe steht im Zugriffsprotokoll (Audit-Log, „Beleg zur Auslesung an … übergeben“). Prompts, Dokumentinhalte und der Schlüssel erscheinen in keinem Log.
- Die Seitenanzeige nennt den Verbrauch der letzten Auslesung (Eingabe-/Ausgabe-Tokens) als Kostenhinweis. Die Preise legt der Anbieter fest.
- **Lernen:** Wird ein Beleg mit Kostenart gespeichert, merkt sich MietHahn „Lieferant → Kostenart“ (Tabelle `supplier_cost_type_rules`, im Änderungsprotokoll und in der Datensicherung). Beim nächsten Beleg dieses Lieferanten hat die gelernte Kostenart Vorrang vor dem Modell.

Beispiel `deploy/.env` für Claude:

```
RECEIPT_AI_PROVIDER=Claude
RECEIPT_AI_ACCEPT_EXTERNAL=true
ANTHROPIC_API_KEY=sk-ant-...
```

## Belege per E-Mail (optional)

MietHahn kann ein Postfach abrufen und die Anhänge (PDF, JPG, PNG) als **offene Belege** ablegen: ohne Betrag und Kostenart, mit dem Hinweis „Betrag fehlt“ in der Belegliste und auf der Übersicht. Beträge und Kostenart ergänzen Sie danach wie bei jedem Beleg.

Voraussetzung ist ein eigenes Postfach mit IMAP-Zugang (z. B. `belege@ihre-domain.de`); ein Mailserver gehört nicht zu MietHahn. Einstellungen (Umgebungsvariablen mit `__` statt `:`):

| Schlüssel | Bedeutung |
|---|---|
| `ReceiptInbox__Enabled` | `true` schaltet den Abruf ein (alle fünf Minuten) |
| `ReceiptInbox__Host`, `Port`, `Security` | IMAP-Server, Standard Port 993 und `SslOnConnect` |
| `ReceiptInbox__UserName`, `Password` | Zugangsdaten des Postfachs |
| `ReceiptInbox__AllowedSenders__0` … | erlaubte Absender: Adresse oder `@domain`; **ohne Eintrag wird nichts abgerufen** |
| `ReceiptInbox__ProcessedFolder` | Ordner für verarbeitete Mails (Standard `MietHahn-verarbeitet`), leer = nur als gelesen markieren |
| `ReceiptInbox__MaxAttachmentMegabytes` | Größenlimit je Anhang, Standard 15 |
| `ReceiptInbox__Extract` | `true` liest die Daten mit der [Belegauslesung](#belegauslesung-optional-ki) aus (nur wenn diese bereit ist; Dokumente verlassen dann ggf. den Server) |

**Sicherheit:** Die Absenderadresse einer Mail lässt sich fälschen. Verwenden Sie eine nicht öffentliche Adresse und ein Postfach, dessen Server SPF/DKIM prüft und Spam aussortiert. Mails anderer Absender, ohne verwertbaren Anhang oder mit zu großen Dateien werden nicht übernommen (nur im Log vermerkt). Eine Virenprüfung führt MietHahn nicht durch; setzen Sie sie am Mailserver ein.

## Benachrichtigungen auf dem Gerät (Web-Push)

MietHahn ist als Web-App installierbar. Jeder Benutzer kann unter *Konto & Sicherheit → Benachrichtigungen auf diesem Gerät* Hinweise einschalten, die auch ohne geöffnete Seite erscheinen: neue Nachrichten, Meldungen (Tickets), Fristen, neue Dokumente und Aushänge, Zählerstand-Erinnerungen, Verbrauchsinformation und die Warnung zur Datensicherung. Sie folgen den bestehenden Benachrichtigungs-Mails: wer eine solche Mail bekäme und Push eingeschaltet hat, bekommt zusätzlich den Hinweis.

- **Datenschutz:** Die Push-Nachricht selbst ist leer. Das Gerät holt Titel und Kurztext nach dem Aufwecken vom eigenen Server (nur mit gültiger Anmeldung); beim Push-Dienst des Browser-Herstellers (Google, Mozilla, Apple, Microsoft) liegt kein Inhalt. Die Hinweise sind absichtlich allgemein („Neue Nachricht“).
- **Abschalten:** Pro Gerät am selben Schalter. Ungültig gewordene Geräte entfernt MietHahn selbst.
- **iPhone/iPad:** Push geht nur für Web-Apps, die zum Home-Bildschirm hinzugefügt wurden (Teilen → „Zum Home-Bildschirm“).
- **Technik:** Der Server erzeugt beim ersten Aufruf ein VAPID-Schlüsselpaar (privater Schlüssel durch Data Protection geschützt in der Datenbank; bei Verlust der Schlüsselringe müssen Geräte neu abonnieren). Die Absenderkennung ist `Push__Contact` (Standard: die Absenderadresse der Mails). Zugelassen sind nur die Push-Dienste von Google, Mozilla, Apple und Microsoft; weitere Hosts über `Push__AllowedHosts__0` …. Der Versand läuft über den Job `push-dispatch` (jede Minute). Bei Verbindungen ohne HTTPS (außer `localhost`) stehen Service Worker und Push nicht zur Verfügung.
- **Offline:** Ein Service Worker zeigt bei fehlender Verbindung eine Hinweisseite statt der Browser-Fehlerseite; die Anwendung selbst benötigt weiterhin eine Verbindung.

## Gesundheit & Logs

```bash
curl https://<domain>/health        # {"status":"Healthy","checks":{"database":…,"gotenberg":…,"file-store":…,"backup":…}}
docker compose -f deploy/docker-compose.yml --env-file deploy/.env logs -f app
```

- `/health/live` meldet, ob der Prozess antwortet. Darauf prüfen Docker (`HEALTHCHECK` per `dotnet MietHahn.Web.dll healthcheck`) und Caddy.
- `/health` prüft Datenbank, Gotenberg und ob die Dateiablage beschreibbar ist. Antwort 503 bei Problemen, ohne Fehlerdetails.

## Update

```bash
docker compose -f deploy/docker-compose.yml --env-file deploy/.env pull
docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d
```

Datenbank-Migrationen laufen beim Start automatisch. Vorher sichern: `docker compose … run --rm backup backup`. Für reproduzierbare Updates `MIETHAHN_IMAGE` auf eine Version setzen und gezielt hochzählen.

### PostgreSQL-Major-Upgrade (z. B. 18 → 19)

Das Datenformat ist zwischen Major-Versionen nicht kompatibel:
1. Sichern: `docker compose … run --rm backup backup`.
2. `docker compose … down`, Volume `miethahn_postgres` umbenennen oder löschen.
3. Image von `postgres` **und** die Basis in `deploy/backup/Dockerfile` anheben, `up -d postgres`.
4. `docker compose … run --rm backup restore`, dann `up -d`.

## Module und Einrichtungsassistent

Nicht jeder braucht alle Funktionen. Unter **Einstellungen → Module** lässt sich jedes Fachmodul ein- und ausschalten; zu jedem steht dort, was es tut, wann es sich lohnt und was dazugehört. Immer an sind Objekte, Personen, Verträge, Mieterkonto, Benutzer und Einstellungen.

| Modul | Wofür |
|---|---|
| Bank & Zahlungsabgleich | Bankkonten (FinTS, CSV), Umsätze zuordnen, Regeln |
| Mahnwesen | Rückstände erkennen, Mahnungen vorbereiten |
| Belege | Belegablage und optionale Belegauslesung |
| Nebenkostenabrechnung | Umlageschlüssel, Heizkosten, Abrechnung (braucht Belege) |
| Zähler & Verbrauch | Zählerstände, Import, Verbrauchsinformation für Mieter |
| Mieterhöhung & Indexmiete | Staffeln, Verbraucherpreisindex (VPI), Mieterhöhungs-Schreiben |
| Steuer & Eigentümer | Anlage V, AfA, Darlehen, DATEV, Eigentümer-Portal (braucht Belege) |
| Tickets & Hausmeister | Anliegen, Handwerker, Routineaufgaben |
| Mitteilungen & Aushänge | Nachrichten und Aushänge im Mieterportal |
| Dokumente & Schreiben | Ablage, Schreiben aus Vorlagen |
| Fristen & Wartung | Kalender, Wartungen, Erinnerungsmails |

Ein abgeschaltetes Modul verschwindet aus Navigation, Suche, Tabs und Mieterportal. Seiten und Downloads antworten mit 404, die Hinweise auf dem Dashboard entfallen und die zugehörigen Hintergrundjobs laufen leer durch. **Es werden keine Daten gelöscht**: Wer ein Modul wieder einschaltet, findet alles unverändert vor. Hängt ein Modul von einem anderen ab, schaltet die Seite das andere mit ein bzw. mit aus.

### Auswahl innerhalb der Bereiche

Zusätzlich lässt sich einstellen, was Sie innerhalb eines Bereichs überhaupt verwenden. Das steht unter Einstellungen → Module (unten) und wird im Assistenten abgefragt:

| Frage | Optionen | Wirkung |
|---|---|---|
| Welche Mietmodelle verwenden Sie? | Staffelmiete, Indexmiete (die Vergleichsmiete ist immer dabei) | Das Vertragsformular bietet nur die gewählten Modelle an und fragt das Modell schon beim Anlegen, sobald es mehr als eines gibt. Indexfelder, die Seite „Verbraucherpreisindex“ und der Staffel-Job gibt es nur bei aktivem Modell. |
| Wie rechnen Sie Nebenkosten ab? | Vorauszahlung, Betriebskostenpauschale, Inklusivmiete | Das Vertragsformular zeigt nur die passenden Felder: bei Vorauszahlung Betriebs- und Heizkosten-Vorauszahlung, bei Pauschale die Pauschale, bei Inklusivmiete keine. |
| Wie arbeitet das Mahnwesen? | Mahngebühren, Verzugszinsen | Die Felder dafür erscheinen unter Forderungen → Einstellungen nur bei Auswahl oder wenn schon Werte gespeichert sind. |
| Welche Teile der Steuer nutzen Sie? | Darlehen, Abschreibung (AfA) und Erhaltungsaufwand, DATEV-Export, Eigentümer-Portal (die Anlage V ist immer dabei) | Seiten, Tabs und Downloads der abgewählten Teile entfallen (404). Der Bereich „Darlehen & Steuer“ bleibt, solange eine Seite darin übrig ist. |
| Was vermieten Sie? | Zimmerweise (WG), Garagen/Stellplätze, Gewerbe (Wohnungen sind immer dabei) | Die Einheitenart-Auswahl bietet nur Gewähltes an. Die Zimmer-Verwaltung der Wohnung erscheint nur bei WG-Vermietung. |
| Welche Angaben im Mietvertrag? | Kaution, Bürgen | Die Felder fehlen in Anlegen-Formular und Vertragsdetails, wenn nicht gewählt. |

Zusätzlich blendet die Heizungsseite eines Objekts Abschnitte nach dem Brennstoff aus: die Aufteilung Heizung/Warmwasser nur bei gemeinsamer Anlage, die Heizöl-Angaben nur bei Heizöl, die CO₂-Aufteilung nur bei Gas und Öl. Der Assistent fragt am Ende auch, ab welchem Monat die Monatsmieten automatisch gebucht werden (nur solange noch kein Start gesetzt ist). Die Mahnwesen-Einstellungen unter Finanzen gibt es nur, solange das Modul Mahnwesen an ist.

Eine abgeschaltete Option bleibt sichtbar, solange ein Vertrag sie noch verwendet. Die Seite nennt dann die Anzahl der Verträge. Bestehende Beträge und Verträge werden nie verändert oder gelöscht.

Nach der Ersteinrichtung (Administrator und Zwei-Faktor-Anmeldung) führt das Dashboard einmalig in den **Einrichtungsassistenten**. Er hat höchstens sieben Schritte und blendet die Navigation aus, bis er fertig ist:

1. **Willkommen:** Die Frage „Wie vermieten Sie?“ (einige Wohnungen, größerer Bestand/Hausmeister, WG-Zimmer) setzt eine Vorauswahl der Funktionen und Optionen. Nichts ist pauschal eingeschaltet.
2. **Funktionen:** Jedes Modul mit Kurzbeschreibung und „Was gehört dazu?“; hier schalten Sie ein und aus.
3. **Ihre Vermietung:** die Optionen der eingeschalteten Module (Mietmodelle, Abrechnungsarten, Steuer, Einheitenarten, Vertragsangaben, Mahnwesen), siehe Tabelle unten.
4. **Ihre Absenderdaten** (nur, solange es kein Vermieterprofil gibt): Name und Anschrift für Briefkopf und Fußzeile.
5. **Erstes Objekt** (nur, solange es kein Objekt gibt): Mit dem Haken „Erstes Objekt jetzt anlegen“ erscheinen Haus mit Anschrift und Wohnungen samt Wohnfläche; ohne Haken überspringt „Weiter“ den Schritt.
6. **Buchhaltung und Vorlagen:** Beginn der automatischen Monatsmieten (nur, solange er fehlt) und die Standardvorlagen, wenn das Modul „Dokumente & Schreiben“ an ist.
7. **Zusammenfassung:** zeigt, was gespeichert und angelegt wird.

„Später“ überspringt den Assistenten ohne Änderung; die Übersicht zeigt dann die Checkliste „So geht es los“. Unter Einstellungen → Module lässt er sich jederzeit erneut starten (`/verwaltung/einrichtung`; ohne die Einstiegsfrage). Bestehende Installationen behalten beim Update alle Module und werden nicht zum Assistenten geleitet.

## Bankumsätze zuordnen

Nach dem Import erscheinen offene Umsätze unter **Finanzen → Umsätze zuordnen** (auch als Hinweis auf der Startseite). Dort lässt sich der Vorschlag bestätigen, ändern/aufteilen oder der Umsatz ignorieren. Wiederkehrende Zahlungen (Stadtwerke, Versicherungen, private Abbuchungen) lassen sich unter **Zuordnungsregeln** hinterlegen. Eine Zuordnung kann auf der Seite des Umsatzes rückgängig gemacht werden; sie stehen im Änderungsprotokoll und sind Teil der Datensicherung wie alle Tabellen (`transaction_assignments`, `match_rules`). Es sind keine zusätzlichen Einstellungen oder Dienste nötig; die Migration läuft beim Start.

## Mahnwesen

Der Job `dunning` (täglich 6:15, sichtbar unter **System → Hintergrundjobs**) vergleicht die Rückstände mit den Mahnvorschlägen. Er **versendet nichts**: Vorschläge erscheinen auf der Startseite und unter **Finanzen → Mahnwesen**; Brief und E-Mail entstehen erst nach der Freigabe dort. Für die Freigabe sind nötig:

- ein **Vermieterprofil** (Einstellungen → Vermieter) für Briefkopf, Absender und Bankverbindung,
- erreichbarer **Gotenberg** (PDF) und für den E-Mail-Versand ein funktionierender Mailserver (die Mail geht über die Outbox, siehe E-Mail-Versand).

Unter **Finanzen → Einstellungen Finanzen → Mahnwesen** stehen Karenzzeit, Wartezeiten zwischen den Stufen, Zahlungsfrist, Mindestbetrag, Mahngebühren je Stufe, die Verzugszinsen und der **Basiszinssatz**. Der Basiszinssatz ändert sich zum 1. Januar und 1. Juli und muss dort von Hand nachgetragen werden (Veröffentlichung der Deutschen Bundesbank); ohne Wert werden keine Verzugszinsen berechnet. Voreinstellung: keine Gebühren, keine Zinsen. Die freigegebenen Briefe liegen als PDF in der Dokumentenablage (Kategorie *Schriftverkehr*, nicht für Mieter freigegeben) und sind Teil der Datensicherung; Tabellen `dunnings` und `dunning_holds`.

> Die mitgelieferte Mahnvorlage (`Pdf/Templates/dunning.sbn`, `Mail/Templates/dunning.sbn`) ist eine **Basisvorlage und keine Rechtsberatung**. Texte, Fristen, Gebühren und Zinsen vor dem produktiven Einsatz rechtlich prüfen lassen.

## Nebenkostenabrechnung

Der **Abrechnungs-Assistent** (Objekt → Abrechnungs-Assistent) führt in sechs Schritten durch die Abrechnung und friert sie bei der Freigabe ein. Auf der Detailseite der freigegebenen Abrechnung (`/verwaltung/abrechnungen/{id}`) wird das Schreiben je Mietvertrag geprüft (Vorschau als PDF), versendet und der Zugang protokolliert. Der Versand legt das Schreiben in der Dateiablage und im Mieterportal ab und bucht Nachzahlung bzw. Guthaben im Mieterkonto. Dafür sind nötig:

- ein **Vermieterprofil** (Einstellungen → Vermieter) für Briefkopf, Absender und Bankverbindung,
- erreichbarer **Gotenberg** (PDF) und für den E-Mail-Versand ein funktionierender Mailserver.

Konfiguration (Abschnitt `Statements`, Umgebungsvariablen `Statements__…`):

| Schlüssel | Standard | Bedeutung |
|---|---|---|
| `Statements__PaymentDays` | 30 | Tage ab Versand bis zur Fälligkeit einer Nachzahlung bzw. Auszahlung eines Guthabens |
| `Statements__PrepaymentSafetyPercent` | 5 | Sicherheitszuschlag auf den Vorschlag für die neue Vorauszahlung (im Versanddialog überschreibbar) |

Auf der Startseite erinnert ab Oktober des Folgejahres eine Aufgabe an die Abrechnung des Vorjahres, 6, 4 und 2 Wochen vor dem 31.12. wird sie dringlicher (§ 556 Abs. 3 BGB).

> Die mitgelieferte Abrechnungsvorlage (`Pdf/Templates/statement.sbn`, `Mail/Templates/statement.sbn`) ist eine **Basisvorlage und keine Rechtsberatung**. Texte vor dem produktiven Einsatz rechtlich prüfen lassen. Die CO₂- und Gradtagstabellen der Heizkostenabrechnung sind „Stand/prüfen“ (siehe Entwicklung).

## Sicherheit

- TLS und HSTS über Caddy; die App setzt CSP, `X-Frame-Options: DENY`, `nosniff`, Referrer- und Permissions-Policy.
- Die App übernimmt Schema und Client-IP aus `X-Forwarded-*` nur von Absendern aus privaten Netzen (Docker-Netz). Die HTTPS-Umleitung macht Caddy (`Hosting__HttpsRedirection=false` im Image).
- **Data-Protection-Schlüssel** (Cookies, Tokens) liegen in der Tabelle `data_protection_keys`. Anmeldungen überstehen so Neustarts und sind im DB-Backup enthalten. Die Schlüssel sind dort nicht zusätzlich verschlüsselt. Wer Zugriff auf die Datenbank hat, hat ohnehin Zugriff auf alle Daten; die Datenbank ist daher nur intern erreichbar. Die Warnung „No XML encryptor configured“ beim ersten Start ist erwartet.
- Uploads: Caddy begrenzt auf 30 MB, die App zusätzlich auf `FileStore__MaxSizeMb` (25).
- Geheimnisse nur in `deploy/.env` (per `.gitignore` ausgeschlossen), Dateirechte `chmod 600 deploy/.env`.

## Backup

Der Dienst `backup` (Image `ghcr.io/ther00st3r/miethahn-backup`, Quelle `deploy/backup/`) sichert jede Nacht um `BACKUP_TIME` (Standard 02:30):

1. die Datenbank per `pg_dump` (Custom-Format; passt zur PostgreSQL-Version, da das Image auf `postgres:18-alpine` basiert),
2. das Volume `files` (Belege, Fotos, PDFs),

verschlüsselt und dedupliziert mit [restic](https://restic.net) in ein Repository. Danach werden alte Stände ausgedünnt (7 tägliche, 4 wöchentliche, 12 monatliche; `BACKUP_BEHALTEN_*`) und das Repository geprüft (`restic check`).

### Einrichten

In `deploy/.env`:

| Variable | Bedeutung |
|---|---|
| `RESTIC_PASSWORD` | Schlüssel des Repositorys. **Ohne dieses Passwort ist keine Wiederherstellung möglich.** Außerhalb des Servers aufbewahren (Passwortmanager). |
| `RESTIC_REPOSITORY` | Ziel, siehe unten. Standard `/repo` = Ordner `BACKUP_LOCAL_PATH` (`deploy/backup-repo`) auf demselben Server, nur zum Ausprobieren |
| `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY` | Zugang für S3-kompatible Ziele |
| `BACKUP_TIME` | Uhrzeit (deutsche Zeit, `HH:MM`) |

Beispiele für `RESTIC_REPOSITORY` (alle von restic unterstützten Ziele funktionieren):

```
s3:https://s3.eu-central-1.amazonaws.com/mein-bucket/miethahn     # AWS, Hetzner Object Storage, Wasabi, MinIO …
sftp:backup@nas.example.de:/volume1/miethahn                      # SFTP: Schlüssel nach deploy/backup-ssh/ (id_ed25519 + known_hosts)
rest:https://benutzer:passwort@backup.example.de/miethahn          # restic REST-Server
```

Das Repository wird beim ersten Lauf angelegt. Danach einmal von Hand sichern und prüfen:

```bash
DC="docker compose -f deploy/docker-compose.yml --env-file deploy/.env"
$DC up -d backup
$DC run --rm backup backup      # sofort sichern
$DC run --rm backup snapshots    # vorhandene Stände
$DC logs backup                  # Zeitplan und Ergebnisse
$DC run --rm backup verify      # zusätzlich 10 % der Daten lesen und prüfen (z. B. monatlich)
```

### Überwachung

Nach jedem Lauf schreibt der Backup-Dienst eine Statusdatei in das Volume `backup-status`, die App liest sie (`Backup__StatusFile`):

- *Einstellungen → Datensicherung* zeigt das Ergebnis der letzten Sicherung.
- Schlägt sie fehl, ist sie älter als 26 Stunden (`Backup__MaxAgeHours`) oder gab es noch keine, erscheint im Dashboard „Datensicherung prüfen“. Alle aktiven Admins erhalten **täglich um 8:05 Uhr eine Warnmail**, bis wieder erfolgreich gesichert wurde. Ein ausgefallener Backup-Container fällt so ebenfalls auf.
- `/health` meldet `backup: Degraded` (HTTP 200, die App bleibt erreichbar).

## Wiederherstellen

Die Wiederherstellung ersetzt Datenbank **und** Dateiablage vollständig durch den gewählten Stand.

```bash
DC="docker compose -f deploy/docker-compose.yml --env-file deploy/.env"
$DC stop app                                  # keine Verbindungen während des Einspielens
$DC run --rm backup snapshots                 # gewünschten Stand wählen
$DC run --rm backup restore          # neuester Stand; oder: restore <ID>
$DC start app
```

Das Skript holt den Stand aus dem Repository, legt die Datenbank neu an (`DROP DATABASE … WITH (FORCE)`, `pg_restore`), ersetzt den Inhalt des Volumes `files` und setzt die Dateirechte für den App-Benutzer (UID 1654). Es bricht ab, solange die App noch verbunden ist.

### Auf einem neuen Server

1. Installation wie oben bis einschließlich `deploy/.env`. Dabei **dasselbe** `RESTIC_PASSWORD`, `RESTIC_REPOSITORY` und die Zugangsdaten eintragen. `POSTGRES_PASSWORD` darf neu sein.
2. Nur die Datenbank starten: `$DC up -d postgres`
3. `$DC run --rm backup restore`
4. `$DC up -d`. Anmeldungen, Konten und Zwei-Faktor-Einstellungen sind erhalten (die Anmelde-Schlüssel liegen in der Datenbank).

### Restore-Test

`deploy/backup/restore-test.sh` spielt den Ablauf vollständig durch und läuft in der CI bei jedem Push: App gegen leere Datenbank starten, Testdaten anlegen, sichern, **alle Volumes löschen**, in eine leere Datenbank wiederherstellen, dann Schema, Inhalt jeder Tabelle (Prüfsumme) sowie Dateien samt Eigentümer vergleichen. Zum Schluss muss die App auf dem wiederhergestellten Stand starten. Lokal:

```bash
docker build -t miethahn:lokal . && docker build -t miethahn-backup:lokal deploy/backup
deploy/backup/restore-test.sh
```

## Vollexport

Unabhängig vom Backup lässt sich unter *Einstellungen → Datensicherung* (Recht *System*) ein **Vollexport** als ZIP herunterladen. Er ist ohne MietHahn lesbar:

- `data/<table>.json` und `data/<table>.csv` (Semikolon, UTF-8 mit BOM, Dezimalkomma; für Excel) für jede Tabelle, gelöschte Datensätze eingeschlossen,
- `files/<id>/<originalname>` für alle hochgeladenen Dateien,
- `manifest.json` (Tabellen, Spalten, Datentypen, Zeilenzahlen) und `LIESMICH.txt` (Formatbeschreibung).

Nicht enthalten sind Passwort-Hashes, Tokens, Anmeldelinks und Cookie-Schlüssel. Jeder Export wird im Audit-Log vermerkt. Zum Wiederherstellen von MietHahn dient das Backup, nicht der Export.

## Lokal ausprobieren

```bash
docker build -t miethahn:lokal .
MIETHAHN_DOMAIN=localhost MIETHAHN_IMAGE=miethahn:lokal POSTGRES_PASSWORD=test ACME_EMAIL=a@b.de \
  MAIL_SENDER=test@localhost SMTP_HOST=localhost \
  docker compose -f deploy/docker-compose.yml up -d --no-build
```

Für `localhost` stellt Caddy ein Zertifikat seiner internen CA aus (Browserwarnung bestätigen).

## Vorlagen und Schreiben an Mieter

- **Vorlagen** (Einstellungen → Vorlagen): eigene Texte für Schreiben an Mieter und wiederverwendbare **Bausteine** (z. B. Grußformel, Bankverbindung). Beim ersten Mal „Standardvorlagen anlegen“ wählen; die Texte sind Ausgangspunkte und **keine Rechtsberatung**, bitte vor dem Einsatz prüfen und anpassen. Zu den Standardvorlagen für Schreiben gehören die **Wohnungsgeberbestätigung** für Einzug und Auszug (§ 19 BMG, binnen zwei Wochen auszustellen, Ordnungswidrigkeit bis 1.000 €) und die Bestätigung der Kündigung. Dazu kommen die **Mietbescheinigung** (Miete, Nebenkosten, Wohnfläche, z. B. für Wohngeld), die **Mietschuldenfreiheitsbescheinigung** (nimmt die fälligen Forderungen aus dem Mieterkonto; bei Rückstand erscheint stattdessen ein Hinweis) und die **Kautionsabrechnung** (Eingang, Zinsen, Verrechnungen, Erstattung aus der Kautionsführung). Die Beträge gelten zum Tag der Erstellung; erzeugt werden sie unter *Kommunikation → Dokumente → Schreiben an Mieter*. Die Übergabeprotokoll-Seite erinnert daran. Die Vorlage **Änderung des Umlageschlüssels (§ 556a Abs. 2 BGB)** dient der Erklärung, z. B. Müll künftig nach Personen umzulegen: Sie muss dem Mieter vor Beginn des Abrechnungszeitraums in Textform zugehen und gilt nur für künftige Zeiträume. Zum Eigentumsübergang gibt es die Vorlage **Information Vermieterwechsel (§ 566 BGB)**: Vorher das Vermieterprofil des Erwerbers anlegen und dem Objekt zuordnen, dann das Schreiben an alle Mieter des Objekts erzeugen.
- Im Text stehen Platzhalter wie `{{ salutation }}` oder `{{ tenant_name }}`; die Liste steht unter dem Editor. Bausteine fügt `{{ include 'Grußformel' }}` ein. Die Vorschau zeigt das PDF mit Beispieldaten und benötigt den PDF-Dienst und das Vermieterprofil.
- Jede Änderung des Textes bleibt als **Fassung** erhalten und lässt sich in den Editor zurückladen. Schon erzeugte Schreiben ändern sich nie; sie verweisen auf die Fassung, aus der sie entstanden.
- **Schreiben an Mieter** (Kommunikation → Dokumente → Schreiben an Mieter): Vorlage und Objekt wählen, Empfänger abhaken (Standard: alle laufenden Mietverträge), optional per E-Mail senden und im Mieterportal freigeben. Je Mieter entsteht ein PDF bei den Dokumenten; ein Fehler bei einem Mieter hält die anderen nicht auf. Mieter ohne E-Mail-Adresse bekommen nur das PDF (per Post senden).
- **Austausch und Sicherung:** Vorlagen lassen sich einzeln als Datei exportieren und in einer anderen Installation importieren. Im Gesamtexport und in der Datensicherung sind sie wie alle Tabellen enthalten.

## Mietvertrag erstellen

- Unter **Verträge → Vertrag → Vertragsdokument** wird aus den Stammdaten ein Entwurf als PDF erzeugt: Parteien, Mietobjekt und Fläche, Miete (Standard, Staffel oder Index), Vorauszahlung oder Pauschale, vereinbarte Betriebskostenarten, Kaution, dazu Hausordnung, Energieausweis-Hinweis, Datenschutzinformation und Übergabeprotokoll als Anlagen. Für ein WG-Zimmer wird der **Zimmervertrag** gewählt (Zimmer, Anteil der Gemeinschaftsflächen, Pauschale, Möblierungsliste).
- Vorher die Standardvorlagen anlegen (Einstellungen → Vorlagen → „Standardvorlagen anlegen“) und prüfen: Sie sind **Basisvorlagen und keine Rechtsberatung**, insbesondere zu Schönheitsreparaturen, Kleinreparaturen, Befristung und Kündigung. Die Möblierungsliste eines Zimmers pflegt man am Zimmer.
- Der Entwurf wird ausgedruckt und unterschrieben. Den Scan lädt man am selben Ort hoch („Unterschrieben hochladen“); er landet bei den Dokumenten des Vertrags und der Vertrag gilt als abgeschlossen. Ein neuer Entwurf ersetzt einen noch offenen.

## Mieterhöhung

- Am Vertrag gibt es **Mieterhöhung**. Der Assistent zeigt die Rahmendaten (Mietmodell, Miete, letzte Erhöhung, Kappungsgrenze, frühester Zugang) und führt durch die vier Fälle: **Vergleichsmiete** (§ 558 BGB), **Indexmiete** (§ 557b), **Staffel** (Information) und **Betriebskostenpauschale** (§ 560).
- **Vergleichsmiete:** Mietspiegel (Spanne und Einordnung der Wohnung in €/m²), drei Vergleichswohnungen oder Gutachten eingeben. „Prüfen“ zeigt, ob 15 Monate und das Jahr seit der letzten Erhöhung eingehalten sind, ob die Kappungsgrenze (20 %, bzw. 15 % wo das am Objekt eingestellt ist) und die Vergleichsmiete passen, und wie hoch die Miete höchstens sein darf. Die Kappungsgrenze stellt man am **Objekt** ein.
- **Modernisierung (§ 559 BGB):** Maßnahme beschreiben, die auf die Wohnung entfallenden Kosten und den Instandhaltungsanteil eingeben. MietHahn rechnet 8 % der umlagefähigen Kosten im Jahr, begrenzt auf 3 € je m² in sechs Jahren (2 € bei einer Miete unter 7 €/m², frühere Erhöhungen zählen mit). Das **vereinfachte Verfahren** (§ 559c, bis 10.000 €, pauschal 30 % Abzug) ist eine Option. Wurde die Maßnahme nicht angekündigt oder wurden die angekündigten Kosten um mehr als 10 % überschritten, beginnt die Erhöhung sechs Monate später. Die Erhöhung braucht keine Zustimmung: mit dem erfassten Zugang steht die neue Miete im Vertrag. Das Schreiben enthält Berechnung, Kappungsgrenze, Fristen und den Hinweis zum Härteeinwand; **die Vorlage muss rechtlich geprüft werden**.
- Nach „Schreiben erzeugen“ liegt das PDF bei den Dokumenten des Vertrags. Nach dem Versand das Datum des **Zugangs** erfassen; daraus berechnet MietHahn die Termine (Zustimmung bis Ende des zweiten Kalendermonats, Wirkung ab dem dritten). Stimmt der Mieter zu, **Zustimmung erfassen**: die neue Miete steht dann ab dem Wirkungstag im Vertrag. Bei Indexmiete und Pauschale wird die neue Miete schon mit dem Zugang eingetragen, weil keine Zustimmung nötig ist.
- Die Texte sind **Basisvorlagen und keine Rechtsberatung** (Einstellungen → Vorlagen → Standardvorlagen). Bitte rechtlich prüfen lassen; das Gutachten oder die Mietspiegelauszüge sind dem Schreiben beizufügen.

Mieter können den Entwurf auch **digital unterschreiben** (einfache elektronische Unterschrift mit persönlichem Link und Protokollseite); Voraussetzungen und Grenzen stehen in [digitale-unterschrift.md](digitale-unterschrift.md).

## Übergabeprotokoll

- Am Vertrag gibt es **Übergabe Einzug** und **Übergabe Auszug**. Das Protokoll wird mit den Räumen, Schlüsselarten und Zählern aus den Stammdaten vorbelegt und lässt sich bequem am **Smartphone** ausfüllen: Zustand je Raum und Bauteil, Mängel mit Fotos (die Kamera öffnet sich direkt), Schlüssel, Zählerstände mit Foto.
- Beim **Auszug** startet das Protokoll mit dem Stand vom Einzug. Der Vergleich zeigt, welche Mängel neu sind, was sich verschlechtert hat und welche Schlüssel fehlen. Außerdem wird die **neue Anschrift** der Mieter erfasst (sie wird beim Unterschreiben bei den Mietern hinterlegt) und im Protokoll auf die spätere Nebenkostenabrechnung und die Kautionsabrechnung hingewiesen.
- **Unterschreiben** kann der Mieter direkt auf dem Gerät (Finger oder Stift), oder man druckt die PDF-Vorschau aus und legt den unterschriebenen Scan ab. Das Protokoll liegt danach bei den Dokumenten des Vertrags und im Mieterportal. Die Zählerstände werden als **Zwischenablesung** zum Übergabetag erfasst und in der Nebenkostenabrechnung verwendet; läuft ein Zähler rückwärts oder weicht stark ab, fragt MietHahn vor dem Ablegen nach.
- Ein unterschriebenes Protokoll ist unveränderlich. Fehler im Entwurf lassen sich vor dem Unterschreiben korrigieren.
