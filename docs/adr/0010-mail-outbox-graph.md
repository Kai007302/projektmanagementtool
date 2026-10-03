# ADR 0010 — Mail-Outbox und Versand über Microsoft Graph

## Status
Proposed

## Kontext

ADR 0008 verschickt Benachrichtigungsmails direkt nach dem Commit über einen Fake. Stürzt der Prozess ab oder ist der Versanddienst nicht erreichbar, fehlt die Mail. `docs/INTEGRATIONS.md` sieht `Domain Event -> Notification Service -> Service Bus -> Graph Mail Sender` vor, `AGENTS.md` verlangt Service Bus für dauerhafte Jobs. Azure (und damit Service Bus) gibt es noch nicht; die echte Graph-Anbindung braucht eine App-Registrierung der IT.

## Entscheidung

- **Transactional Outbox in PostgreSQL** (DEC-029, entschieden von Kai am 2026-10-03): Mails landen in `mail_outbox` (Migration 009), in derselben Transaktion wie die In-App-Benachrichtigungen. Dadurch gibt es keine Benachrichtigung ohne Mail und umgekehrt.
- **Versand im Hintergrund:** `MailOutboxWorker` läuft in jeder API-Instanz. Er reserviert fällige Zeilen mit `FOR UPDATE SKIP LOCKED` und setzt dabei eine **Sperrfrist** (`next_attempt_at = jetzt + 2 min`). Der Versand selbst läuft außerhalb der Transaktion; danach wird die Zeile `sent`, neu terminiert oder `failed`.
  - Instanzen arbeiten nie an derselben Zeile. Stirbt eine Instanz mitten im Versand, greift nach der Sperrfrist eine andere zu. Zustellung ist damit „mindestens einmal“; eine doppelte Mail ist im Ausnahmefall möglich und wird hingenommen (Graph `sendMail` ist nicht idempotent).
  - Neue Mails wecken den Worker derselben Instanz sofort; sonst schaut er alle 15 Sekunden nach.
- **Fehlerklassen:** Netzwerkfehler, 401 (abgelaufene Anmeldedaten), 408, 429 und 5xx sind vorübergehend (wachsender Abstand ab 30 s, höchstens 1 h, `Retry-After` hat Vorrang, maximal 8 Versuche). Andere 4xx sind dauerhaft und führen sofort zu `failed`.
- **Datensparsam:** `last_error` enthält nur Status und Graph-Fehlercode. Verschickte Zeilen werden nach 7 Tagen gelöscht, fehlgeschlagene nach 30 Tagen. Die Admin-API zeigt weder Adressen noch Betreff.
- **Transport hinter `IEmailSender`** (Port, `AGENTS.md`): `fake` (Standard) oder `graph`, umschaltbar über `PROJECTHUB_MAIL_TRANSPORT`. Der Graph-Transport ruft `POST /v1.0/users/{Funktionspostfach}/sendMail` mit `saveToSentItems = false` auf. Tokens holt `Azure.Identity` per Client Credentials (Secret aus dem Secret Store) oder Managed Identity.
- **Rechte:** nur die Anwendungsberechtigung zum Mail-Versand, in Exchange Online auf das Funktionspostfach beschränkt (RBAC for Applications, alternativ Application Access Policy). Kein Versand als Benutzer (DEC-012).
- **Service Bus später:** Kommt Azure, liest ein Relay die Outbox und stellt Nachrichten in Service Bus ein, oder der Worker bleibt, wenn die Last es erlaubt. Die Module ändern sich dafür nicht.

## Kalender

Kein Graph-Kalenderzugriff in dieser Phase (Vorschlag DEC-028). Aufgaben und Meilensteine werden als `.ics` geliefert oder über einen Outlook-Deeplink im Browser der Person angelegt. Beides braucht keine zusätzlichen Rechte und keinen Token im Backend. Delegiertes `Calendars.ReadWrite` (On-Behalf-Of) kommt erst, wenn jemand Termine ohne eigenen Klick in Outlook braucht.

## Konsequenzen

- Mails kommen auch nach Ausfällen von Graph oder Neustarts der API an.
- Die Datenbank hält Empfängeradresse und Betreff, bis die Mail verschickt und 7 Tage alt ist.
- Der Versand hängt nicht mehr am Request; zwischen Benachrichtigung und Mail liegen im Normalfall Millisekunden, nach Fehlern bis zu einer Stunde.
- Für einen Durchsatz deutlich über einigen Mails pro Sekunde ist der Worker nicht gedacht. Dann übernimmt Service Bus.
