# Phase 3 — Knowledge Hub & Knowledge Galaxy

Read first:

- `AGENTS.md`
- `docs/PRODUCT.md`
- `docs/ARCHITECTURE.md`
- `docs/DATA_MODEL.md`
- `docs/SECURITY.md`
- `docs/OPEN_DECISIONS.md`
- `database/migrations/002_knowledge.sql`

## Umsetzung in zwei Teilen

- Teil 1: Domäne, Rechte (DEC-020/021), Versionen, Suche, Block-Editor, Beziehungen, Verweise, Kommentare, Wissen im Projekt. Verweise auf Whiteboards folgen mit Phase 7 (DEC-022).
- Teil 2: Knowledge Galaxy (Renderer-Bewertung mit mehreren hundert Knoten, ADR 0007) und die E2E-Tests für den Artikel-Ablauf und die Galaxy-Navigation (`src/ProjectHub.Web/e2e`, CI-Job `e2e`).

## Goal

Implement the Knowledge Hub as a first-class domain module.

## Required capabilities

1. Knowledge spaces
2. Article CRUD
3. Article types
4. Draft/review/published/archived lifecycle
5. Version history
6. Block-based content model stored as JSON
7. Tags
8. Bidirectional article relations
9. Comments and @mentions
10. References to Projects, Tasks, Teams and Whiteboards
11. Permissions
12. Knowledge search
13. Knowledge Galaxy view

## Editor

Build a clean block editor. At minimum support:

- heading
- paragraph
- bullet list
- numbered list
- checklist
- quote
- callout
- code
- image
- file
- link
- task reference
- project reference
- knowledge reference

Do not persist arbitrary HTML as the canonical content format.

## Knowledge Galaxy

Implement a modern interactive visualization.

Requirements:

- pan
- zoom
- hover state
- selected node
- animated transitions
- category differentiation
- relation lines
- focus on selected topic
- keyboard-accessible alternative representation
- reduced-motion support

The visualization is a projection of KnowledgeArticle + KnowledgeRelation. It is not a separate source of truth.

For the first implementation, prefer React Flow, D3 or Canvas/WebGL as appropriate. Evaluate performance with at least hundreds of nodes before choosing an approach.

## AI-ready search

Create interfaces such as:

```text
IKnowledgeSearch
IKnowledgeSemanticSearch
IKnowledgeAnswerService
```

The first implementation may use PostgreSQL full-text search. Do not add an external vector database yet.

The retrieval interface must accept an authorization context. It must never return knowledge the user cannot access.

## Tests

Must include:

- authorization tests
- article CRUD tests
- versioning tests
- relation validation
- search tests
- reference tests
- E2E article creation/edit/publish flow
- E2E Galaxy navigation flow

## Definition of done

Build passes, tests pass, migration is applied, documentation is updated, no secrets are introduced, and the feature works using synthetic seed data.
