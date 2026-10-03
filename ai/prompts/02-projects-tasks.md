# AI PROMPT — Phase 2 Projects & Tasks

Du arbeitest an ProjectHub.

## Pflichtlektüre

- `AGENTS.md`
- `docs/PRODUCT.md`
- `docs/ARCHITECTURE.md`
- `docs/SECURITY.md`
- `docs/DATA_MODEL.md`
- Datenbankmigration
- Ergebnis von Phase 1

## Ziel

Implementiere den fachlichen Kern: Projects, Memberships, Tasks, Subtasks, Comments, Attachment-Metadaten und Activity/Audit.

## Projects

CRUD:

```text
POST   /api/v1/projects
GET    /api/v1/projects
GET    /api/v1/projects/{id}
PATCH  /api/v1/projects/{id}
DELETE /api/v1/projects/{id}
```

Membership:

```text
POST   /api/v1/projects/{id}/members
PATCH  /api/v1/projects/{id}/members/{userId}
DELETE /api/v1/projects/{id}/members/{userId}
```

## Tasks

```text
POST   /api/v1/projects/{projectId}/tasks
GET    /api/v1/projects/{projectId}/tasks
GET    /api/v1/tasks/{id}
PATCH  /api/v1/tasks/{id}
DELETE /api/v1/tasks/{id}
```

Task-Felder:

- title
- description
- status
- priority
- assignee
- parent task
- start date
- due date
- progress
- estimated hours

## Comments

```text
GET    /api/v1/tasks/{id}/comments
POST   /api/v1/tasks/{id}/comments
PATCH  /api/v1/comments/{id}
DELETE /api/v1/comments/{id}
```

Mentions als Domain Event vorbereiten.

## Attachments

In dieser Phase nur Metadata + Storage Interface.

```text
IAttachmentStorage
```

Lokale Implementierung darf ein Development-Verzeichnis verwenden. Azure Blob folgt später.

## Audit / Activity

Mindestens:

```text
ProjectCreated
ProjectUpdated
ProjectDeleted
MemberAdded
MemberRemoved
TaskCreated
TaskUpdated
TaskDeleted
CommentAdded
```

## Concurrency

Optimistic concurrency mit `version`.

Ein veraltetes Update darf keine neuere Version überschreiben.

Konflikte sauber als `409 Conflict` behandeln.

## Business Rules

- Task gehört zum Project.
- Parent Task gehört zum gleichen Project.
- Assignee muss entsprechend der Permission-Policy zulässig sein.
- Start/Ende dürfen nicht inkonsistent sein.
- Progress liegt zwischen 0 und 100.
- Gelöschte Tasks erscheinen nicht in normalen Listen.
- Cross-organization access muss unmöglich sein.

## Acceptance Criteria

Ein berechtigter Benutzer kann:

1. Projekt erstellen
2. Mitglieder hinzufügen
3. Task erstellen
4. Task zuweisen
5. Subtask erstellen
6. kommentieren
7. Activity sehen
8. Optimistic-Concurrency-Konflikt reproduzieren und korrekt behandeln

Tests müssen Ownership, Authorization, Validation, Concurrency und Cross-organization access abdecken.
