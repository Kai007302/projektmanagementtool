# AI PROMPT — Phase 8 Microsoft 365

Du arbeitest an ProjectHub.

## Pflichtlektüre

- `AGENTS.md` (Abschnitte Security und Integrationen/Microsoft)
- `docs/INTEGRATIONS.md`, `docs/SECURITY.md`
- `docs/adr/0008-notification-delivery.md`
- DEC-003 (System-Mailbox), DEC-012 (Mail als Benutzer)

## Ziel

ProjectHub verschickt Benachrichtigungsmails echt über **Microsoft Graph** aus einem Funktionspostfach, ohne dass eine Mail verloren geht, wenn Graph ausfällt. Aufgaben und Meilensteine lassen sich mit einem Klick in den eigenen Outlook-Kalender übernehmen. Microsoft-Graph-Rechte bleiben so klein wie möglich.

## Mailversand (Vorschlag ADR 0010)

- Neue Tabelle `mail_outbox` (Migration 009). Der `NotificationDispatcher` schreibt Mails **in derselben Transaktion** wie die In-App-Benachrichtigungen dorthin, statt sie direkt zu verschicken.
- Ein Hintergrunddienst `MailOutboxWorker` holt fällige Mails (`FOR UPDATE SKIP LOCKED`, mehrere Instanzen stören sich nicht), verschickt sie über `IEmailSender` und markiert sie als verschickt. Er wird nach neuen Mails sofort geweckt und schaut zusätzlich regelmäßig nach.
- Fehler:
  - vorübergehend (Netz, 408, 429, 5xx): Wiederholung mit wachsendem Abstand, `Retry-After` wird beachtet, höchstens 8 Versuche
  - dauerhaft (andere 4xx, z. B. Postfach unbekannt, keine Berechtigung): sofort `failed`
  - Ein Absturz während des Versands führt nach Ablauf einer Sperrfrist zu einem neuen Versuch („mindestens einmal“, im Ausnahmefall doppelt).
- Gespeicherte Fehlertexte enthalten nur HTTP-Status und Graph-Fehlercode, keine Adressen oder Inhalte.
- Aufräumen: verschickte Mails nach 7 Tagen, fehlgeschlagene nach 30 Tagen löschen.
- Versandweg per Konfiguration `PROJECTHUB_MAIL_TRANSPORT`:
  - `fake` (Standard): wie bisher nur im Speicher, `GET /api/v1/dev/outbox` in Development
  - `graph`: `POST /users/{Funktionspostfach}/sendMail` mit App-Token (Client Credentials), `saveToSentItems = false`
- Graph-Zugang über `Azure.Identity`: Client Secret aus dem Secret Store oder ohne Secret über Managed Identity. Fehlt bei `graph` eine Pflichtangabe, startet die API nicht.
- Mails enthalten wie bisher nur den Titel und einen Link in die App (ADR 0008).
- Service Bus kann später hinter dieselbe Tabelle kommen (Outbox-Relay); Module ändern sich dafür nicht.

## Mail-Status für Organisations-Admins

```text
GET  /api/v1/admin/mail-outbox                 Zähler (wartend, fehlgeschlagen, verschickt in 24 h), älteste wartende Mail, letzte Fehler
POST /api/v1/admin/mail-outbox/{id}/retry      fehlgeschlagene Mail erneut versuchen (Audit-Log)
```

Nur Organisations-Admins; andere bekommen 403. Antworten enthalten keine Adressen und keine Betreffzeilen, nur Empfänger-ID, Zeitpunkte, Versuche und Fehlercode.

## Kalender (Vorschlag DEC-028)

Kein Kalenderzugriff über Graph. Stattdessen zwei Wege, die **keine zusätzlichen Graph-Rechte** brauchen:

```text
GET /api/v1/tasks/{taskId}/calendar.ics             ganztägiger Termin (Start- bis Fälligkeitsdatum)
GET /api/v1/gantt-milestones/{id}/calendar.ics      ganztägiger Termin am Meilensteintag
```

- Rechte: wer die Aufgabe bzw. das Projekt sehen darf. Sonst 404.
- Ohne Datum: 400.
- Inhalt: nur Titel und Link in die App (wie Mails), feste `UID` je Aufgabe/Meilenstein, `SEQUENCE` aus der Version. Erneut importiert, ersetzt Outlook den alten Eintrag.
- Im Frontend: „Kalenderdatei (.ics)“ und „In Outlook im Web anlegen“ (Outlook-Deeplink, öffnet das Formular mit Titel und Datum im eigenen Postfach) in den Aufgabendetails und bei Meilensteinen.
- Termine im Namen der Person per Graph (delegiert `Calendars.ReadWrite`) bleiben offen, bis jemand sie wirklich braucht.

## IT-Freigabe

`docs/integrations/microsoft-365-setup.md` beschreibt für die IT:

- App-Registrierung, nur Anwendungsberechtigung für Mail-Versand
- Einschränkung auf das Funktionspostfach (RBAC for Applications in Exchange Online, alternativ Application Access Policy)
- Secret im Key Vault oder Managed Identity, Konfigurationswerte

Ohne diese Freigabe läuft alles mit `fake` weiter.

## Acceptance Criteria

1. Eine Benachrichtigungsmail wird zusammen mit der Benachrichtigung gespeichert und danach verschickt
2. Fällt der Versand vorübergehend aus, wird er später wiederholt; dauerhafte Fehler werden markiert und nicht endlos wiederholt
3. Mehrere Instanzen verschicken keine Mail doppelt im Normalbetrieb
4. Mit `graph` geht genau ein Aufruf an `/users/{Funktionspostfach}/sendMail` mit Titel, Link und Empfänger
5. Admins sehen den Zustand der Warteschlange und können fehlgeschlagene Mails neu anstoßen
6. Aufgaben und Meilensteine lassen sich als Kalenderdatei laden und per Outlook-Deeplink anlegen; ohne Leserecht 404
7. Keine Secrets im Code, keine Graph-Aufrufe aus dem Browser

Tests müssen Outbox (Speichern, Versand, Wiederholung, dauerhafter Fehler, Sperrfrist, Aufräumen), Graph-Aufruf (mit Fake-HTTP), Admin-Rechte, Kalenderdatei (Inhalt, Rechte, Escaping) und den E2E-Ablauf abdecken.
