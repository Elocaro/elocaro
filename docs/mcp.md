# KI-Zugriff über MCP

Elocaro kann seine Daten einem KI-Assistenten **lesend** und, wenn der Betreiber es einschaltet, **schreibend** zur Verfügung stellen, über das [Model Context Protocol](https://modelcontextprotocol.io) (MCP). Dann lässt sich in Claude, GitHub Copilot, Cursor und ähnlichen Werkzeugen fragen: „Welche Mieter sind im Rückstand?“, „Was steht diese Woche an?“, „Zeig mir die Belege von Lindenweg 3 aus 2025, die noch nicht bezahlt sind.“

Die Schnittstelle ist **standardmäßig aus**, ändert ohne ausdrückliche Freigabe (siehe [Schreibzugriff](#schreibzugriff)) nichts und gibt nie mehr preis, als das Konto, dem das Token gehört, in Elocaro selbst sehen darf.

## Auf einen Blick

| | |
|---|---|
| Adresse | `https://<ihre-domain>/mcp` (Streamable HTTP, zustandslos) |
| Anmeldung | Persönliches Zugriffstoken im Header `Authorization: Bearer elo_…` |
| Wer darf | Administratoren und Verwalter (Rollen mit Pflicht-Zwei-Faktor), nicht Mieter, Eigentümer, Hausmeister, Handwerker |
| Umfang | Lesen: 19 Werkzeuge; Schreiben: 6 Werkzeuge, nur mit `Mcp__AllowWrite=true` und Bereich `write:masterdata` (siehe [Werkzeuge](#werkzeuge)) |
| Aktivieren | `Mcp__Enabled=true` |
| Nicht in der Demo | Auf einer Demo-Instanz ist MCP immer abgeschaltet |

## Einschalten (Betreiber)

In `deploy/.env` (wird an die App weitergereicht):

```
MCP_ENABLED=true
# optional
# MCP_REQUESTS_PER_MINUTE=120
# MCP_MAX_RESULTS=50
# MCP_ALLOW_WRITE=false
# ACCESS_TOKEN_MAX_DAYS=90
```

Ohne Docker-Compose sind es die Konfigurationsschlüssel `Mcp:Enabled`, `Mcp:RequestsPerMinute`, `Mcp:MaxResults` und `AccessTokens:MaxDays` (Umgebungsvariablen `Mcp__Enabled` usw.). Danach die App neu starten. Der Endpoint verlangt https; das erledigt der Reverse Proxy (Caddy/Traefik), der Antworten ohne Besonderheiten durchreicht, weil der Server keine langlebigen Streams hält.

## Zugriffstoken

Ein **Zugriffstoken** lässt ein Programm in Ihrem Namen handeln, ohne dass es sich wie ein Mensch anmeldet. MCP ist der erste Nutzer; das Token selbst kennt keinen bestimmten Zweck. Was ein Token darf, bestimmen sein Konto und seine **Bereiche** (siehe unten), und der Endpoint, der es annimmt. Weitere Integrationen können dasselbe Token-Modell nutzen, ohne dass es neue Arten von Zugangsdaten gibt: Sie brauchen nur eine eigene Berechtigungsregel.

Die Seite zur Verwaltung heißt *Konto & Sicherheit → Zugriffstoken für Integrationen* (`/konto/sicherheit/zugriffstoken`) und ist nur sichtbar, solange der Betreiber mindestens eine Integration eingeschaltet hat (derzeit MCP).

## Token erstellen (Anwender)

1. *Konto & Sicherheit → Zugriffstoken für Integrationen* öffnen. Der Eintrag erscheint nur für Administratoren und Verwalter.
2. Mit „Token erstellen“ den Dialog öffnen, einen Namen vergeben (z. B. „Claude Code Laptop“), die **Bereiche** wählen und die Gültigkeit festlegen.
3. Das Token **sofort kopieren** (Schaltfläche „Kopieren“ neben dem Token). Elocaro speichert nur einen Hash und zeigt es nicht noch einmal.
4. Nicht mehr benötigte Token dort **widerrufen**; das wirkt sofort.

Das Token wie ein Passwort behandeln: nicht in Repositories, Chats oder Tickets einfügen.

## Wie Berechtigungen gelten

Ein Token **erweitert nie** die Rechte seines Kontos, es kann sie nur einschränken.

- **Wirksame Rechte = heutige Rechte des Kontos ∩ Bereiche des Tokens.** Sie werden bei **jeder Anfrage** neu aus der Datenbank bestimmt. Wird das Konto gesperrt, die Rolle gewechselt oder ein Recht entzogen, gilt das sofort auch für alle Token.
- **Bereiche:** `read:masterdata` (Stammdaten: Objekte, Verträge, Zähler, Dokumente, Personen) und `read:finance` (Finanzen: Mieterkonten, Belege, Bank, Abrechnungen). Beim Erstellen lassen sich nur Bereiche wählen, die das Konto selbst hat. Ein Verwalter ohne Recht „Finanzen“ kann also kein Finanz-Token erzeugen. Die Rechte *System* und *Benutzerverwaltung* sind über MCP nie erreichbar.
- **Rollen:** Nur Administrator und Verwalter. Das Konto muss die Zwei-Faktor-Anmeldung eingerichtet haben (ein Token hebelt die Pflicht nicht aus). Fällt sie weg, funktionieren die Token nicht mehr.
- **Nicht über das Cookie:** Der Endpoint akzeptiert ausschließlich das Token, nie die Anmeldung im Browser. Damit ist ein Aufruf aus einer fremden Webseite (CSRF) ausgeschlossen.
- **Sperren widerruft:** Wird das Konto gesperrt, sind alle seine Token endgültig widerrufen. Entsperren belebt sie nicht wieder.
- **Intern** läuft eine Anfrage als „Verwalter“ mit genau den wirksamen Rechten, auch bei Administratoren. Die bestehenden Prüfungen in den Diensten (Dashboard-Hinweise, Fristen, Kennzahlen) sehen deshalb nur den eingeschränkten Umfang.
- **Werkzeugliste gefiltert:** Der Assistent sieht nur Werkzeuge, für die das Token berechtigt ist; ein Aufruf ohne Recht wird zusätzlich abgewiesen.
- **Datensparsamkeit:** Es werden keine Dateiinhalte geliefert, nur Metadaten. Personensuche liefert keine Bankverbindungen; Kontoumsätze enthalten keine IBAN der Gegenseite und keine Mandatsdaten. Listen sind auf `Mcp:MaxResults` Einträge (Standard 50) begrenzt.

### Nachvollziehbarkeit

- Jeder Werkzeugaufruf schreibt einen Eintrag ins Audit-Protokoll: Konto, Token, Werkzeug und ein Hash der Argumente, **nicht** der Inhalt.
- Erstellen und Widerrufen von Token sind Änderungen am Datensatz und stehen im Änderungsverlauf. Der Token-Hash ist maskiert. Jede Nutzung eines Tokens, nicht nur durch MCP, hat die gleiche Art Eintrag.
- „Zuletzt benutzt“ zeigt die Liste der Token (auf die Minute genau).
- Pro Absenderadresse gelten höchstens `Mcp:RequestsPerMinute` Anfragen je Minute (Antwort 429).

### Worauf Sie achten sollten

- **Datenschutz:** Was der Assistent liest, geht an den KI-Anbieter (Claude, OpenAI, GitHub …). Im Mieterkonto stehen personenbezogene Daten. Wählen Sie nur Bereiche, die Sie dort verarbeiten lassen dürfen, prüfen Sie den Auftragsverarbeitungsvertrag des Anbieters und nennen Sie die Verarbeitung in der Datenschutzdokumentation (siehe [datenschutz-betrieb.md](datenschutz-betrieb.md)).
- **Prompt-Injection:** Texte aus Tickets, Nachrichten oder Belegen stammen teils von Dritten und können Anweisungen an das Modell enthalten. Weil alle Werkzeuge nur lesen, bleibt der Schaden begrenzt; trotzdem Antworten nicht blind weiterverwenden.
- **Kurze Laufzeiten** und ein Token je Gerät erleichtern das Widerrufen.

## Werkzeuge

Alle Werkzeuge sind schreibgeschützt (`readOnlyHint`).

| Werkzeug | Bereich | Liefert |
|---|---|---|
| `list_properties`, `get_property` | Stammdaten | Objekte, Einheiten, Flächen, Vermietungsstand |
| `list_leases`, `get_lease` | Stammdaten | Mietverträge mit Parteien, Mietbestandteilen, Mietmodell |
| `list_meters` | Stammdaten | Zähler eines Objekts |
| `search_documents` | Stammdaten | Dokumente (nur Metadaten) |
| `search_people` | Stammdaten | Personen mit Kontaktdaten, ohne Bankverbindung |
| `list_todos` | Stammdaten oder Finanzen | Dashboard-Hinweise; jede Quelle liefert nur, was der Umfang des Tokens erlaubt |
| `list_deadlines` | Stammdaten oder Finanzen | Fristen und Termine, nach Recht gefiltert |
| `get_portfolio_figures` | Finanzen | Kennzahlen des Bestands |
| `get_tenant_account`, `list_open_items` | Finanzen | Mieterkonto, offene Posten |
| `get_dunning_overview` | Finanzen | Mahnwesen |
| `search_receipts` | Finanzen | Belege |
| `list_bank_transactions` | Finanzen | Kontoumsätze (ohne Gegenseiten-IBAN) |
| `preview_operating_costs`, `list_statements` | Finanzen | Betriebskosten-Vorschau, freigegebene Abrechnungen |

Ids für Folgeabfragen (Objekt, Vertrag) stammen aus den Listenwerkzeugen; der Assistent verkettet das selbst.

## Schreibzugriff

Schreibende Werkzeuge sind **aus**, solange der Betreiber `MCP_ALLOW_WRITE=true` (`Mcp:AllowWrite`) nicht setzt. Dann lässt sich beim Erstellen eines Tokens der Bereich `write:masterdata` wählen. Er schließt das Lesen der Stammdaten ein und gilt nur für Konten mit dem Recht *Stammdaten*.

| Werkzeug | Wirkung |
|---|---|
| `create_property` | Objekt anlegen |
| `create_unit` | Einheit in einem Objekt anlegen |
| `create_person` | Person anlegen, auf Wunsch mit Anschrift |
| `update_person_contact` | E-Mail, Telefon, Mobil, Notizen einer Person ändern |
| `create_meter` | Zähler mit Anfangsstand anlegen |
| `create_lease` | Mietvertrag anlegen |

Was dabei gilt (Entscheidung: [ADR 0007](adr/0007-mcp-schreibzugriff-und-rueckgaengig.md)):

- **Nichts wird gelöscht**, und es gibt keine Werkzeuge für Finanzbuchungen, Mails, Bank, Signaturen, Benutzer oder Token. Es sind nur Datentypen freigegeben, die sich rückgängig machen lassen.
- **Dieselben Regeln wie in der Oberfläche:** Pflichtfelder, Längen und Wertebereiche der Formulare prüfen auch die Dienste; ein Aufruf mit ungültigen Werten wird mit denselben Meldungen abgelehnt.
- **Jeder Aufruf ist eine Aktion** unter *Einstellungen → System → Aktivität* (Weg „KI-Assistent (MCP)“, Konto, Werkzeug, was geändert wurde) und lässt sich dort mit „Rückgängig“ zurücknehmen. Das Rückgängig bricht ab, wenn der Datensatz seitdem geändert wurde oder etwas darauf verweist.
- **Prompt-Injection:** Texte aus Tickets, Nachrichten und Belegen kann ein Dritter geschrieben haben. Mit Schreibzugriff kann ein Assistent darauf hereinfallen und Daten anlegen oder ändern. Begrenzt wird das durch die kleine Werkzeugmenge, den eigenen Bereich und das Rückgängig; prüfen Sie die Aktivität regelmäßig. Das Token nur dem Client geben, dem Sie so weit vertrauen.

## Anleitungen für Clients

Ersetzen Sie `https://elocaro.example.de` durch Ihre Adresse und `elo_…` durch Ihr Token. Die Oberflächen der Clients ändern sich häufig; im Zweifel gilt deren aktuelle Dokumentation.

### Claude Code (getestet)

```
claude mcp add --transport http elocaro https://elocaro.example.de/mcp \
  --header "Authorization: Bearer $ELOCARO_TOKEN"
```

Das Token vorher als Umgebungsvariable setzen, damit es nicht im Shell-Verlauf steht. Servername und URL stehen vor `--header`. In einer Sitzung zeigt `/mcp` den Zustand der Verbindung. Alternativ gehört der Eintrag mit `"type": "http"`, `"url"` und `"headers"` in die `.mcp.json` des Projekts oder in `~/.claude.json`; dort das Token über `${ELOCARO_TOKEN}` einsetzen lassen, nicht im Klartext einchecken.

### VS Code mit GitHub Copilot Chat

`.vscode/mcp.json` (oder „MCP: Server hinzufügen“). Das Token fragt VS Code beim ersten Start ab und speichert es sicher:

```json
{
  "servers": {
    "elocaro": {
      "type": "http",
      "url": "https://elocaro.example.de/mcp",
      "headers": { "Authorization": "Bearer ${input:elocaro-token}" }
    }
  },
  "inputs": [
    { "type": "promptString", "id": "elocaro-token", "description": "Elocaro-Zugriffstoken", "password": true }
  ]
}
```

Der Chat muss im Agent-Modus laufen. Auf Unternehmens- oder Organisationskonten kann die Organisation MCP-Server per Richtlinie sperren.

### Cursor und andere Editoren

Cursor liest `~/.cursor/mcp.json` bzw. `.cursor/mcp.json`:

```json
{
  "mcpServers": {
    "elocaro": {
      "url": "https://elocaro.example.de/mcp",
      "headers": { "Authorization": "Bearer ${env:ELOCARO_TOKEN}" }
    }
  }
}
```

Andere Clients mit „Streamable HTTP“ und frei wählbaren Headern (z. B. Gemini CLI, Windsurf, Zed) funktionieren sinngemäß: URL und der Header `Authorization: Bearer …`.

### Claude Desktop und claude.ai

Hier gilt eine **Einschränkung**: Die Verbindung über *Einstellungen → Konnektoren* (benutzerdefinierter Konnektor) und claude.ai im Browser ist für **OAuth** ausgelegt; ein frei wählbarer Bearer-Header wird dort nach unseren Recherchen nicht überall angeboten. Elocaro bietet in dieser Version kein OAuth (siehe [Grenzen](#grenzen-und-ausblick)). Wege:

- **Claude Desktop über eine lokale Brücke:** Das Paket `mcp-remote` startet lokal als stdio-Server und reicht Header an Elocaro weiter. In `claude_desktop_config.json`:

  ```json
  {
    "mcpServers": {
      "elocaro": {
        "command": "npx",
        "args": ["-y", "mcp-remote", "https://elocaro.example.de/mcp", "--header", "Authorization:${ELOCARO_AUTH}"],
        "env": { "ELOCARO_AUTH": "Bearer elo_…" }
      }
    }
  }
  ```

  Das Leerzeichen steckt bewusst in der Umgebungsvariable (bekannte Eigenheit von Clients unter Windows). Node.js muss installiert sein. Claude Desktop neu starten.
- **Claude Code** (oben) nutzen, falls Sie ohnehin am Rechner arbeiten.

Wichtig: Die Brücke und die Konfigurationsdatei enthalten das Token im Klartext auf Ihrem Rechner.

### ChatGPT

**Voraussichtlich nicht direkt nutzbar.** ChatGPT bindet eigene MCP-Server über den Entwicklermodus (*Einstellungen → Apps/Konnektoren*, je nach Tarif) ein und unterstützt dort nach den vorliegenden Angaben **OAuth oder keine Anmeldung**. Ein statisches Bearer-Token wird je nach Version und Tarif angeboten oder nicht; die Quellen widersprechen sich. Ein MCP ohne Anmeldung kommt für Elocaro nicht in Frage, denn es enthält Mieterdaten. Prüfen Sie in Ihrem Tarif, ob ein Feld für ein Token („API-Schlüssel/Bearer“) existiert; dann gelten URL und Token wie oben. Andernfalls hilft erst OAuth (Ausblick).

Auch die OpenAI-API und das Agents SDK können Remote-MCP-Server mit eigenem Header ansprechen. Das ist für eigene Skripte und Agenten gedacht, nicht für den Chat.

### Eigene Programme (MCP-SDKs)

Jedes MCP-SDK mit Streamable-HTTP-Client funktioniert: URL `https://…/mcp`, Header `Authorization: Bearer …`. Beispiel mit dem C#-SDK (`ModelContextProtocol`): `new HttpClientTransport(new() { Endpoint = new Uri(url), AdditionalHeaders = new Dictionary<string,string> { ["Authorization"] = "Bearer " + token } })`.

## Fehlersuche

| Symptom | Ursache |
|---|---|
| 404 auf `/mcp` | `Mcp__Enabled` ist nicht `true`, oder die App wurde nicht neu gestartet |
| 401 | Token fehlt, ist falsch, widerrufen oder abgelaufen; Konto gesperrt oder nicht (mehr) Administrator/Verwalter; Zwei-Faktor nicht eingerichtet; Token mit einem anderen Header als `Authorization: Bearer` gesendet |
| 429 | Zu viele Anfragen von dieser Adresse (`Mcp:RequestsPerMinute`) |
| Werkzeuge fehlen | Dem Token fehlt der Bereich, oder dem Konto wurde das Recht entzogen. Neues Token mit passenden Bereichen erstellen |
| Leere Antworten | Das Konto hat keine Daten in dem Bereich, oder die Liste wurde gekürzt (Hinweis `note` in der Antwort) |
| Über einen Proxy kommt nichts an | Der Proxy muss `Authorization` weiterreichen und POST auf `/mcp` zulassen |

## Grenzen und Ausblick

- **Schreiben nur eingeschränkt.** Es gibt nur Werkzeuge für Stammdaten (siehe [Schreibzugriff](#schreibzugriff)), keine Löschungen und nichts bei Finanzen, Mail, Bank oder Benutzern. Siehe [ADR 0007](adr/0007-mcp-schreibzugriff-und-rueckgaengig.md).
- **Nur Administrator und Verwalter.** Portale (Mieter, Eigentümer, Hausmeister) sind nicht angebunden.
- **Kein OAuth.** Damit entfallen die Web-Konnektoren von claude.ai und ChatGPT, die OAuth erwarten. Ein späterer Ausbau wäre OAuth 2.1 mit PKCE, bei dem die Anmeldung über die bestehende Seite samt Zwei-Faktor läuft und das Ergebnis weiter in dasselbe Token-Modell mündet. Siehe [ADR 0006](adr/0006-mcp-schnittstelle.md).
- **Eine Instanz, ein Mandant.** Es gibt keine Trennung nach Objekten innerhalb einer Rolle, wie in der Oberfläche auch.

## Für Entwickler

Siehe [entwicklung.md](entwicklung.md#mcp-schnittstelle).
