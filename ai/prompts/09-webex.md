# AI PROMPT — Phase 9 Webex

Du arbeitest an ProjectHub.

## Pflichtlektüre

- `AGENTS.md` (Abschnitte Security und Integrationen/Webex)
- `docs/INTEGRATIONS.md` (Webex), `docs/SECURITY.md`
- `docs/adr/0010-mail-outbox-graph.md`
- DEC-009 (Webex App Ownership)

## Ziel

Projekte lassen sich mit Webex-Meetings und -Spaces verknüpfen, ein Projektraum lässt sich mit einem Klick anlegen, und Benachrichtigungen kommen auf Wunsch auch per Webex. Webhooks von Webex werden geprüft, nur einmal verarbeitet und protokolliert. Kerntransaktionen hängen nie an Webex.

## Identität bei Webex (Vorschlag ADR 0011, DEC-009)

- ProjectHub spricht als **ein zentraler Bot** mit Webex. Personen melden sich nicht bei Webex an; es werden **keine persönlichen Tokens** gespeichert.
- Das Bot-Token liegt nur im Secret Store (`WEBEX_BOT_TOKEN`), nie im Browser, nie in der Datenbank.
- Versandweg per `PROJECTHUB_WEBEX_TRANSPORT`: `off` (Standard außerhalb von Development), `fake` (Standard in Development, nur im Speicher, `GET /api/v1/dev/webex`) oder `bot`.
- Meetings werden als Link hinterlegt, nicht über die API geplant. Planen im Namen einer Person (OAuth je Person, `meeting:schedules_write`) bleibt offen.

## Verknüpfungen am Projekt

Neue Tabelle `project_webex_link` (Migration 010): Art (`meeting`, `space`), Titel, Link, bei vom Bot angelegten Räumen die Raum-ID und ein Status (`active`, `disconnected`).

```text
GET    /api/v1/projects/{projectId}/webex             Verknüpfungen und ob Webex verfügbar ist
POST   /api/v1/projects/{projectId}/webex/links       { kind, title, url }   nur https-Links auf webex.com
POST   /api/v1/projects/{projectId}/webex/space       Projektraum anlegen: Bot erstellt den Raum, lädt die Projektmitglieder ein, begrüßt
DELETE /api/v1/webex-links/{id}
```

- Ansehen: View. Anlegen und Entfernen: Edit. Aktivitäts-Log wie bei anderen Projektänderungen.
- Höchstens ein aktiver Projektraum je Projekt.
- Spätere Mitgliederänderungen werden nicht automatisch nach Webex übertragen.

## Benachrichtigungen per Webex

- Die Outbox aus ADR 0010 bekommt einen Kanal (`email`, `webex`). Der Dispatcher legt bei eingeschalteter Einstellung eine Webex-Zeile an; der Worker schickt sie als Direktnachricht des Bots an die Mail-Adresse der Person.
- Inhalt wie bei Mails: nur Titel und Link.
- Einstellung „Per Webex“ (`notification_preference.webex_enabled`, Standard aus) erscheint nur, wenn Webex verfügbar ist.
- Wiederholung und Fehlerklassen wie bei Mails.

## Webhooks

```text
POST /api/v1/integrations/webex/webhook        anonym, Signatur Pflicht
POST /api/v1/admin/webex/webhook               Webhook beim Bot registrieren (Organisations-Admin)
GET  /api/v1/admin/webex                       Zustand: Versandweg, registrierte Webhooks
```

- **Validiert:** `X-Spark-Signature` (HMAC-SHA1 über den Rohinhalt mit `WEBEX_WEBHOOK_SECRET`), Vergleich in konstanter Zeit. Ohne gültige Signatur 401, ohne Secret nimmt der Endpunkt nichts an.
- **Dedupliziert:** jedes Ereignis bekommt einen Schlüssel aus Ressource, Ereignis und Objekt-ID; neue Tabelle `webhook_event` mit eindeutigem Schlüssel. Wiederholte Zustellungen antworten 200 und tun nichts.
- **Idempotent:** Verarbeitung setzt nur Zustände (z. B. Raum `disconnected`), zählt nichts hoch.
- **Correlation ID** im Log und in `webhook_event`.
- Verarbeitet wird zunächst: Bot aus einem Projektraum entfernt (`memberships`/`deleted`) → Verknüpfung `disconnected`. Andere Ereignisse werden angenommen und ignoriert.

## Frontend

- Projektansicht: Bereich „Webex“ mit Meetings und Räumen, Öffnen in neuem Fenster, Hinzufügen und Entfernen (Edit), „Projektraum in Webex anlegen“.
- Glocke: Einstellung „Per Webex“, wenn verfügbar.

## IT-Freigabe

`docs/integrations/webex-setup.md`: Bot anlegen, Token und Webhook-Secret im Secret Store, öffentliche Adresse für den Webhook, Konfiguration.

## Acceptance Criteria

1. Meeting- und Raum-Links lassen sich am Projekt hinterlegen und entfernen; nur Webex-Links werden angenommen
2. Ein Projektraum wird mit den Projektmitgliedern angelegt; ein zweiter wird abgelehnt
3. Mit „Per Webex“ kommt eine Benachrichtigung als Direktnachricht mit Titel und Link
4. Webhooks ohne gültige Signatur werden abgelehnt, doppelte Zustellungen ändern nichts
5. Entfernt jemand den Bot aus dem Projektraum, zeigt ProjectHub den Raum als getrennt
6. Fällt Webex aus, funktionieren Projekte, Aufgaben und Benachrichtigungen in der App weiter

Tests müssen Rechte, Link-Prüfung, Raum anlegen (mit Fake), Webex-Benachrichtigungen über die Outbox, Webhook-Signatur, Deduplizierung, Verarbeitung, den Bot-Client (Fake-HTTP) und den E2E-Ablauf abdecken.
