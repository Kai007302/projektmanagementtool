# AI PROMPT — Phase 0 Foundation

Du arbeitest an ProjectHub.

## Pflichtlektüre

- `AGENTS.md`
- `README.md`
- `docs/PRODUCT.md`
- `docs/ARCHITECTURE.md`
- `docs/SECURITY.md`
- `docs/DATA_MODEL.md`

## Ziel

Erstelle eine vollständig startbare lokale Entwicklungsbasis, aber noch keine fachlichen ProjectHub-Features.

## Tasks

1. Initialisiere React 19.3 + TypeScript + Vite unter `src/ProjectHub.Web`.
2. Initialisiere .NET 10 / ASP.NET Core unter `src/ProjectHub.Api`.
3. Lege Feature-/Modulstruktur gemäß `AGENTS.md` an.
4. Integriere PostgreSQL und Redis über die vorhandene `docker-compose.yml`.
5. Implementiere `/health/live` und `/health/ready`.
6. Richte OpenAPI für Development ein.
7. Baue eine minimale Home-Seite.
8. Richte Umgebungsvariablen nach `.env.example` ein.
9. Erstelle Backend Unit- und Integration-Testprojekte.
10. Erstelle Frontend Test-/Lint-Setup.
11. Erstelle minimale GitHub Actions CI für Build und Tests.
12. Dokumentiere lokale Startbefehle.

## Development Auth

Noch keine produktive Entra-Integration. Verwende für lokale Tests eine klar gekapselte Development-Identity-Abstraktion.

## Nicht implementieren

- Projects
- Tasks
- Kanban
- Gantt
- Whiteboard
- Microsoft Graph
- Webex

## Acceptance Criteria

```text
docker compose up -d
Backend startet
Frontend startet
/health/live -> 200
/health/ready -> 200 wenn DB/Redis verfügbar sind
Backend build -> OK
Backend unit tests -> OK
Backend integration tests -> OK
Frontend build -> OK
Frontend tests -> OK
Frontend lint -> OK
```

Keine Secrets committen.
