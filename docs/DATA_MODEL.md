# Data Model

ER-Diagramm: `docs/diagrams/er.mmd`

SQL-Startschema: `database/migrations/001_initial.sql`

Spätere Änderungen liegen als fortlaufende Skripte daneben (ADR 0005):

- `003_identity_organization.sql`: `organization.entra_tenant_id`, `app_user.organization_role`
- `004_project_soft_delete.sql`: `project.deleted_at` (Projekte werden nur weich gelöscht)
- `005_kanban_column_status.sql`: `kanban_column.task_status`. Jede Spalte steht für einen Task-Status; die Spalte einer Karte ergibt sich aus Status, `task.kanban_column_id` und `task.board_position` (Kanban bleibt eine Ansicht auf Tasks)
- `006_knowledge_visibility_search.sql`: `knowledge_article.visibility` (`organization`/`restricted`, DEC-020), `knowledge_article.search_text` (Klartext des aktuellen Inhalts) und ein GIN-Index für die Volltextsuche (`german`) über Titel, Zusammenfassung und `search_text`
- `007_notification_unread.sql`: Teilindex auf ungelesene Benachrichtigungen je Person (Zähler der Glocke)
- `008_whiteboard_updates.sql`: `whiteboard_update` (angenommene Yjs-Updates seit dem letzten Snapshot) und Indizes für Whiteboards je Projekt und Verweise je Aufgabe
- `009_mail_outbox.sql`: `mail_outbox` (Benachrichtigungsmails bis zum Versand, ADR 0010)
- `010_webex.sql`: `project_webex_link` (Webex-Räume und Besprechungslinks je Projekt), `mail_outbox.channel` (`email`/`webex`) und `webhook_event` (empfangene Webhooks zur Deduplizierung, ADR 0011)
- `012_privacy.sql`: `app_user.anonymized_at` (anonymisierte Personen, ADR 0017) und Indizes über `created_at` für die Löschfristen von Benachrichtigungen, Aktivität und Audit-Log
- `011_knowledge_search_vector.sql`: `knowledge_article.search_vector`, von PostgreSQL generierter Suchvektor mit GIN-Index; ersetzt den Ausdrucksindex aus 006 (ADR 0013)
- `013_calendar_feed.sql`: `calendar_feed` (geheime Adresse des Kalender-Abos je Person, ADR 0018)

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

## Notifications

- `notification`: eine Nachricht für genau eine Person (`user_id`) mit `type` (`task_assigned`, `task_comment_mention`, `knowledge_comment_mention`, `project_member_added`), Titel, optionalem Auszug (höchstens 200 Zeichen), Verweis auf die Ressource (`task`, `knowledge_article`, `project`) und `read_at`.
- `notification_preference`: Kanäle je Person; ohne Zeile sind In-App und Mail an. `webex_enabled` schaltet Direktnachrichten des Webex-Bots ein (Standard aus).
- `mail_outbox`: eine ausgehende Benachrichtigung an eine Person über `channel` (`email`, `webex`; Migration 010) mit Adresse, Betreff und Text (nur Titel und Link), `status` (`pending`, `sent`, `failed`), `attempts`, `next_attempt_at` (nächster Versuch bzw. Ende der Sperrfrist während des Versands) und `last_error` (nur HTTP-Status und Graph-Fehlercode). Verschickte Zeilen werden nach 7 Tagen gelöscht, fehlgeschlagene nach 30 Tagen.
- Zustellung und Mail siehe ADR 0008 und ADR 0010, Webex ADR 0011.

## Kalender-Abo

Migration 013 (ADR 0018):

- `calendar_feed`: höchstens eine Zeile je Person (`user_id` eindeutig) mit `token_hash` (SHA-256 des Tokens in der Adresse, 32 Bytes, eindeutig), `created_at` und `last_used_at` (höchstens alle 15 Minuten aktualisiert). Das Token selbst wird nicht gespeichert. Die Anonymisierung einer Person löscht die Zeile.

## Webex

Migration 010 (ADR 0011):

- `project_webex_link`: Meeting- oder Raum-Link eines Projekts mit `kind` (`meeting`, `space`), `title` (1–200), `url` (höchstens 2000), `status` (`active`, `disconnected`) und `created_by`. Vom Bot angelegte Räume tragen `room_id`; je Projekt höchstens ein aktiver Raum mit `room_id`.
- `webhook_event`: angenommene Webhook-Zustellungen mit `provider`, `event_key` (eindeutig je Provider), `resource`, `event`, `correlation_id`, `received_at`. Nach 30 Tagen gelöscht.
- Die vorhandenen Tabellen `integration` und `webhook_subscription` halten die Webex-Integration der Organisation und die registrierten Webhooks (`last_event_at`).

## Whiteboard

Whiteboards sind Yjs-Dokumente (ADR 0004, ADR 0009). Die Tabellen halten den Inhalt nur binär, nie als Kopie von Geschäftsdaten:

- `whiteboard`: Name je Projekt, versioniert; `collaboration_document_id` ist die Kennung des Yjs-Dokuments.
- `whiteboard_update`: jedes angenommene Update mit fortlaufender `sequence_number` je Whiteboard, `created_by` und Zeitpunkt.
- `whiteboard_snapshot`: zusammengeführter Stand bis einschließlich `sequence_number`; die Bytes liegen unter `storage_key` im Snapshot-Speicher (Development: `.data/whiteboards`). Die letzten zwei bleiben erhalten.
- `whiteboard_reference`: welche Aufgabe auf welchem Objekt liegt. Wird bei der Compaction aus dem Dokument abgeleitet; nur aktive Aufgaben desselben Projekts.
- Im Dokument: Root-Map `objects`, je Objekt eine Map mit `type` (`sticky`, `rect`, `ellipse`, `diamond`, `text`, `arrow`, `task`), `x`, `y`, `w`, `h` (Pfeile: `x2`, `y2`), `color`, `text` und bei Aufgabenkarten nur `taskId`.
- Vorlagen (Mindmap, SWOT, Retrospektive usw.) sind nur Client-Code: Einfügen legt gewöhnliche Objekte in einer Transaktion an; es gibt keinen eigenen Vorlagen-Typ im Dokument. Ältere Clients überspringen unbekannte Typen wie `diamond`.
- `color` ist eine von 16 Farben (`white`, `yellow`, `orange`, `red`, `pink`, `violet`, `blue`, `cyan`, `teal`, `green`, `lime`, `sand`, `gray`, `navy`, `purple`, `black`); unbekannte Werte zeigt der Client in der Standardfarbe des Typs.

## Cross-domain references

Knowledge kann referenzieren:

- Project
- Task
- Team
- Whiteboard

Die Referenzen sind Links auf die autoritativen Objekte und keine Kopien deren Business-State. Ein Verweis ist nur sichtbar, wer das Ziel sehen darf; Verweise auf gelöschte Whiteboards bleiben verborgen.


# Identity & Organization (Migration 003)

- `organization.entra_tenant_id`: ordnet einen Entra-ID-Tenant (Token-Claim `tid`) genau einer Organisation zu. Eindeutig, falls gesetzt.
- `app_user.organization_role`: organisationsweite Rolle, `admin` oder `member` (Default). Projektrollen stehen weiterhin in `project_member.role`.

Ein Request wird über `tid` → Organisation und `oid` → `app_user.entra_object_id` innerhalb dieser Organisation einem aktiven Benutzer zugeordnet. Alle weiteren Abfragen starten von dieser `organization_id`.
