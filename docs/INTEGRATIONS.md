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

### Kalender-Abo

Umgesetzt (ADR 0018), ebenfalls ohne Graph-Berechtigung: Jede Person kann unten auf jeder Seite eine geheime Kalender-Adresse erstellen und in Outlook („Aus dem Internet abonnieren“), Google oder Apple Kalender eintragen. Der Kalender enthält ihre Aufgaben mit Termin und die Meilensteine ihrer Projekte und aktualisiert sich selbst (`GET /api/v1/calendar-feed.ics?token=…`).

## Standardformate

Umgesetzt (ADR 0018):

- **Excel (.xlsx) und CSV:** Aufgaben eines Projekts exportieren und importieren (Ansicht „Liste“). CSV wie Excel in Deutschland (Semikolon, UTF-8 mit BOM); der Import liest auch Komma, Tab und Windows-1252 und versteht deutsche und englische Spaltennamen.
- **PDF:** das Gantt-Diagramm eines Projekts, A4 quer, vom Server gezeichnet.
- **iCalendar (.ics):** einzelne Termine (oben) und das Kalender-Abo.
- **JSON:** Export der eigenen Daten (ADR 0017).

## Webex

Umgesetzt (Phase 9, ADR 0011) über **einen zentralen Bot**. Personen verbinden kein eigenes Webex-Konto. Einrichtung: `docs/integrations/webex-setup.md`.

Der Transport wird mit `PROJECTHUB_WEBEX_TRANSPORT` gewählt: `off` (Standard außerhalb von Development, Webex ist in der Oberfläche ausgeblendet), `fake` (Standard in Development, Nachrichten unter `GET /dev/webex`) oder `bot`.

### Meetings

Projekt kann ein Webex Meeting referenzieren: Titel und Link (`https`, nur `webex.com` und Subdomains). ProjectHub plant keine Meetings.

### Spaces

Projekt kann einen Webex Space referenzieren, als Link oder als vom Bot angelegter Projektraum: Der Bot legt einen Raum mit dem Projektnamen an, lädt die aktiven Projektmitglieder per Mailadresse ein und postet einen Link ins Projekt. Höchstens ein aktiver Projektraum je Projekt. Spätere Änderungen an den Projektmitgliedern werden nicht übertragen.

### Benachrichtigungen

Wer „Per Webex“ einschaltet, bekommt Benachrichtigungen zusätzlich als Direktnachricht des Bots (nur Titel und Link). Zustellung über dieselbe Outbox wie Mails (`mail_outbox.channel = 'webex'`) mit Wiederholung; ein Webex-Ausfall verzögert nur diese Nachrichten.

### Webhooks

Incoming webhooks (`POST /api/v1/integrations/webex/webhook`) werden:

- validiert (`X-Spark-Signature`, HMAC-SHA1 mit `WEBEX_WEBHOOK_SECRET`, höchstens 64 KB)
- dedupliziert (`webhook_event`, Schlüssel `resource:event:data.id`)
- idempotent verarbeitet (nur Zustände setzen, in derselben Transaktion)
- mit Correlation ID geloggt

Ausgewertet wird bisher `memberships:deleted` für den Bot: Der Projektraum gilt dann als getrennt. Registriert wird der Webhook von einem Organisations-Admin (`POST /admin/webex/webhook`).

### Future

Recordings und Transcripts können später über externe IDs/Links mit Projekten verknüpft werden. Verfügbarkeit hängt von Webex-Rechten und Organisationsrichtlinien ab.


## Knowledge / AI

AI ist eine optionale interne Plattform-Schicht. Externe LLM-Anbieter dürfen nicht ohne explizite Unternehmensfreigabe verwendet werden.

Die Retrieval-Schicht muss Berechtigungen bereits beim Retrieval erzwingen. Es ist nicht ausreichend, erst im Prompt zu erwähnen, dass bestimmte Dokumente verboten sind.

Für die erste Version bleibt die Suche provider-neutral. Eine Embedding-/Vector-Implementierung wird hinter Interfaces gekapselt.

Umsetzung (ADR 0015):

- **Sprachmodell:** `PROJECTHUB_AI_PROVIDER` = `off` (Standard), `anthropic` (Claude über das offizielle SDK, `ANTHROPIC_API_KEY`) oder `openai` (OpenAI-kompatibler Endpunkt, auch lokal, `PROJECTHUB_AI_BASE_URL`). Der Assistent hängt nicht an der Verfügbarkeit des Anbieters: Fällt er aus, endet die Antwort mit einer Fehlermeldung, alles andere läuft weiter.
- **MCP-Server:** `PROJECTHUB_MCP=on` stellt `/api/v1/mcp` bereit (Streamable HTTP). Anmeldung mit Entra-ID-Token, Protected Resource Metadata unter `/.well-known/oauth-protected-resource/api/v1/mcp`. Einrichtung der Clients: `docs/SELF_HOSTING.md`.
