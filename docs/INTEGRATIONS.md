# Integrations

## Microsoft 365

### Identity

Entra ID / OIDC.

### Mail

Backend-only über Microsoft Graph.

Empfohlener Flow:

```text
Domain Event -> Notification Service -> Service Bus -> Graph Mail Sender
```

### Calendar

Keine eigene Kalenderdatenbank. Nur gezielte Aktionen wie "Outlook-Termin erstellen".

## Webex

### Meetings

Projekt kann ein Webex Meeting referenzieren.

### Spaces

Projekt kann einen Webex Space referenzieren.

### Webhooks

Incoming webhooks werden:

- validiert
- dedupliziert
- idempotent verarbeitet
- mit Correlation ID geloggt

### Future

Recordings und Transcripts können später über externe IDs/Links mit Projekten verknüpft werden. Verfügbarkeit hängt von Webex-Rechten und Organisationsrichtlinien ab.


## Knowledge / AI

AI ist eine optionale interne Plattform-Schicht. Externe LLM-Anbieter dürfen nicht ohne explizite Unternehmensfreigabe verwendet werden.

Die Retrieval-Schicht muss Berechtigungen bereits beim Retrieval erzwingen. Es ist nicht ausreichend, erst im Prompt zu erwähnen, dass bestimmte Dokumente verboten sind.

Für die erste Version bleibt die Suche provider-neutral. Eine Embedding-/Vector-Implementierung wird hinter Interfaces gekapselt.
