# AI PROMPT — Phase 12 KI-Assistent und MCP

Du arbeitest an ProjectHub.

## Pflichtlektüre

- `AGENTS.md`, `docs/SECURITY.md`, `docs/PERMISSIONS.md`, `docs/INTEGRATIONS.md` (Knowledge / AI)
- ADR 0006 (Zugriff auf Wissen), ADR 0014 (eigener Server), ADR 0015 (KI-Assistent und MCP)

## Ziel

ProjectHub unterstützt den aktuellen Stand der KI-Anbindung (Kai, 2026-10-04): ein Assistent, der Fragen zu Projekten, Aufgaben und Wissen beantwortet, und ein MCP-Server, über den Agenten wie Claude ProjectHub nutzen. Anbieter austauschbar, Claude als Standard, alles per `.env` auf dem eigenen Server.

## Umfang

- Sprachmodell hinter `IChatClient` (Microsoft.Extensions.AI): `off`, `fake`, `anthropic` (offizielles SDK), `openai` (OpenAI-kompatibel, auch lokal).
- Werkzeuge (Tool Use), die als die angemeldete Person über die vorhandenen Services laufen.
- RAG über den Knowledge Hub: `IKnowledgeRetrieval` auf der Volltextsuche, gefiltert mit `KnowledgeAccess.Visible`; Antworten nennen ihre Quellen.
- Gestreamte Antworten (Server-Sent Events) und ein Reiter „Assistent“ in der Oberfläche.
- MCP-Server (Streamable HTTP) mit Entra-ID-Anmeldung und Protected Resource Metadata.

## Acceptance Criteria

1. Ohne Konfiguration ist KI aus; mit `PROJECTHUB_AI_PROVIDER=anthropic` und Schlüssel antwortet Claude (`claude-opus-5-5`, Effort, Prompt-Caching, Refusal-Fallback)
2. Antworten erscheinen Stück für Stück, zeigen, was nachgeschlagen wird, und listen die genutzten Artikel; ein Klick öffnet den Artikel
3. Eingeschränktes Wissen erreicht nur Personen, die es lesen dürfen, im Assistenten wie über MCP
4. MCP bietet ohne `PROJECTHUB_MCP_WRITE_TOOLS` nur lesende Werkzeuge; Schreibwerkzeuge prüfen die Rechte der Person
5. Ohne Token antwortet der MCP-Endpunkt 401 mit `resource_metadata`; das Metadaten-Dokument nennt den Entra-Mandanten
6. Gespräche werden nicht gespeichert; Telemetrie enthält keine Prompts
