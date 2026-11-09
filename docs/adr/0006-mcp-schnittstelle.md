# ADR 0006: MCP-Schnittstelle (nur lesend) mit persönlichen Zugriffstoken

- Status: angenommen
- Datum: 2026-11-09

## Kontext

KI-Assistenten sollen Elocaro-Daten abfragen können. MCP ist dafür der verbreitete Standard. Elocaro hatte bisher nur die Cookie-Anmeldung und keine API. Die Dienste der Verwaltung (`IPropertyService`, `IPersonService`, `ILeaseService` …) prüfen Rechte nicht selbst, das tut die Seite oder der Endpoint davor. Ein neuer Zugang darf das Rollenmodell deshalb nicht umgehen.

## Entscheidung

- **Endpoint** `/mcp` (Streamable HTTP, zustandslos) mit dem Paket `ModelContextProtocol.AspNetCore`. Aus, bis `Mcp:Enabled` gesetzt ist; nie in der Demo.
- **Persönliche Zugriffstoken** (`elo_…`, nur der SHA-256 liegt in der Datenbank, Ablauf, widerrufbar) statt OAuth. Das Token ist **allgemein** (`AccessToken`, Authentifizierungsschema `ElocaroAccessToken`) und nicht an MCP gebunden: Weitere Integrationen (Kalender, Skripte, Webhooks) können es nutzen, wenn ihr Endpoint das Schema und eine Policy nennt. Das Cookie gilt an solchen Endpoints nicht, damit scheidet CSRF aus.
- **Rechte werden pro Anfrage neu berechnet**: heutige Rechte des Kontos ∩ Bereiche des Tokens (`read:masterdata`, `read:finance`). Gesperrt, Rolle gewechselt, Recht entzogen, Zwei-Faktor aus: sofort wirksam. Sperren widerruft die Token.
- **Der Principal der Anfrage ist ein Verwalter mit genau den wirksamen Rechten**, auch bei Administratoren. So sehen die bestehenden Prüfungen der Dienste (`IsInRole(Admin) || HasClaim(…)`) und die Todo-Quellen den begrenzten Umfang, ohne dass jede Stelle den MCP-Fall kennen muss.
- **Jedes Werkzeug trägt `[Authorize(Policy = …)]`.** Die SDK-Filter blenden die Werkzeugliste nach Policy aus und weisen Aufrufe ohne Recht ab. Architekturtests (`McpRulesTests`) verlangen Policy, `ReadOnly = true` und verbieten schreibende Dienstaufrufe in Werkzeugen.
- **Nur Administrator und Verwalter.** Die Portale bleiben außen vor.
- **Nur lesend**, ohne Dateiinhalte, Bankverbindungen von Personen und Gegenseiten-IBAN der Umsätze.
- **Protokollierung** jedes Aufrufs im Audit-Protokoll (Werkzeug und Hash der Argumente), Rate-Limit je Absenderadresse.

## Alternativen

- **OAuth 2.1 mit Registrierung (DCR) und PKCE.** Bräuchte einen eigenen Autorisierungsserver in der Anwendung und würde die Web-Konnektoren von claude.ai und ChatGPT erschließen. Die Sicherheitsfläche und der Umfang sind deutlich größer. Zurückgestellt; das Token-Modell bleibt dafür die Grundlage (OAuth würde Token desselben Typs ausstellen).
- **Rechteprüfung in den Diensten statt an den Werkzeugen.** Wäre sauberer, ist aber ein großer Umbau aller Verwaltungsdienste. Bis dahin sichern Policy am Werkzeug und Architekturtest den neuen Zugang ab.
- **Token nur für MCP.** Verworfen: Eine zweite Integration bräuchte sonst eine zweite Art von Zugangsdaten samt eigener Verwaltung. Die Bereiche (`read:masterdata`, `read:finance`) sind fachlich, nicht an MCP gebunden.
- **Rate-Limit je Token.** Verworfen: Partitionen je präsentiertem Token ließen sich von Unbefugten unbegrenzt anlegen. Es gilt ein Limit je Absenderadresse.

## Folgen

- Neue Tabelle `access_tokens` (Migration `AddAccessTokens`), neue Konfiguration `Mcp:*` und `AccessTokens:*`, neue Seite `/konto/sicherheit/zugriffstoken`.
- Neue Werkzeuge brauchen Policy, `ReadOnly = true` und einen Test; siehe `docs/entwicklung.md`.
- Betrieb und Anwender: `docs/mcp.md`.
