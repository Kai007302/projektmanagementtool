# ADR 0015 — KI-Assistent, Werkzeuge und MCP-Server

## Status
Proposed

## Kontext

Kai möchte, dass ProjectHub den aktuellen Stand der KI-Anbindung unterstützt (2026-10-04). Bis dahin gab es nur Platzhalter (`IKnowledgeSemanticSearch`, `IKnowledgeAnswerService`) und die offene Frage nach dem Anbieter (`docs/OPEN_DECISIONS.md`, „AI provider“). Vorgaben aus dem Konzept:

- KI darf nur Inhalte nutzen, die die Person sehen darf, und muss ihre Quellen nennen (AGENTS.md, ADR 0006, PRODUCT.md).
- Externe Anbieter nur mit Freigabe (INTEGRATIONS.md): KI ist ohne Konfiguration aus.
- Externe Integrationen hinter Schnittstellen, keine Secrets im Code, Betrieb auf Kais eigenem Server per `.env` (ADR 0014).

Stand der Technik 2026: Sprachmodelle rufen Werkzeuge selbst auf (Tool Use), Antworten werden gestreamt, Wissen kommt per Retrieval dazu (RAG, oft als „agentische Suche“: das Modell sucht selbst nach), und Agenten wie Claude binden Anwendungen über das **Model Context Protocol (MCP)** an.

## Entscheidung

### Anbieter austauschbar, Claude als Standard

- Schnittstelle ist `IChatClient` aus **Microsoft.Extensions.AI**, die Standard-Abstraktion für Sprachmodelle in .NET (Streaming, Werkzeugaufrufe, OpenTelemetry). Der Assistent kennt nur sie.
- `PROJECTHUB_AI_PROVIDER` wählt den Anbieter:
  - `off` (Standard außerhalb von Development): kein Assistent, nichts verlässt den Server.
  - `anthropic`: Claude über das **offizielle Anthropic-SDK**, Modell aus `PROJECTHUB_AI_MODEL`, Standard `claude-opus-5-5`. Ein neueres Modell ist eine `.env`-Änderung.
  - `openai`: jeder OpenAI-kompatible Endpunkt, z. B. ein lokales Modell (Ollama, vLLM, LM Studio) über `PROJECTHUB_AI_BASE_URL`. Damit bleiben alle Daten im eigenen Haus, wenn kein externer Anbieter freigegeben ist. Auch OpenAI und Azure OpenAI gehen so.
  - `fake` (Standard in Development): deterministische Antworten ohne Modell und ohne Schlüssel, für Entwicklung und Tests.
- Für Claude setzt ProjectHub: Aufwand (`PROJECTHUB_AI_EFFORT`, Standard `medium`; Claude denkt adaptiv), automatisches Prompt-Caching (Anweisungen und Werkzeuge sind bei jeder Anfrage gleich), und bei den aktuellen Modellen den serverseitigen Fallback (`fallbacks: "default"`): Lehnt das Modell eine Anfrage ab, antwortet das Modell, das die API dafür vorsieht, im selben Aufruf.

### Werkzeuge statt Prompt-Füllung

- `ProjectHubTools` beschreibt, was ein Modell in ProjectHub darf: `search_knowledge`, `read_knowledge_article`, `list_projects`, `get_project`, `list_tasks`, `get_task`, und als Schreibwerkzeuge `create_task` und `add_task_comment`.
- Jedes Werkzeug läuft **als die angemeldete Person** über die vorhandenen Services (`KnowledgeAccess.Visible`, `ProjectAccess`, `TaskService` …). Rechte, Validierung, Audit und Aktivitäten sind dieselben wie in der API; ein Modell kann nichts sehen oder tun, was die Person nicht kann.
- Fehler kommen als kleines Ergebnis (`{"error": …}`) zurück, damit das Modell reagieren kann.
- Werkzeuge laufen nacheinander (gemeinsamer `DbContext`), höchstens sechs Runden je Frage.

### RAG über den Knowledge Hub

- `IKnowledgeRetrieval` ersetzt die Platzhalter: Eine Frage trifft Artikel, die eines ihrer Wörter enthalten (ODER-Verknüpfung über den vorhandenen Volltextindex, deutsche Stammformen), sortiert nach Abdeckung; die Passagen sind PostgreSQL-Ausschnitte um die Treffer. Gefiltert wird in der Datenbankabfrage mit `KnowledgeAccess.Visible` (ADR 0006).
- Das Modell sucht selbst (agentisch) und kann mehrfach nachsuchen oder einen Artikel ganz lesen. Es nennt genutzte Artikel als Link `[Titel](article:<id>)`; zusätzlich schickt der Server die Liste aller gelesenen Artikel mit (`sources`, mit Kennzeichen „zitiert“).
- Keine Vektordatenbank und keine Embeddings in diesem Schritt: Claude bietet keine Embeddings an, und die Volltextsuche mit agentischem Nachsuchen genügt für den Start. Embeddings (pgvector) kommen bei Bedarf hinter `IKnowledgeRetrieval` dazu (DEC-036).

### Assistent in der Oberfläche

