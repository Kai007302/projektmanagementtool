# Data Model

ER-Diagramm: `docs/diagrams/er.mmd`

SQL-Startschema: `database/migrations/001_initial.sql`

Spätere Änderungen liegen als fortlaufende Skripte daneben (ADR 0005):

- `003_identity_organization.sql`: `organization.entra_tenant_id`, `app_user.organization_role`
- `004_project_soft_delete.sql`: `project.deleted_at` (Projekte werden nur weich gelöscht)
- `005_kanban_column_status.sql`: `kanban_column.task_status`. Jede Spalte steht für einen Task-Status; die Spalte einer Karte ergibt sich aus Status, `task.kanban_column_id` und `task.board_position` (Kanban bleibt eine Ansicht auf Tasks)
- `006_knowledge_visibility_search.sql`: `knowledge_article.visibility` (`organization`/`restricted`, DEC-020), `knowledge_article.search_text` (Klartext des aktuellen Inhalts) und ein GIN-Index für die Volltextsuche (`german`) über Titel, Zusammenfassung und `search_text`

## Regeln

- Jede organisationsgebundene Ressource besitzt `organization_id` oder ist eindeutig darüber ableitbar.
- Neue Primärschlüssel: UUIDv7.
- Zeitstempel: UTC / `timestamptz`.
- Task ist zentrales Domain-Objekt.
- Kanban/Gantt referenzieren Tasks.
- Whiteboard referenziert Tasks statt Task-Business-State zu duplizieren.
- AuditLog ist aus Anwendungssicht append-only.
- Integration-Credentials nur als verschlüsselter Secret-Referenzwert.

## Concurrency

Mutable Domain Entities besitzen `version` für optimistic concurrency.

## Organization Boundary

Alle API-Zugriffe müssen die Organisation des Benutzers vor der Ressourcenprüfung berücksichtigen.
Das SQL-Schema enthält dafür `organization_id` auf zentralen Entitäten; Cross-Organization-Invarianten werden zusätzlich in transaktionalen Domain-Services abgesichert.


# Knowledge Model

## KnowledgeSpace

Optionaler Container für fachliche Wissensbereiche. Ein Projekt muss nicht an einen KnowledgeSpace gebunden sein.

## KnowledgeArticle

```text
id
organization_id
knowledge_space_id
title
slug
article_type
summary
owner_id
status
current_version_id
published_at
review_due_at
created_at
updated_at
version
```

Article types:

```text
article
how_to
best_practice
process
policy
faq
template
checklist
glossary
```

## KnowledgeVersion

Versionierte Inhalte werden separat gespeichert. Dadurch bleiben veröffentlichte Stände nachvollziehbar.

```text
id
article_id
version_number
content_json
summary
created_by
created_at
change_note
```

`content_json` beschreibt Block-Inhalte des Editors. Keine business-kritische Information wird ausschließlich als unstrukturiertes HTML persistiert.

Format (`BlockContent` im Backend prüft und normalisiert, unbekannte Felder werden verworfen):

```json
{ "blocks": [ { "id": "…", "type": "paragraph", "text": "…" } ] }
```

| Typ | Felder |
|---|---|
| `heading` | `level` (1–3), `text` |
| `paragraph`, `quote` | `text` |
| `callout` | `tone` (`info`/`warning`/`success`), `text` |
| `code` | `language` (optional), `code` |
| `bullet_list`, `numbered_list` | `items` (Texte) |
| `checklist` | `items` (`text`, `checked`) |
| `image` / `file` / `link` | `url` (nur `http(s)` oder Pfad mit `/`), `alt` / `name` / `label` |
| `task_reference` / `project_reference` / `knowledge_reference` | `taskId` / `projectId` / `articleId` |

Jede Speicherung erzeugt eine neue Version (`version_number` fortlaufend); Wiederherstellen speichert den alten Inhalt als neue Version. `knowledge_article.search_text` hält den Klartext der aktuellen Version für die Suche.

## KnowledgeRelation

```text
id
organization_id
source_article_id
target_article_id
relation_type
created_by
created_at
```

Relation types:

```text
RELATED
REQUIRES
PART_OF
SUPERSEDES
REFERENCES
```

## KnowledgeTag

Tags können Artikeln zugeordnet werden.

## KnowledgeComment

Kommentare auf Knowledge Articles. @Mentions verwenden die bestehende Notification-Infrastruktur.

## KnowledgePermission

Knowledge kann auf Organization-, Team- oder expliziter User-Ebene sichtbar gemacht werden. Backend-Authorization ist verbindlich. Regeln: `docs/PERMISSIONS.md` (Abschnitt Wissen).

## Gantt

Das Gantt-Diagramm ist eine Ansicht auf Tasks und braucht keine eigene Migration:

- Balken: `task.start_date`/`task.due_date` (beide inklusiv; ein Task mit nur einem Datum belegt einen Tag), Fortschritt aus `task.progress`, Hierarchie aus `task.parent_task_id`. Verschoben wird über `PATCH /tasks/{id}` mit `version`.
- `task_dependency`: Quelle → Ziel mit `dependency_type` (`finish_to_start`, `start_to_start`, `finish_to_finish`, `start_to_finish`). Beide Tasks im selben Projekt, höchstens eine Abhängigkeit je Paar, keine Kreise (geprüft unter einer Advisory-Sperre je Projekt). Abhängigkeiten weich gelöschter Tasks bleiben in der Tabelle, werden aber nicht mehr angezeigt.
- Verletzt ist eine Abhängigkeit, wenn `finish_to_start`: Ziel beginnt am oder vor dem Endtag der Quelle; `start_to_start`: Ziel beginnt vor der Quelle; `finish_to_finish`: Ziel endet vor der Quelle; `start_to_finish`: Ziel endet vor dem Beginn der Quelle. Sie wird nur angezeigt (DEC-023).
- `gantt_milestone`: Name und Datum je Projekt, versioniert.

## Cross-domain references

Knowledge kann referenzieren:

- Project
- Task
- Team
- Whiteboard

Die Referenzen sind Links auf die autoritativen Objekte und keine Kopien deren Business-State. Whiteboard-Verweise folgen mit dem Whiteboard-Modul (DEC-022).


# Identity & Organization (Migration 003)

- `organization.entra_tenant_id`: ordnet einen Entra-ID-Tenant (Token-Claim `tid`) genau einer Organisation zu. Eindeutig, falls gesetzt.
- `app_user.organization_role`: organisationsweite Rolle, `admin` oder `member` (Default). Projektrollen stehen weiterhin in `project_member.role`.

Ein Request wird über `tid` → Organisation und `oid` → `app_user.entra_object_id` innerhalb dieser Organisation einem aktiven Benutzer zugeordnet. Alle weiteren Abfragen starten von dieser `organization_id`.
