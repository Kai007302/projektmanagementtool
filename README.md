# ProjectHub AI Starter Workspace

Enterprise Project Management & Collaboration Platform für ca. 6.000 Mitarbeitende.

## Zweck

Dieses Repository ist die verbindliche technische Ausgangsbasis für die Entwicklung mit einem AI Coding Agent.

Enthalten sind:

- Produkt- und Architekturregeln
- AI-Agent-Vertrag
- Sicherheitsregeln
- Datenmodell und ER-Diagramm
- initiales PostgreSQL-Schema
- lokale Entwicklungsumgebung
- CI-Grundlage
- Prompts für Phase 0–2
- ADRs für zentrale Architekturentscheidungen

## Architektur in einem Satz

**Ein modularer Monolith für Projekte/Tasks/Kanban/Gantt/Notifications/Audit plus ein dedizierter Collaboration-Layer für Whiteboard-Realtime; Microsoft 365 und Webex bleiben externe Integrationen.**

## Technologie-Baseline

- Frontend: React 19.3 + TypeScript + Vite
- Backend: .NET 10 / ASP.NET Core
- Datenbank: PostgreSQL 18
- Realtime: SignalR/WebSockets + Redis für ephemeres Cross-Instance-Fan-out
- Durable Messaging: Azure Service Bus
- Whiteboard: Yjs/CRDT-basierter Collaboration Layer
- Storage: Azure Blob Storage
- Identity: Microsoft Entra ID
- Microsoft Integration: Microsoft Graph
- Webex: Webex REST API + Webhooks
- Hosting: Azure

Die Versionen wurden am 25.09.2026 gegen die offiziellen Quellen überprüft. Details und Links stehen in `docs/REFERENCES.md`.

## Lokaler Start

Voraussetzungen:

- Docker Desktop / Docker Engine
- Node.js 22 LTS
- .NET 10 SDK
- Git

### 1. Infrastruktur (PostgreSQL 18, Redis 8)

```bash
docker compose up -d
```

PostgreSQL: `postgresql://projecthub:projecthub_dev@localhost:5432/projecthub`
Redis: `redis://localhost:6379`

### 2. Backend

```bash
dotnet run --project src/ProjectHub.Api --launch-profile http
```

- läuft auf http://localhost:5080
- wendet beim Start alle noch fehlenden Skripte aus `database/migrations/` an (ADR 0005)
- `GET /health/live` → 200, sobald der Prozess läuft
- `GET /health/ready` → 200, wenn PostgreSQL und Redis erreichbar sind, sonst 503
- OpenAPI (nur Development): http://localhost:5080/openapi/v1.json

Fachliche Endpunkte unter `/api/v1` (Auszug):

| Bereich | Endpunkte |
|---|---|
| Identität | `GET /me`, `GET /organization`, `GET /users` |
| Teams | `GET/POST /teams`, `GET /teams/{id}`, `POST /teams/{id}/members`, `DELETE /teams/{id}/members/{userId}` |
| Projekte | `GET/POST /projects`, `GET/PATCH/DELETE /projects/{id}`, `POST /projects/{id}/members`, `PATCH/DELETE /projects/{id}/members/{userId}`, `GET /projects/{id}/activity` |
| Aufgaben | `GET/POST /projects/{id}/tasks`, `GET/PATCH/DELETE /tasks/{id}` |
| Kommentare | `GET/POST /tasks/{id}/comments`, `PATCH/DELETE /comments/{id}` |
| Dateien | `GET/POST /tasks/{id}/attachments`, `GET /attachments/{id}/content`, `DELETE /attachments/{id}` |
| Kanban | `GET /projects/{id}/board`, `POST /projects/{id}/board/columns`, `PATCH/DELETE /board-columns/{id}`, `POST /board-columns/{id}/move`, `POST /tasks/{id}/move` |
| Gantt | `GET /projects/{id}/gantt`, `POST /projects/{id}/gantt/dependencies`, `DELETE /task-dependencies/{id}`, `POST /projects/{id}/gantt/milestones`, `PATCH/DELETE /gantt-milestones/{id}`; Termine über `PATCH /tasks/{id}` |
| Benachrichtigungen | `GET /me/notifications`, `GET /me/notifications/unread-count`, `POST /notifications/{id}/read`, `POST /me/notifications/read-all`, `GET/PUT /me/notification-preferences`; Realtime-Hub `/api/v1/hubs/notifications`; Fake-Mails in Development unter `GET /dev/outbox`; Mail-Warteschlange für Organisations-Admins `GET /admin/mail-outbox`, `POST /admin/mail-outbox/{id}/retry` |
| Kalender | `GET /tasks/{id}/calendar.ics`, `GET /gantt-milestones/{id}/calendar.ics` |
| Webex | `GET /projects/{id}/webex`, `POST /projects/{id}/webex/links`, `POST /projects/{id}/webex/space`, `DELETE /webex-links/{id}`; für Organisations-Admins `GET /admin/webex`, `POST /admin/webex/webhook`; Webhook `POST /integrations/webex/webhook` (Signatur statt Anmeldung); Fake-Nachrichten in Development unter `GET /dev/webex` |
| Whiteboards | `GET/POST /projects/{id}/whiteboards`, `GET/PATCH/DELETE /whiteboards/{id}`, `GET /whiteboards/{id}/tasks?ids=…` (Live-Daten der Aufgabenkarten), `GET /tasks/{id}/whiteboards`, `GET /whiteboards/{id}/knowledge`; Sync-Hub `/api/v1/hubs/whiteboards` (`Join`, `PushUpdate`, `UpdatePresence`, `Leave`) |
| Wissen | `GET/POST /knowledge/spaces`, `PATCH /knowledge/spaces/{id}`, `GET/POST /knowledge/articles` (Suche `q`, Filter `type`, `status`, `spaceId`, `tag`), `GET/PATCH/DELETE /knowledge/articles/{id}`, `PUT /knowledge/articles/{id}/content`, `POST /knowledge/articles/{id}/status`, `GET /knowledge/articles/{id}/versions[/{n}]`, `POST /knowledge/articles/{id}/versions/{n}/restore`, `PUT /knowledge/articles/{id}/tags`, `GET /knowledge/tags`, `GET/PUT /knowledge/articles/{id}/permissions`, `POST /knowledge/articles/{id}/relations`, `DELETE /knowledge/relations/{id}`, `POST /knowledge/articles/{id}/references`, `DELETE /knowledge/references/{id}`, `GET/POST /knowledge/articles/{id}/comments`, `PATCH/DELETE /knowledge/comments/{id}`, `GET /projects/{id}/knowledge`, `GET /tasks/{id}/knowledge`, `GET /teams/{id}/knowledge`, `GET /whiteboards/{id}/knowledge`, `GET /knowledge/graph` (Knowledge Galaxy, Filter `spaceId`, `type`) |
| Realtime | SignalR-Hub `/api/v1/hubs/projects` (Methode `JoinProject`, Nachricht `ProjectChanged`), Fan-out über Redis |

