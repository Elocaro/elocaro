# Umbenennung in Elocaro

Beschluss vom 29.10.2026: Das Produkt heißt **Elocaro** (lat. *ēlocāre*, vermieten). Der alte Name soll im Quellcode, in der Oberfläche, in der Doku und im Betrieb **nirgends** mehr vorkommen. Es gibt nur eine Umgebung (die Produktivinstanz auf der Azure-VM), deshalb braucht es keine Rückwärtskompatibilität. Befunde zur Namenswahl: [namenspruefung.md](namenspruefung.md).

Reihenfolge: **1. Website (erledigt, Entwurf), 2. Code und Doku, 3. Produktivinstanz per Backup und Restore auf einen neuen Stack umziehen.**

**Vor Schritt 2 offen:** Die Markenprüfung für „Elocaro“ (insbesondere Ähnlichkeit zu „Elcaro“, Klassen 9 und 42) ist noch nicht gemacht. Eine Umbenennung des Codes ist groß; wer sie zweimal machen muss, zahlt doppelt. Mindestens eine Abfrage im DPMAregister und bei EUIPO eSearch vorher.

## Schritt 2: Code und Doku

Mechanisch, in Teilschritten, nach jedem Teil Build und Tests:

1. **Dateien und Namensräume:** Projekte und Ordner `MietHahn.*` → `Elocaro.*`, `MietHahn.slnx`, Namensräume, `MietHahnDbContext` → `ElocaroDbContext`, Migrations-Designer und Modell-Snapshot (die Migrations-IDs bleiben unverändert, die Datenbank kennt nur diese).
2. **Konfiguration und Betrieb:** Umgebungsvariablen `MIETHAHN_*` → `ELOCARO_*` (ohne Übergangsunterstützung, es gibt keine zweite Installation), Image-Namen `elocaro` und `elocaro-backup`, Compose-Projektname, `POSTGRES_DB` und `POSTGRES_USER` `elocaro`, Backup-Skript `elocaro-backup.sh` mit Tag und Host `elocaro`, `install.sh`, CI, Dockerfile, Caddyfile.
3. **Anwendung:** `SetApplicationName("Elocaro")` (siehe Tabelle unten), Claim-Namen (`miethahn:2fa` → `elocaro:2fa`), Oberflächentexte, PWA-Manifest, Mail- und PDF-Texte, Vorlagen.
4. **Logo und Icons:** Entwurf „Haus mit E“ (`site/logo.svg`) als `icon.svg`, zusätzlich PNG in 192 und 512 Pixel sowie ein Apple-Touch-Icon (PNG), maskierbar.
5. **Doku und Repository-Dateien:** README, `docs/`, `CLAUDE.md`, `CONTRIBUTING.md`, Issue-Vorlagen, `THIRD-PARTY-NOTICES.md`, `CHANGELOG.md` (ältere Einträge dürfen den alten Namen als Geschichte nennen, eine Ausnahme ist ausdrücklich zu kennzeichnen).
6. **Absicherung:** Ein Architekturtest, der fehlschlägt, sobald der alte Name im Quellbaum auftaucht (mit kleiner, dokumentierter Ausnahmeliste: `docs/namenspruefung.md`, `docs/umbenennung.md`, Kontaktadresse in der Website, Geschichte im Changelog).
7. **Screenshots** für README, Website und Handbuch neu aufnehmen (zeigen derzeit den alten Namen im App-Kopf).
8. **Repository:** Umzug nach `github.com/elocaro/elocaro` (Organisation vorher anlegen), GHCR-Pakete `elocaro` und `elocaro-backup`. Die Links der Website zeigen schon dorthin.

## Was bei Namensänderung Daten berührt

Die ASP.NET-Schlüssel für die Datensicherheit liegen in der Datenbank (`PersistKeysToDbContext`) und werden mit gesichert und wiederhergestellt. Der **Anwendungsname** geht aber in die Ableitung der Schlüssel ein. Ein neuer Name bedeutet daher:

| Betroffen | Folge | Maßnahme |
|---|---|---|
| Anmelde- und Sitzungscookies, Anti-Forgery | Alle Sitzungen enden, einmal neu anmelden | keine |
| Einladungs- und Passwort-Zurücksetzen-Links | Offene Links werden ungültig | neu versenden |
| **Push-Schlüssel (VAPID)** | Der private Schlüssel ist nicht mehr lesbar. Die App würde beim Senden abbrechen, weil die Zeile vorhanden ist | **Code härten:** Bei nicht lesbarem Schlüssel neuen erzeugen und alte Push-Abonnements verwerfen; Mieter müssen Push neu einschalten |
| **FinTS-Benutzerkennung** (`BankAccount.FinTsUserId`) | Nicht mehr lesbar | Falls in Produktion eingerichtet: nach dem Umzug neu eingeben (laut Doku bisher nur mit Beispieldaten getestet) |
| Passwörter (Hash), Zwei-Faktor-Schlüssel | **Unberührt**, Identity speichert sie ohne diese Schlüssel | Anmeldung funktioniert nach dem Umzug weiter |
| Verträge, Personen, Buchungen, Dateien | Unberührt | – |

Das Anlegen eines neuen Administrators ist also meist **nicht nötig**; die vorhandene Anmeldung (Passwort plus Zwei-Faktor) läuft weiter. Falls etwas nicht stimmt, legt der Einrichtungsassistent einen neuen Administrator an.

## Schritt 3: Umzug der Produktivinstanz

