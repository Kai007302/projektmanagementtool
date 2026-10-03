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

Ein zentraler Bot (ADR 0011). `WEBEX_BOT_TOKEN` und `WEBEX_WEBHOOK_SECRET` kommen nur aus dem Secret Store, nie in den Browser und nie ins Log; Fehlermeldungen tragen nur den HTTP-Status. ProjectHub speichert keine persönlichen Webex-Tokens. Falls später OAuth je Person dazukommt: Tokens verschlüsselt speichern.

Webhooks validieren (HMAC-SHA1-Signatur, Vergleich in konstanter Zeit, ohne Secret wird nichts angenommen), deduplizieren und idempotent verarbeiten. Meeting- und Raum-Links nur als `https` auf `webex.com`.

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

Umsetzung und Werte: ADR 0012. TLS und die Weiterleitung auf HTTPS übernimmt der Ingress; HSTS, CSP und die übrigen Header setzen Web-Container und API; Rate Limits und Größenlimits die API; Dependency-, Secret- und Container-Scanning sowie SAST (CodeQL) laufen in der CI, eine ZAP-Baseline (DAST) wöchentlich. Pentest vor dem Rollout bleibt offen.

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
