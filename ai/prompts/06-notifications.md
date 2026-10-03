# AI PROMPT — Phase 6 Notifications

Du arbeitest an ProjectHub.

## Pflichtlektüre

- `AGENTS.md`
- `docs/ARCHITECTURE.md`, `docs/INTEGRATIONS.md` (Mail: `Domain Event -> Notification Service -> Service Bus -> Graph Mail Sender`)
- `docs/adr/0003-realtime-boundaries.md`
- `docs/PERMISSIONS.md`
- Tabellen `notification` und `notification_preference` (Migration 001)

## Ziel

Personen erfahren, wenn etwas sie betrifft: in der App (Glocke mit Zähler) und per Mail. Mail ist in dieser Phase ein **Fake**: Nachrichten landen in einem Postausgang im Speicher und im Log, es wird nichts verschickt. Der echte Versand über Microsoft Graph folgt in Phase 8 hinter derselben Schnittstelle `IEmailSender`.

## Auslöser (Vorschlag DEC-026)

| Typ | Wer wird benachrichtigt | Auslöser |
|---|---|---|
| `task_assigned` | neue zuständige Person | Aufgabe wird jemandem zugewiesen (beim Anlegen oder Ändern) |
| `task_comment_mention` | erwähnte Personen | `@`-Erwähnung in einem Aufgabenkommentar |
| `knowledge_comment_mention` | erwähnte Personen | `@`-Erwähnung in einem Kommentar zu einem Wissensartikel |
| `project_member_added` | hinzugefügte Person | Person wird Mitglied eines Projekts |

- Wer etwas selbst auslöst, wird dafür nie benachrichtigt.
- Die Module veröffentlichen nach dem Commit Domain Events. Der Notification-Service reagiert darauf; die Module kennen ihn nicht.
- Ein Fehler beim Benachrichtigen macht die ursprüngliche Änderung nie rückgängig und lässt den Request nicht scheitern (wird geloggt).

## Inhalt

- Titel auf Deutsch mit Name der auslösenden Person und dem Titel der Aufgabe, des Artikels oder Projekts.
- In der App zusätzlich ein kurzer Auszug (höchstens 200 Zeichen) des Kommentars bei Erwähnungen.
- **Mails enthalten keinen Kommentartext**, nur Titel und Link in die App. So landen keine Inhalte in Postfächern, auf die die Person später vielleicht keinen Zugriff mehr hat.
- Jede Benachrichtigung verweist auf eine Ressource (`task`, `knowledge_article`, `project`). Öffnen prüft die Rechte wie immer über die API.

## Einstellungen

- Pro Person: In-App an/aus, Mail an/aus (Standard: beide an). `webex_enabled` bleibt bis Phase 9 ungenutzt.
- **Annahme (DEC-025):** Einstellungen gelten je Kanal, nicht je Typ. Feinere Einstellungen können später kommen.

## API

```text
GET  /api/v1/me/notifications?unreadOnly&limit&offset
GET  /api/v1/me/notifications/unread-count
POST /api/v1/notifications/{id}/read
POST /api/v1/me/notifications/read-all
GET  /api/v1/me/notification-preferences
PUT  /api/v1/me/notification-preferences   { inAppEnabled, emailEnabled }
GET  /api/v1/dev/outbox                     (nur Development: Fake-Mails an mich)
```

- Benachrichtigungen sieht nur die Person, an die sie gehen. Fremde Benachrichtigungen sind „nicht gefunden“ (404).
- Migration 007: Index für ungelesene Benachrichtigungen je Person.

## Realtime

- Eigener Hub `/api/v1/hubs/notifications`. Jede Verbindung kommt automatisch in die Gruppe ihrer Person.
- Nachricht `NotificationsChanged` enthält nur den Zähler der ungelesenen Benachrichtigungen; Inhalte lädt der Client über die API.

## Frontend

- Glocke im Kopfbereich mit Zähler der ungelesenen Benachrichtigungen, aktualisiert sich live.
- Liste mit „Gelesen“, „Alle gelesen“ und Sprung zur Aufgabe, zum Artikel oder zum Projekt.
- Einstellungen mit zwei Schaltern.
- Bedienbar per Tastatur, Zähler für Screenreader beschriftet.

## Acceptance Criteria

1. Zuweisung, Erwähnung und Projektaufnahme erzeugen genau eine Benachrichtigung für die richtige Person
2. Eigene Aktionen erzeugen keine Benachrichtigung
3. Die Glocke zählt live hoch, ohne Neuladen
4. Gelesen markieren, alle gelesen markieren
5. Mit ausgeschalteter Mail entsteht keine Fake-Mail, mit ausgeschaltetem In-App kein Eintrag in der Liste
6. Fremde Benachrichtigungen sind nicht abrufbar

Tests müssen Auslöser, Empfänger, Einstellungen, Isolation zwischen Personen und Organisationen, Realtime und den E2E-Ablauf abdecken.
