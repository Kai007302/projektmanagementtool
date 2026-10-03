# AI PROMPT — Phase 1 Identity & Organization

Du arbeitest an ProjectHub.

## Pflichtlektüre

- `AGENTS.md`
- `docs/PRODUCT.md`
- `docs/ARCHITECTURE.md`
- `docs/SECURITY.md`
- `docs/DATA_MODEL.md`
- `docs/OPEN_DECISIONS.md`
- Phase-0-Ergebnis

## Ziel

Implementiere Identity, Organization, Users, Teams und serverseitige Berechtigungen.

## Identity

- Ziel: Microsoft Entra ID / OIDC.
- Lokal: gekapselter Development Identity Provider.
- Keine eigene Passwortauthentifizierung.
- Lokaler User besitzt `entra_object_id`.

## Domain

Implementieren:

- Organization
- User
- Team
- TeamMember

## Rollen

Mindestens:

- OrganizationAdmin
- ProjectAdmin
- ProjectEditor
- ProjectMember
- ProjectViewer
- Guest

Project-Rollen können zunächst als Enum/Const im Application-Layer umgesetzt werden, müssen aber zentral definiert sein.

## Authorization

Baue eine zentrale Autorisierungsabstraktion, zum Beispiel:

```text
IAuthorizationService
  CanViewProject
  CanEditProject
  CanManageProject
  CanManageOrganization
```

Security muss serverseitig erfolgen.

## API

Mindestens:

```text
GET  /api/v1/me
GET  /api/v1/organization
GET  /api/v1/users
GET  /api/v1/teams
POST /api/v1/teams
POST /api/v1/teams/{id}/members
```

## Tests

Nachweisen:

- kein Zugriff ohne Berechtigung
- Viewer kann lesen, aber nicht ändern
- OrganizationAdmin darf Organisation verwalten
- Project-Scoping funktioniert
- Cross-organization access ist blockiert

## Acceptance Criteria

Ein synthetischer Benutzer kann:

1. sich im Dev-Modus anmelden
2. seine Identität abrufen
3. seine Organisation sehen
4. Teams anzeigen
5. Teams anlegen, falls berechtigt
6. Mitglieder verwalten, falls berechtigt

Alle Authorization- und Integrationstests müssen grün sein.
