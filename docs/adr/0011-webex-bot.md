# ADR 0011 — Webex über einen zentralen Bot

## Status
Accepted (Bot entschieden von Kai, 2026-10-03)

## Kontext

Phase 9 verknüpft Projekte mit Webex-Meetings und -Spaces, schickt Benachrichtigungen per Webex und nimmt Webhooks an (`docs/INTEGRATIONS.md`). `AGENTS.md` verlangt OAuth, Tokens nie dauerhaft im Browser, validierte, deduplizierte und idempotente Webhooks, und dass Kerntransaktionen nicht von Webex abhängen. DEC-009 (wem die Webex-App gehört) ist offen.

Webex bietet zwei Identitäten: einen **Bot** (ein Token für die ganze Anwendung, handelt als eigener Teilnehmer) oder eine **Integration** (OAuth je Person, handelt in deren Namen).

## Entscheidung

- **Ein zentraler Bot.** Er schickt Direktnachrichten, legt Projekträume an und lädt Mitglieder ein. Personen verbinden kein eigenes Konto; ProjectHub speichert keine persönlichen Webex-Tokens. Das Bot-Token liegt nur im Secret Store.
- **Meetings als Link.** Projekte speichern Meeting- und Raum-Links (`project_webex_link`, Migration 010), nur `https`-Adressen auf `webex.com`. ProjectHub plant keine Meetings.
- **Benachrichtigungen über die Outbox** aus ADR 0010: `mail_outbox.channel` (`email`, `webex`). Gleiche Sperrfrist, Wiederholung und Fehlerklassen; der Tabellenname bleibt, die Tabelle hält ausgehende Benachrichtigungen beider Kanäle.
- **Port `IWebexClient`** mit `fake` und `bot`; `off` blendet Webex in der Oberfläche aus. Webex-Ausfälle betreffen nur den Projektraum-Knopf (Fehlermeldung) und die Outbox (Wiederholung).
- **Webhooks:**
  - Signatur `X-Spark-Signature` = HMAC-SHA1 des Rohinhalts mit dem Webhook-Secret, Vergleich in konstanter Zeit; ohne Secret nimmt der Endpunkt nichts an.
  - Webex liefert keine Ereignis-ID. Der Schlüssel ist `resource:event:data.id`; `webhook_event` hat ihn als eindeutigen Schlüssel. Doppelte Zustellungen antworten 200 ohne Wirkung.
  - Verarbeitung setzt nur Zustände (idempotent) und läuft in derselben Transaktion wie der Eintrag in `webhook_event`.
  - Correlation ID (Trace-ID der Anfrage) steht im Log und in `webhook_event`.
  - Registriert wird der Webhook von einem Organisations-Admin über die API; die Webex-ID landet in `webhook_subscription`.

## Alternativen

- **OAuth je Person:** erlaubt Meetings im eigenen Namen zu planen, braucht aber verschlüsselt gespeicherte Access- und Refresh-Tokens, Schlüsselverwaltung und Token-Erneuerung. Kann später als zweiter Port dazukommen (`integration_account.encrypted_credential_ref` ist dafür vorgesehen).

## Konsequenzen

- Nachrichten kommen vom Bot, nicht von der Person, die etwas geändert hat.
- Der Bot sieht nur Räume, in denen er Mitglied ist. Wird er entfernt, gilt der Raum in ProjectHub als getrennt.
- Mitgliederänderungen am Projekt werden nicht automatisch in den Webex-Raum übertragen.
- Webhooks brauchen eine öffentlich erreichbare Adresse der API.
