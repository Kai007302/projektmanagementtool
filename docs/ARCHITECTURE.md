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
- Departments (Abteilungen, ADR 0021)
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

Umsetzung (ADR 0009): eigener SignalR-Hub im API-Server, Updates werden geprüft, in `whiteboard_update` gespeichert und dann über Redis an die anderen Instanzen verteilt. Ein Hintergrunddienst fasst ruhige Whiteboards zu Snapshots zusammen. Yjs-Dekodierung (yrs) läuft nur in einem isolierten Kindprozess, weil manche fehlerhaften Updates den Prozess beenden. Presence ist flüchtig und wird nie gespeichert.

## Auslieferung

Zwei Container (ADR 0012): Nginx liefert die Oberfläche aus und leitet `/api` (einschließlich WebSockets) und `/health` an die API weiter; Browser sprechen nur mit dieser einen Origin. TLS endet am Ingress (Front Door/App Service/Container Apps), die API ist nur intern erreichbar und übernimmt Client-Adresse und Schema aus genau einem Proxy-Eintrag.

## Anmeldung

Außerhalb von Development meldet die Oberfläche Personen per MSAL (Authorization Code mit PKCE, Weiterleitung) bei Entra ID an und schickt das Access Token an die API; Mandant und Client-ID liefert `GET /api/v1/sign-in`. Optional legt die API Personen beim ersten Login an (ADR 0014).

## Betrieb

Telemetrie per OpenTelemetry/OTLP, sobald `OTEL_EXPORTER_OTLP_ENDPOINT` gesetzt ist; Logs außerhalb von Development als JSON mit Trace-ID; Migrationen beim Start unter einer PostgreSQL-Advisory-Sperre (ADR 0013). Probes, Skalierung, Backup/Restore und Ausfälle: `docs/OPERATIONS.md`.

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

Umsetzung (ADR 0015): Der Assistent (`/api/v1/ai/chat`, gestreamt per Server-Sent Events) spricht über `IChatClient` (Microsoft.Extensions.AI) mit dem konfigurierten Modell, Standard Claude. Das Modell sucht selbst mit Werkzeugen (`ProjectHubTools`), die als die angemeldete Person über die vorhandenen Services laufen; `IKnowledgeRetrieval` liefert Passagen aus der PostgreSQL-Volltextsuche, gefiltert mit `KnowledgeAccess.Visible`. Dieselben Werkzeuge bietet der MCP-Server (`/api/v1/mcp`) externen Agenten an. Schreibwerkzeuge führt der Assistent erst nach Freigabe der Person aus; der wartende Schritt geht verschlüsselt an den Browser und zurück (ADR 0016). Embeddings/Vektorsuche kommen bei Bedarf hinter `IKnowledgeRetrieval` dazu (DEC-036).
