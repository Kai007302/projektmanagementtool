# Webex einrichten (für die IT)

ProjectHub spricht mit Webex über **einen zentralen Bot** (ADR 0011). Er schickt Benachrichtigungen als Direktnachricht, legt Projekträume an und lädt Projektmitglieder ein. Personen verbinden kein eigenes Webex-Konto. Bis die Einrichtung abgeschlossen ist, läuft die API mit `PROJECTHUB_WEBEX_TRANSPORT=off` (Webex ist in der Oberfläche ausgeblendet) oder in Development mit `fake`.

Meeting- und Raum-Links können Projektleitungen auch ohne Bot eintragen, sobald der Transport nicht `off` ist.

## 1. Bot anlegen

- Auf https://developer.webex.com mit einem Konto der Organisation anmelden, „My Webex Apps“ → „Create a Bot“.
- Name z. B. „ProjectHub“, Benutzername z. B. `projecthub@webex.bot`, Symbol und Beschreibung nach Wahl.
- Das angezeigte **Bot Access Token** sofort in den Secret Store legen (`WEBEX_BOT_TOKEN`). Es wird nur einmal angezeigt; es läuft nicht ab, kann aber auf derselben Seite neu erzeugt werden.
- Wer das Bot-Konto besitzt und das Token erneuern darf, legt die IT fest (DEC-009).

Falls die Organisation externe Bots einschränkt: den Bot in Control Hub für die eigene Organisation freigeben.

## 2. Webhook-Secret und öffentliche Adresse

- Ein zufälliges Secret erzeugen, z. B. `openssl rand -hex 32`, und als `WEBEX_WEBHOOK_SECRET` in den Secret Store legen.
- `PROJECTHUB_PUBLIC_API_URL` auf die öffentlich erreichbare Adresse der API setzen, z. B. `https://projecthub.firma.de`. Webex ruft dort `/api/v1/integrations/webex/webhook` auf. Ohne öffentliche Adresse funktioniert alles außer der Erkennung, dass der Bot aus einem Raum entfernt wurde.

## 3. API konfigurieren

```
PROJECTHUB_WEBEX_TRANSPORT=bot
WEBEX_BOT_TOKEN=<aus dem Secret Store>
WEBEX_WEBHOOK_SECRET=<aus dem Secret Store>
PROJECTHUB_PUBLIC_API_URL=https://projecthub.firma.de
```

Mit `bot` und ohne Token startet die API nicht.

## 4. Webhook registrieren

Ein Organisations-Admin ruft einmal auf:

```
POST /api/v1/admin/webex/webhook
```

ProjectHub registriert dann beim Bot einen Webhook für `memberships:deleted` mit dem Secret. `GET /api/v1/admin/webex` zeigt Transport, ob ein Secret gesetzt ist, die Zieladresse und die registrierten Webhooks mit dem Zeitpunkt des letzten Ereignisses.

## 5. Prüfen

- In einem Projekt „Projektraum anlegen“ klicken. In Webex erscheint ein Raum mit dem Projektnamen, die Mitglieder sind eingeladen, der Bot hat einen Link gepostet.
- In der Glocke „Per Webex“ einschalten und sich z. B. eine Aufgabe zuweisen lassen. Der Bot schickt eine Direktnachricht.
- Den Bot aus dem Raum entfernen. Der Raum ist in ProjectHub als getrennt markiert.
- Fehlgeschlagene Webex-Nachrichten stehen unter `GET /api/v1/admin/mail-outbox` mit `channel: "webex"` und lassen sich dort erneut versenden.

## Was ProjectHub nicht tut

- keine persönlichen Webex-Tokens speichern, keine Meetings im Namen von Personen planen
- keine Nachrichten lesen; der Bot sieht nur Räume, in denen er Mitglied ist
- Änderungen an Projektmitgliedern nicht in den Raum übertragen
