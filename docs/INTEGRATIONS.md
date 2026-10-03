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

Umgesetzt (Phase 8, ADR 0010), bis Azure Service Bus verfügbar ist:

```text
Domain Event -> NotificationDispatcher -> mail_outbox (PostgreSQL, gleiche Transaktion) -> MailOutboxWorker -> IEmailSender (Graph)
```

- Versand app-only aus dem Funktionspostfach: `POST /users/{Postfach}/sendMail`, ohne Kopie in „Gesendete Elemente“.
- Wiederholung bei vorübergehenden Fehlern, `failed` bei dauerhaften; Admins sehen den Zustand unter `GET /api/v1/admin/mail-outbox`.
- Einrichtung für die IT: `docs/integrations/microsoft-365-setup.md`.

### Calendar

Keine eigene Kalenderdatenbank. Nur gezielte Aktionen wie "Outlook-Termin erstellen".

Umgesetzt (Phase 8, Vorschlag DEC-028) ohne Graph-Berechtigung:

- `GET /api/v1/tasks/{id}/calendar.ics` und `GET /api/v1/gantt-milestones/{id}/calendar.ics`: ganztägiger Termin mit Titel und Link, feste UID (erneuter Import ersetzt den alten Eintrag).
- „In Outlook anlegen“ öffnet Outlook im Web mit vorausgefülltem Termin im Browser der Person.

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
