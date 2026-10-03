# Data Model

ER-Diagramm: `docs/diagrams/er.mmd`

SQL-Startschema: `database/migrations/001_initial.sql`

Spätere Änderungen liegen als fortlaufende Skripte daneben (ADR 0005):

- `003_identity_organization.sql`: `organization.entra_tenant_id`, `app_user.organization_role`
- `004_project_soft_delete.sql`: `project.deleted_at` (Projekte werden nur weich gelöscht)

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

Knowledge kann auf Organization-, Team- oder expliziter User-Ebene sichtbar gemacht werden. Backend-Authorization ist verbindlich.

## Cross-domain references

Knowledge kann referenzieren:

- Project
- Task
- Team
- Whiteboard

Die Referenzen sind Links auf die autoritativen Objekte und keine Kopien deren Business-State.


# Identity & Organization (Migration 003)

- `organization.entra_tenant_id`: ordnet einen Entra-ID-Tenant (Token-Claim `tid`) genau einer Organisation zu. Eindeutig, falls gesetzt.
- `app_user.organization_role`: organisationsweite Rolle, `admin` oder `member` (Default). Projektrollen stehen weiterhin in `project_member.role`.

Ein Request wird über `tid` → Organisation und `oid` → `app_user.entra_object_id` innerhalb dieser Organisation einem aktiven Benutzer zugeordnet. Alle weiteren Abfragen starten von dieser `organization_id`.
