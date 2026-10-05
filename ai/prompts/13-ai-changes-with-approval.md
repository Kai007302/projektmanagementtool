# AI PROMPT — Phase 13 KI ändert mit Freigabe

Du arbeitest an ProjectHub.

## Pflichtlektüre

- `AGENTS.md`, `docs/SECURITY.md`, `docs/PERMISSIONS.md`
- ADR 0015 (KI-Assistent und MCP), ADR 0016 (KI ändert mit Freigabe)

## Ziel

Kai, 2026-10-05: „Der KI-Assistent soll genauso viel wie ein Mensch in dem System machen können, solange er sich vorher die Genehmigung für diese Schreibaktion holt.“

## Umfang

- Schreibwerkzeuge in `ProjectHubTools` für Projekte, Aufgaben, Wissen und Teams, über die vorhandenen Services als die Person.
- Freigabe mit `ApprovalRequiredAIFunction`; der Server beschreibt jede Änderung in Worten der Person, die Oberfläche zeigt sie als Karte.
- Wartender Schritt als verschlüsselte, an die Person gebundene, einmal verwendbare Continuation (keine Speicherung).
- Dieselben Werkzeuge über MCP mit `PROJECTHUB_MCP_WRITE_TOOLS=true`.

## Acceptance Criteria

1. Ohne Klick auf „Ausführen“ ändert sich nichts; „Ablehnen“ und eine neue Frage verwerfen den Vorschlag
2. Die Karte zeigt Namen statt IDs und bei Artikeln den ganzen Text; Löschen ist als solches erkennbar
3. Eine Continuation gilt nur für die Person, unverändert, einmal und 30 Minuten
4. Über die KI darf eine Person genau das, was sie selbst darf
5. Neue Wissensartikel sind Entwürfe; Markdown wird zu gültigen Blöcken
6. Nach einer Freigabe stehen Thinking-Block und Werkzeugaufruf bei Claude im selben Zug