Die neue Anwendung läuft in einem **neuen Stack** (`~/Elocaro`, neue Volumes, neue Datenbank `elocaro`). Die alte Instanz bleibt unverändert liegen, bis alles geprüft ist.

1. **Vorbereiten:** Neuen Quellstand nach `~/Elocaro.neu` hochladen, `deploy/.env` übernehmen und anpassen (`ELOCARO_DOMAIN`, Image-Namen), Images auf der VM bauen. Die Domain bleibt zunächst `mieten.hahnsfamily.de`.
2. **Letztes Backup der alten Instanz** erzeugen (`backup`). Das Backup-Repository (Azure-Container) bleibt dasselbe.
3. **Metadaten des Repositorys einmalig umschreiben:** Die Schnappschüsse tragen Host und Tag des alten Namens, das neue Skript sucht nach den neuen. Mit `restic rewrite --new-host` und `restic tag --set` anpassen (vorher `restic snapshots` ansehen). Alternativ beim Restore die ID des Schnappschusses angeben.
4. **Alte Instanz stoppen** (App, Caddy), damit keine neuen Daten mehr entstehen.
5. **Neuen Stack starten, nur Datenbank:** `up -d postgres`, dann `run --rm backup restore`. Das Skript legt die Datenbank neu an und stellt sie mit `pg_restore --no-owner` unter dem neuen Datenbanknamen wieder her (der alte Datenbankbenutzer spielt keine Rolle) und füllt das Dateien-Volume.
6. **Nacharbeiten in der Datenbank:** Push-Schlüssel und -Abonnements löschen (falls der Code nicht schon härtet), danach in allen Textspalten nach dem alten Namen suchen (SQL-Skript im Plan) und Treffer in Benutzerdaten bereinigen, zum Beispiel Absenderprofile oder Vorlagen.
7. **Alles starten** (`up -d`), warten bis Caddy nicht mehr `503` meldet, dann prüfen: `/health`, Anmeldung mit Passwort und Zwei-Faktor, ein PDF erzeugen, ein Dokument öffnen, `blazor.web.js` ausliefern, Mail-Testversand.
8. **Rückfall:** Neuen Stack stoppen, alten Stack (`~/MietHahn`, alte Volumes) wieder starten. Das geht, solange alte Volumes und Verzeichnis liegen bleiben.
9. **Aufräumen** nach einigen Wochen: alte Volumes, altes Verzeichnis, alten Backup-Container (Azure, `miethahn-backup`) und alte Images entfernen. Wer das Repository im Azure-Container behalten will, kann den Containernamen unverändert lassen. Dort steht der alte Name aber im Klartext (Konfiguration), deshalb besser ein neuer Container und `restic copy`.

**Test vorab:** `deploy/backup/restore-test.sh` läuft in der CI bei jedem Push und beweist, dass Sicherung und Wiederherstellung in eine leere Datenbank funktionieren. Für den Umzug zusätzlich einmal eine echte Wiederherstellung auf dem Arbeitsrechner aus einer Kopie des Produktiv-Backups, mit Anmeldung und PDF-Erzeugung.

## Offene Fragen

- Ist FinTS in der Produktivinstanz eingerichtet?
- Sind in der Produktivinstanz Push-Abonnements von Mietern in Gebrauch?
- Gibt es in den Daten selbst (eigene Vorlagen, Absenderprofile, Aushänge) Texte mit dem alten Namen?

## Stand Code-Umbenennung (29.10.2026)

Umgesetzt: Projekte, Ordner, Namensräume, `ElocaroDbContext`, Konfigurationsschlüssel (`ConnectionStrings:Elocaro`), Umgebungsvariablen `ELOCARO_*`, Image-Namen, Compose-Projektname, Datenbank und Benutzer `elocaro`, Backup-Skript `elocaro-backup.sh` (Host und Tag `elocaro`), Name des Data-Protection-Schlüsselrings, Claim-Namen, Oberfläche, Mail- und PDF-Texte, Doku, neue Icons (SVG und PNG), Architekturtest `NamingRulesTests` gegen Rückfälle. Härtung: Nicht lesbare Push-Schlüssel werden ersetzt statt den Versand abzubrechen.

**Offene Folgen außerhalb des Codes:**

- **FinTS-Produktregistrierung** bei der Deutschen Kreditwirtschaft wurde unter dem früheren Produktnamen beantragt. Die Registrierungsnummer im Code bleibt unverändert. Die Namensänderung dem Registrierungsdienst melden (Kontakt über <https://www.fints.org/de/hersteller/produktregistrierung>) und klären, ob eine neue Registrierung nötig ist.
- **Azure:** Ressourcengruppe und Backup-Container (`…-backup`) der bestehenden Instanz tragen noch den alten Namen. Die Doku beschreibt die Neuanlage mit neuen Namen; für den Umzug siehe Schritt 3 oben.
- **Verzeichnis auf dem Entwicklungsrechner:** Der Arbeitsordner heißt noch wie früher; er lässt sich außerhalb von Git umbenennen.
- **Repository und Pakete:** Umzug nach `github.com/elocaro/elocaro` und neue GHCR-Pakete stehen aus. Bis dahin zeigen Links in Doku und Website auf das künftige Repository.
- **Screenshots** in README, `docs/bilder` und `site/` zeigen noch die alte Oberfläche und sind neu aufzunehmen.
- **E-Mail-Adresse** in Impressum und Datenschutz der Website (bestehendes Postfach) gegen eine Adresse mit dem neuen Namen tauschen, sobald die Domain feststeht.
