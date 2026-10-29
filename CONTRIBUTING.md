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

## Empfohlene Branch-Protection für den Default-Branch

In *Settings → Branches → Branch protection rules* (bzw. *Rulesets*):

- Pull Request vor dem Merge erforderlich
- Statuschecks erforderlich: **Build & Test**, **Docker & Restore-Test**
- Branch muss vor dem Merge aktuell sein
- Force-Pushes und Löschen verhindern

## Releases

Ein Tag `vX.Y.Z` auf dem Default-Branch baut das Image für amd64 und arm64 und veröffentlicht es als
`ghcr.io/elocaro/elocaro:X.Y.Z` (sowie `X.Y` und `latest`):

```bash
git tag v0.1.0 && git push origin v0.1.0
```

Vorher `CHANGELOG.md` (Abschnitt „Unveröffentlicht“ → neue Version mit Datum) abschließen. Die Version aus dem Tag landet über das Build-Argument `VERSION` in der Anwendung („Über Elocaro“).

Neue GHCR-Pakete sind zunächst privat. Entweder unter *Packages → elocaro → Package settings* auf öffentlich stellen
oder auf dem Server einmalig `docker login ghcr.io` mit einem Token (Scope `read:packages`) ausführen.

Abhängigkeiten aktualisiert Dependabot wöchentlich (EF Core/ASP.NET/Npgsql gruppiert, Quartz-Major ausgenommen).
