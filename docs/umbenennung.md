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

## Durchführung des Umzugs (29.10.2026)

Die Produktivinstanz `mieten.hahnsfamily.de` läuft seit 14:15 Uhr im neuen Stack `~/Elocaro` (Commit 44c942f). Ausfallzeit: wenige Minuten, vom Stoppen der alten App bis zum Start der neuen. Ergebnis: Zeilenzahlen aller 122 Tabellen vor und nach dem Umzug identisch, `/health` meldet alle Prüfungen `Healthy`, das Zertifikat wurde übernommen (kein neuer Abruf bei Let's Encrypt), die erste Sicherung des neuen Stacks lief fehlerfrei.

**Was anders war als geplant (für künftige Umbenennungen):**

1. **Dump-Dateiname:** Das Backup-Skript legt den Dump als `<Datenbankname>.dump` ab. Die alten Snapshots enthalten `…/work/<alter Name>.dump`, das neue `restore` sucht `elocaro.dump` und bricht ab. Die Datenbank wurde deshalb mit denselben Schritten von Hand eingespielt (`restic restore`, `DROP/CREATE DATABASE`, `pg_restore --no-owner`, Dateien kopieren, `chown 1654`).
2. **Quartz speichert alles unter dem Planernamen** (`sched_name`) und die Job-Klassen als Typnamen mit Assembly. Nach dem Umzug legte der neue Planer seine 14 Jobs selbst an (Planername geändert), die 14 alten Jobs unter dem alten Planernamen blieben als verwaiste Zeilen liegen und wurden gelöscht. Zusätzlich wurden die Klassennamen der alten Zeilen vor dem Start korrigiert, damit nichts auf nicht mehr vorhandene Typen zeigt.
3. **Texte in der Datenbank:** Eine Standardvorlage (Mietschuldenfreiheitsbescheinigung) und ihre Version nannten den alten Produktnamen im Text und wurden angepasst. **Unverändert blieben** 3 versendete E-Mails (Betreff) und 3 Audit-Einträge: Sie dokumentieren, was tatsächlich versendet oder geändert wurde.
4. **Backup-Repository:** Neuer Container `elocaro-backup` im bestehenden Speicherkonto, per `restic init --copy-chunker-params` und `restic copy` aus dem alten gefüllt. Danach im neuen Repository `restic tag --set elocaro` und `restic rewrite --new-host elocaro --forget`. Weil sich die Snapshot-IDs durch das Umschreiben ändern, kopiert ein erneutes `restic copy` ältere Snapshots doppelt; die Aufbewahrungsregel des nächsten Backups räumt das auf.
5. **Zertifikat:** Die Volumes `caddy-data` und `caddy-config` wurden in die neuen Volumes kopiert (alter Caddy vorher gestoppt).
6. **Keine Anmeldung neu nötig:** Passwörter und Zwei-Faktor-Schlüssel stammen aus der Datenbank und blieben gültig. Die Sitzungen sind beendet (neuer Data-Protection-Name), das ist erwartet.

**Rückfall (solange die alten Reste bestehen):**

```bash
cd ~/Elocaro && docker compose -f deploy/docker-compose.yml --env-file deploy/.env stop
cd ~/MietHahn/deploy && docker compose --env-file .env start postgres gotenberg app caddy backup
```

Die alte Instanz hat ihre eigenen Volumes und den Datenstand von 14:12 Uhr; Änderungen im neuen Stack gingen bei einem Rückfall verloren.

**Aufräumen (nach einigen Wochen Betrieb ohne Auffälligkeiten):**

- Alte Container und Volumes (`docker compose … down -v` im alten Verzeichnis), die Images mit altem Namen, die Verzeichnisse `~/MietHahn` und `~/MietHahn.alt`.
- Alten Azure-Container (`…-backup` mit altem Namen) löschen. Der Name des **Speicherkontos** enthält den alten Namen und lässt sich nicht ändern; wer ihn loswerden will, legt ein neues Konto an und kopiert das Repository erneut mit `restic copy`.
- Die Ressourcengruppe in Azure trägt ebenfalls noch den alten Namen (Umbenennen geht nur durch Neuanlegen und Verschieben).
