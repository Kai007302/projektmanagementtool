# ADR 0008 — Zustellung von Benachrichtigungen

## Status
Accepted

## Kontext

Phase 6 bringt Benachrichtigungen in der App und per Mail. `docs/INTEGRATIONS.md` sieht für Mail `Domain Event -> Notification Service -> Service Bus -> Graph Mail Sender` vor. Service Bus und Microsoft Graph gibt es aber erst mit Phase 8 und den nötigen App-Registrierungen (Human Review Gate).

## Entscheidung

- Module veröffentlichen nach dem Commit Domain Events (`TaskAssigned`, `ProjectMemberAdded`, `UsersMentionedInComment`, `UsersMentionedInKnowledgeComment`). Das Notifications-Modul reagiert darauf; die Module kennen es nicht.
- Der `NotificationDispatcher` arbeitet in einer **eigenen Unit of Work** (eigener Scope und `DbContext`). Fehler werden geloggt und machen die auslösende Änderung nie rückgängig; der Request scheitert nicht.
- Pro Empfänger gelten die Kanal-Einstellungen aus `notification_preference` (ohne Zeile: In-App und Mail an).
- Mail läuft über `IEmailSender`. Bis Phase 8 ist das `FakeEmailSender`: kein Versand, die letzten 500 Mails bleiben im Speicher (`GET /api/v1/dev/outbox` nur in Development), im Log steht nur die Empfänger-ID.
- **Mails enthalten nur den Titel und einen Link in die App, keinen Kommentartext.** Postfächer behalten Inhalte dauerhaft, auch wenn die Person später den Zugriff verliert.
- Realtime über einen eigenen Hub `/api/v1/hubs/notifications`. Jede Verbindung landet in der Gruppe ihrer Person; Nachrichten enthalten nur den Zähler der ungelesenen Benachrichtigungen (wie ADR 0003: keine Inhalte über Realtime).

## Konsequenzen

- Zustellung ist derzeit „höchstens einmal“: stürzt der Prozess zwischen Commit und Zustellung ab, fehlt die Benachrichtigung. Mit Phase 8 wandert der Mailversand hinter Service Bus (Outbox), damit Graph-Ausfälle nachgeholt werden.
- Nachtrag Phase 8: Mails laufen über die Outbox in PostgreSQL (ADR 0010) und werden zusammen mit den In-App-Benachrichtigungen gespeichert.
- Titel der Benachrichtigung sind Momentaufnahmen (z. B. Aufgabentitel zum Zeitpunkt der Zuweisung). Öffnen prüft die Rechte wie immer über die API.
- Einstellungen gibt es je Kanal, nicht je Typ (DEC-025).