Listen sind seitenweise (`limit` bis 100, `offset`). `PATCH` erwartet die aktuelle `version` und antwortet bei veralteter Version mit 409.

### 3. Frontend

```bash
cd src/ProjectHub.Web
npm install
npm run dev
```

- läuft auf http://localhost:5173 und leitet `/api` und `/health` an das Backend weiter
- anderes Backend: `PROJECTHUB_API_URL=http://localhost:1234 npm run dev`

### Konfiguration

Die Variablen aus `.env.example` werden als Umgebungsvariablen gelesen. Für die lokale Entwicklung stehen dieselben Werte bereits in `src/ProjectHub.Api/appsettings.Development.json`; gesetzte Umgebungsvariablen haben Vorrang.

Im Development-Modus legt die API beim Start synthetische Testdaten an (zwei Organisationen, Benutzer mit allen Rollen, Teams, Projekte; siehe `DevelopmentSeedData.cs`). Ein gekapselter Development-Identity-Provider meldet jede Anfrage als einen dieser Benutzer an: Standard ist `dev-ada` (Organisations-Admin), ein anderer lässt sich über den Header `X-Dev-User` bzw. im Frontend über „Dev-Anmeldung als“ wählen. Außerhalb von Development startet die API mit diesem Provider nicht. Alle Endpunkte verlangen standardmäßig einen angemeldeten Benutzer; Ausnahmen wie die Health-Checks sind explizit markiert. Rollen und Rechte: `docs/PERMISSIONS.md`.

Für Microsoft/Webex werden in der lokalen Entwicklung Mock-/Fake-Provider eingesetzt. Produktive Zugangsdaten werden nie aus dem Repository geladen.

### Tests und Checks

```bash
# Backend (Integrationstests starten PostgreSQL/Redis per Testcontainers, Docker muss laufen)
dotnet build
dotnet test

# Frontend
cd src/ProjectHub.Web
npm run lint
npm test -- --run
npm run build

# End-to-End (Playwright; PostgreSQL/Redis per docker compose, API und Vite startet Playwright selbst)
npx playwright install chromium   # einmalig
npm run e2e
```

### Projektstruktur

```text
src/ProjectHub.Api/                 ASP.NET Core API (modularer Monolith)
  Infrastructure/                   Querschnitt: Datenbank-Migrationen, Health-Checks
  Modules/<Modul>/                  ein Ordner pro fachlichem Modul (ARCHITECTURE.md)
src/ProjectHub.Web/                 React + TypeScript + Vite
tests/ProjectHub.Api.UnitTests/
tests/ProjectHub.Api.IntegrationTests/
database/migrations/                versionierte SQL-Skripte (einzige Schemaquelle)
```

## Verbindliche Dokumentation

Vor jeder Implementierung zuerst lesen:

1. `AGENTS.md`
2. `docs/PRODUCT.md`
3. `docs/ARCHITECTURE.md`
4. `docs/SECURITY.md` und `docs/PERMISSIONS.md`
5. `docs/DATA_MODEL.md`
6. `docs/OPEN_DECISIONS.md`
7. den relevanten Sprint-Prompt unter `ai/prompts/`

## Entwicklungsreihenfolge

```text
Phase 0  Foundation
Phase 1  Identity & Organization
Phase 2  Projects & Tasks
Phase 3  Knowledge Hub & Knowledge Galaxy
Phase 4  Kanban (vor Phase 3 umgesetzt)
Phase 5  Gantt
Phase 6  Notifications
Phase 7  Whiteboard
Phase 8  Microsoft 365
Phase 9  Webex
Phase 10 Enterprise Hardening
```

## Grundregel

Nicht alles auf einmal bauen. Nach jedem vertikalen Schritt müssen Build, Tests und Dokumentation funktionieren und der Git-Stand konsistent sein.
