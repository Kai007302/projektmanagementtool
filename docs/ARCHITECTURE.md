# Architecture

## Zielarchitektur

```text
                         Browser
                            |
                         HTTPS
                            |
                     Azure Front Door/WAF
                            |
                 +----------+----------+
                 |                     |
             React Web            SignalR/WebSocket
                 |                     |
                 +----------+----------+
                            |
                     ASP.NET Core API
                            |
     +----------+-----------+-----------+-----------+
     |          |           |           |           |
  Domain    Realtime    Notifications  Search     Integrations
     |          |           |             |           |
     |        Redis      Service Bus      |      +----+----+
     |          |           |             |      |         |
     +----------+-----------+-------------+   Graph     Webex
                |
            PostgreSQL
                |
           Blob Storage
```

## Module boundaries

- Identity
- Organizations
- Users
- Teams
- Projects
- Tasks
- Kanban
- Gantt
- Whiteboard
- Notifications
- Search
- Audit
- Integrations

## Modularer Monolith

Alle Core-Module liegen zunächst in derselben Anwendung, besitzen aber eigene fachliche Grenzen.

Cross-module communication:

- definierte Interfaces
- Application Services
- Domain Events

Vermeide direkten Zugriff auf die internen Implementierungsdetails anderer Module.

## Source of Truth

PostgreSQL ist autoritativ für Projekt-/Task-Daten.

Redis, SignalR, Service Bus und externe Systeme sind keine Source of Truth für den Projektstatus.

## Task Flow

```text
Browser
  -> API
  -> Authorization
  -> Domain
  -> PostgreSQL transaction
  -> durable event/job
  -> Worker
  -> Outlook/Webex
```

Externe API-Ausfälle dürfen erfolgreiche Kerntransaktionen nicht rückabwickeln.

## Kanban Realtime

```text
User A
  -> API
  -> DB transaction
  -> TaskMoved
  -> Redis fan-out
  -> SignalR
  -> User B/C/D
```

## Whiteboard

Whiteboard nutzt CRDT/Yjs. Kein vollständiger Canvas-State-Broadcast bei jeder Änderung.

Persistenz: Updates + Snapshots/Compaction.

## Horizontal Scaling

API-Instanzen müssen stateless sein.

Shared State befindet sich in:

- PostgreSQL
- Redis
- Blob Storage
- Service Bus

## Nicht-Ziele

- keine Microservices per default
- kein Event Sourcing als Grundarchitektur
- kein Kafka in V1
- keine eigene Mail-/Kalenderplattform


## Knowledge Hub

Knowledge ist ein eigenes Core-Modul. Es besteht aus strukturierten Articles, Versionen, Tags, Relationen, Anhängen, Kommentaren und Berechtigungen.

```text
Knowledge Hub
  ├── Articles
  ├── Versions
  ├── Tags
  ├── Relations
  ├── Attachments
  ├── Permissions
  └── Comments
```

Der Knowledge Galaxy ist eine Read/Navigation-View auf diesen Daten. Er darf keinen eigenen fachlichen Zustand führen.

### Knowledge Graph

Relations können u. a. folgende Typen haben:

```text
RELATED
REQUIRES
PART_OF
SUPERSEDES
REFERENCES
```

### Knowledge Editor

Ein blockbasierter Editor wird bevorzugt. Inhalte müssen einfach pflegbar sein und später lokalisierbar bleiben.

### AI / RAG

AI ist eine optionale Schicht über dem Knowledge Hub. Die Retrieval-Schicht muss vor der Generierung die Benutzerberechtigungen berücksichtigen. Keine Cross-Project- oder Cross-Organization-Leaks.

```text
User Query
   ↓
Authorization Context
   ↓
Semantic / Full-Text Retrieval
   ↓
Authorized Knowledge Chunks
   ↓
LLM
   ↓
Answer + Source References
```

V1 kann zunächst PostgreSQL Full-Text Search verwenden. Embeddings/vector search werden hinter einem Interface gekapselt und später ergänzt.
