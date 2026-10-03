# Security Baseline

## Identity

Microsoft Entra ID / OIDC. Keine eigene Passwortauthentifizierung.

## Authorization

Serverseitig. Jeder geschützte Zugriff prüft mindestens:

```text
User -> Organization -> Project -> Resource -> Permission
```

Frontend-only authorization ist nicht ausreichend.

## Secrets

Secrets ausschließlich über Azure Key Vault / Secret Store.

Nie committen:

- Client Secrets
- Access Tokens
- Refresh Tokens
- API Keys
- DB Credentials

## Microsoft Graph

Systemmails vorzugsweise über dediziertes Funktionspostfach. Application Permissions restriktiv konfigurieren.

`Mail.Send` kann app-only das Senden ohne angemeldeten Benutzer erlauben; diese Application Permission benötigt Admin Consent und sollte über Exchange Application RBAC bzw. geeignete Zugriffsbeschränkungen auf notwendige Postfächer begrenzt werden.

## Webex

OAuth-Tokens verschlüsselt speichern. Webhooks validieren, deduplizieren und idempotent verarbeiten.

## Logging

Nicht loggen:

- Passwörter
- OAuth-Tokens
- Secrets
- unnötige Mail-/Chat-Inhalte
- unnötige personenbezogene Daten

## Audit

Sicherheitsrelevante Aktionen auditieren:

- Login/Logout
- Rollenänderungen
- Member-Änderungen
- Permission Changes
- Projekt-/Task-Löschungen
- Integrations-Änderungen

## App Security

Mindestens:

- TLS
- HSTS
- CSP
- Rate limiting
- Input Validation
- Output Encoding
- parameterized queries / EF Core
- dependency scanning
- secret scanning
- container scanning
- SAST
- DAST/Pentest vor Produktiv-Rollout

## Datenschutz

DSGVO und interne Richtlinien mit IT/Datenschutz/Security abstimmen. Die AI darf rechtliche Freigaben nicht voraussetzen.