- `POST /api/v1/ai/chat` streamt die Antwort als **Server-Sent Events** (`delta`, `tool`, `sources`, `done`, `error`); `GET /api/v1/ai/status` sagt der Oberfläche, ob der Assistent eingerichtet ist.
- Das Gespräch liegt nur im Browser und wird bei jeder Frage mitgeschickt; ProjectHub speichert keine Gespräche (DEC-035). Grenzen: 40 Nachrichten, je 8000 Zeichen, zusammen 60 000.
- Der Assistent ist **nur lesend** (DEC-034): Er bekommt keine Schreibwerkzeuge, weil es in der Oberfläche noch keine Bestätigung vor einer Änderung gibt. *Abgelöst durch ADR 0016: Änderungen mit Freigabe.*
- Eigenes Rate Limit je Person (`PROJECTHUB_AI_RATE_LIMIT_PER_MINUTE`, Standard 10), zusätzlich zum allgemeinen Limit.
- Die Oberfläche zeigt Antworten mit einem kleinen Markdown-Ausschnitt. Klickbar sind nur Links auf ProjectHub-Artikel, Bilder werden nie geladen.

### MCP-Server für Agenten

- `PROJECTHUB_MCP=on` öffnet `/api/v1/mcp` (Streamable HTTP, zustandslos, offizielles C#-SDK `ModelContextProtocol.AspNetCore`). Agenten wie Claude Desktop, Claude Code oder VS Code nutzen dieselben Werkzeuge mit Annotationen (`readOnlyHint` usw.).
- Anmeldung mit **Entra-ID-Token** derselben App-Registrierung (Bereich `access_as_user`). Ohne Token antwortet der Server 401 mit `resource_metadata`; unter `/.well-known/oauth-protected-resource/api/v1/mcp` steht nach RFC 9728, dass die Token aus dem Entra-Mandanten kommen. Damit folgt ProjectHub der Autorisierung der MCP-Spezifikation; Entra bietet keine dynamische Client-Registrierung, MCP-Clients brauchen deshalb eine Client-ID (docs/SELF_HOSTING.md).
- Schreibwerkzeuge nur mit `PROJECTHUB_MCP_WRITE_TOOLS=true` (Standard aus). MCP-Clients fragen vor Werkzeugaufrufen nach; die Rechte der Person gelten trotzdem.
- Da der Endpunkt unter `/api/v1` liegt, gelten Benutzerauflösung, Rate Limit und Autorisierung der API unverändert.

### Sicherheit gegen Prompt Injection

Werkzeugergebnisse sind Inhalte von Menschen der Organisation. Die Anweisungen sagen dem Modell, dass sie Daten sind, keine Befehle. Wirksam sind aber die Grenzen dahinter: Der Assistent sieht nur, was die Person sieht, kann nichts ändern, und die Oberfläche lädt keine fremden Adressen (kein Abfluss über Links oder Bilder).

### Telemetrie

GenAI-Spans und -Metriken nach den OpenTelemetry-Konventionen (Modell, Tokens, Werkzeugaufrufe) gehen mit den übrigen Daten an den Collector (ADR 0013), **ohne** Prompts und Antworten.

## Alternativen

- **Direkt gegen die Anthropic-API ohne Abstraktion:** volle Kontrolle, aber ein lokales Modell oder ein anderer Anbieter bräuchte eine zweite Implementierung von Schleife, Streaming und Werkzeugen. Microsoft.Extensions.AI liefert das, das Anthropic-SDK setzt Claude-Besonderheiten (Thinking, Effort, Fallback) trotzdem um.
- **Klassisches RAG (Passagen vorab in den Prompt):** eine Runde weniger, aber das Modell kann nicht nachsuchen und bekommt auch bei Fragen zu Aufgaben Wissenstext. Bleibt als Optimierung möglich.
- **Embeddings jetzt (pgvector):** bessere Treffer bei Umschreibungen, aber anderes PostgreSQL-Image, Indexierungsjob und ein zweiter Anbieter für Embeddings. Später hinter derselben Schnittstelle.
- **Gespräche speichern:** Verlauf über Geräte hinweg, aber personenbezogene Daten mit Löschpflichten. Erst bei Bedarf.
- **Eigene API-Schlüssel für MCP:** einfacher für manche Clients, aber eine zweite Anmeldung neben Entra und Schlüssel, die verwaltet und verschlüsselt werden müssten (AGENTS.md: keine eigene Anmeldung).

## Konsequenzen

- Mit `anthropic` oder einem externen `openai`-Endpunkt verlassen Fragen und die gefundenen Inhalte den Server. Das braucht die Freigabe der Organisation (INTEGRATIONS.md, Datenschutz); für Kais eigenen Server entscheidet Kai. Ein lokales Modell vermeidet das.
- Kosten entstehen je Frage beim Anbieter; Rate Limit und `PROJECTHUB_AI_MAX_OUTPUT_TOKENS` begrenzen sie.
- Die Qualität der Wissensantworten hängt an der Volltextsuche; Umschreibungen ohne gemeinsame Wörter findet sie schlechter als Embeddings.
- MCP-Clients brauchen eine Client-ID in Entra (Redirect-URI des Clients eintragen).
