# Mitarbeiten

## Ablauf

1. Issue auswählen oder anlegen; Umsetzung auf einem eigenen Branch.
2. Vor jedem Commit lokal prüfen:
   ```bash
   dotnet format --verify-no-changes && dotnet build && dotnet test
   ```
3. Pull Request gegen den Default-Branch `main`, Beschreibung mit `Closes #<Issue>`.
4. Merge erst, wenn die CI grün ist.

**Sprache:** Code ist englisch (Bezeichner, Kommentare, Testnamen, Datenbank, Konfiguration, CSS-Klassen). Deutsch bleiben
Oberflächentexte, URLs, Dokumentation und Commit-Nachrichten. Details und Glossar: [docs/entwicklung.md](docs/entwicklung.md#sprache-im-code).

Konventionen, Testaufbau und Migrationen: [docs/entwicklung.md](docs/entwicklung.md).
Oberfläche (Design-Tokens, Bausteine, Seitentypen, Checkliste für neue Seiten): [docs/ui.md](docs/ui.md). Neue Seiten halten sich daran.

## Lizenz und Beitragsvereinbarung (CLA)

Elocaro steht unter der [AGPL-3.0-or-later](LICENSE). Jeder Beitrag steht unter derselben Lizenz. Zusätzlich gilt die [Beitragsvereinbarung (CLA)](CLA.md): Das Urheberrecht bleibt bei dir, der Projektinhaber erhält aber das Recht, deinen Beitrag auch unter anderen Lizenzen weiterzugeben (zum Beispiel für eine kommerzielle Zweitlizenz). Du stimmst zu, indem du im Pull Request das Kästchen zur CLA ankreuzt (oder den Zustimmungssatz aus der CLA kommentierst). Die Action `CLA` prüft das und setzt den Status „CLA“ am Pull Request; ohne Zustimmung bleibt er rot und der Beitrag kann nicht übernommen werden. Projektinhaber, Mitglieder der Organisation und Bots (Dependabot) sind ausgenommen.

Bitte keine echten Mieter- oder Bankdaten und keine Zugangsdaten in Beiträgen, Issues oder Testdaten.

## Empfohlene Branch-Protection für den Default-Branch

In *Settings → Branches → Branch protection rules* (bzw. *Rulesets*):

- Pull Request vor dem Merge erforderlich
- Statuschecks erforderlich: **Build & Test**, **Docker & Restore-Test**, **CLA**
- Branch muss vor dem Merge aktuell sein
- Force-Pushes und Löschen verhindern

## Releases

Ein Tag `vX.Y.Z` auf dem Default-Branch baut das Image für amd64 und arm64 und veröffentlicht es als
`ghcr.io/elocaro/elocaro:X.Y.Z` (sowie `X.Y` und `latest`):

```bash
git tag v0.1.0 && git push origin v0.1.0
```

Es gibt keine gepflegte Änderungsdatei: Der Release-Workflow erzeugt die Änderungen der Version aus den Commit-Betreffs seit dem vorigen Tag (`.github/scripts/release-notes.sh`; Vorschau: `.github/scripts/release-notes.sh HEAD`). Deshalb folgt jeder Commit-Betreff dem Muster `<Bereich>: <was sich ändert, als verständlicher deutscher Satz> (Refs #<Issue>)` (oder `Closes`), 15–130 Zeichen vor dem Verweis, Großbuchstabe, kein Punkt am Ende, Einzelheiten im Text darunter. Beispiel: `Zähler: Änderungshistorie zeigt wieder Einträge (Refs #184)`. Der Hook `.githooks/commit-msg` prüft das beim Commit (einmalig `git config core.hooksPath .githooks`), die CI-Aufgabe „Commit-Betreffe“ bei jedem Push und Pull Request. In die Release-Notes gehört nur, was für Anwender direkt spürbar ist. Alles ohne direkte Auswirkung (Refactoring, Modulstruktur, Tests, CI, Entwicklungswerkzeuge) bekommt einen der Bereiche `Intern:`, `Werkzeuge:`, `Architektur:`, `Tests:` oder `CI:` und wird aus den Notes herausgefiltert. Merge-Commits sind ausgenommen, Betreffe mit `Changelog:`, `Release ` oder `Version ` sind nicht erlaubt. Die Version aus dem Tag landet über das Build-Argument `VERSION` in der Anwendung („Über Elocaro“).

Die erzeugte Liste wird als Text des GitHub-Releases veröffentlicht und unter *Neuigkeiten und Updates* als Markdown dargestellt (Fett, Kursiv, `Code`, Listen, Links mit http/https; kein HTML, keine Bilder).

**Google-Anmeldung im Image:** Damit „Mit Google verbinden“ in den veröffentlichten Images ohne Einrichtung funktioniert, liest der
Release-Workflow die Repository-Secrets `GOOGLE_CLIENT_ID` und `GOOGLE_CLIENT_SECRET` (OAuth-Client vom Typ „Fernsehgeräte und Geräte mit
begrenzter Eingabe“, siehe `docs/betrieb.md`) und baut sie als BuildKit-Secret in das App-Image ein (`google-client.json`). Einmalig
setzen: `gh secret set GOOGLE_CLIENT_ID` und `gh secret set GOOGLE_CLIENT_SECRET` (Wert wird abgefragt, nicht ins Terminal-Protokoll
schreiben). Die Werte stehen **nicht** im Repository, aber lesbar im veröffentlichten Image: Google stuft den Schlüssel dieses Client-Typs
als nicht vertraulich ein, und der Zugriff reicht nur auf selbst angelegte Dateien (`drive.file`). Wird der Client missbraucht, in der
Cloud Console einen neuen Schlüssel erzeugen, die Secrets ersetzen und neu veröffentlichen. Ohne die Secrets entsteht ein Image ohne
Client; jede Installation kann dann ihren eigenen eintragen.

Neue GHCR-Pakete sind zunächst privat, und die Compose-Dateien ziehen `ghcr.io/elocaro/elocaro:latest`. Nach dem **ersten**
Release deshalb beide Pakete (`elocaro` und `elocaro-backup`) unter *Packages → Package settings → Danger Zone* auf
**öffentlich** stellen (die Organisation muss öffentliche Pakete erlauben: *Organization settings → Packages*) und prüfen,
dass `docker pull ghcr.io/elocaro/elocaro:latest` ohne Anmeldung funktioniert. Für ein privates Paket gilt stattdessen:
auf dem Server einmalig `docker login ghcr.io` mit einem Token (Scope `read:packages`) ausführen.

Abhängigkeiten aktualisiert Dependabot wöchentlich (EF Core/ASP.NET/Npgsql gruppiert, Quartz-Major ausgenommen). Dependabot-Commits heißen „Intern: Bump …“ (Präfix in `.github/dependabot.yml`) und sind von der Betreffprüfung ausgenommen.
