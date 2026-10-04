# ADR 0014 — Betrieb auf einem eigenen Server und Anmeldung mit Entra ID

## Status
Proposed

## Kontext

Kai möchte ProjectHub per Docker auf einem eigenen Server betreiben, unabhängig von der noch offenen Azure-Entscheidung (DEC-002). Bis Phase 10 fehlte dafür zweierlei:

- Die API prüft außerhalb von Development Entra-ID-Token, die Oberfläche holte aber keine: Sie kannte nur die synthetische Dev-Anmeldung.
- ProjectHub-Benutzer entstanden nur aus den Testdaten; unbekannte Personen bekamen 403 (DEC-013 offen).

Außerdem gab es nur eine Compose-Datei für die Entwicklung (feste Passwörter, offene Datenbank-Ports, Testdaten, Migrationen bei jedem Start).

Kai hat entschieden, dass Personen sich mit ihrem Microsoft-Konto anmelden (2026-10-04).

## Entscheidung

### Anmeldung in der Oberfläche

- Die Oberfläche meldet Personen mit MSAL (`@azure/msal-browser`) über **Authorization Code mit PKCE** an, als Vollseiten-Weiterleitung. Popups scheiden aus, weil `Cross-Origin-Opener-Policy: same-origin` (ADR 0012) die Verbindung zum Popup trennt.
- Mandant, Client-ID und Bereich holt die Oberfläche beim Start von `GET /api/v1/sign-in` (ohne Anmeldung, die Werte sind nicht geheim). Ein Image passt damit für jede Installation; nichts davon wird ins Frontend gebaut. In Development antwortet der Endpunkt `development`, und die Dev-Anmeldung bleibt wie bisher.
- Token liegen im `sessionStorage` (nur der Tab). Die API bekommt sie als `Authorization: Bearer`, die Hubs als `access_token` im Query-String (wie bisher vorgesehen; ADR 0013 sorgt dafür, dass er nicht in Telemetrie landet).
- Bereich: `api://<Client-ID>/access_as_user` (änderbar über `ENTRA_API_SCOPE`). Die API nimmt v2- und v1-Token **desselben Mandanten** an; für v1 kommt die E-Mail aus `upn`.
- CSP: `connect-src` und `frame-src` erlauben zusätzlich `https://login.microsoftonline.com` (Token-Abruf und stille Erneuerung). Sonst bleibt die Richtlinie unverändert.

### Benutzer beim ersten Login (DEC-013)

- Mit `PROJECTHUB_USER_PROVISIONING=first-sign-in` legt die API beim ersten Aufruf einer unbekannten Person aus dem konfigurierten Mandanten (`ENTRA_TENANT_ID`) einen Benutzer an. Gibt es die Organisation des Mandanten noch nicht, entsteht sie dabei (Name aus `PROJECTHUB_ORGANIZATION_NAME`).
- Die **erste Person einer Organisation wird Admin**, alle weiteren Mitglied. Eine Advisory-Sperre je Mandant verhindert, dass zwei gleichzeitige erste Anmeldungen beide Admin werden.
- Bestehende Benutzer werden nie verändert: Gesperrte (`inactive`) bleiben gesperrt. Ist die E-Mail-Adresse schon an ein anderes Konto vergeben, entsteht kein Benutzer (403, Warnung im Log).
- Standard ist `off` (Verhalten wie bisher). Wer den Kreis einschränken will, nutzt in Entra „Zuweisung erforderlich“.

### Docker auf einem Server

- `docker-compose.prod.yml`: Caddy (TLS mit Let's Encrypt, einzige öffentliche Ports 80/443) → Web-Container → API → PostgreSQL und Redis (nur intern). Die API läuft in `Production`, ohne Testdaten und ohne Migrationen beim Start.
- Migrationen laufen im eigenen Dienst `migrate` (`ProjectHub.Api --migrate`, gleiches Image), die API startet erst nach seinem Erfolg. Damit gilt auch hier: Die laufende API migriert nie selbst; der Prüfschritt ist das Update (`git pull`, Migrationen ansehen, `up --build`).
- `PROJECTHUB_TRUST_FORWARDED_HEADERS=true`: Caddy setzt `X-Forwarded-For/-Proto`, Nginx reicht sie durch, die API vertraut genau diesem einen Eintrag (ADR 0012).
- Sicherung mit `scripts/backup.sh` (Datenbank zuerst, dann Dateien) und `scripts/restore.sh`. RPO je nach cron-Takt, Vorschlag stündlich.
- Konfiguration in `.env` neben der Compose-Datei (`.env.prod.example`), nie im Repository.

## Alternativen

- **Eigene Konten in ProjectHub:** keine Abhängigkeit von Microsoft, aber Passwort-Speicherung, Zurücksetzen, MFA und Sperren müssten wir selbst bauen; widerspricht dem Konzept (Entra ID, Graph).
- **Anmeldung im Reverse Proxy (oauth2-proxy):** kein Code in der Oberfläche, aber die API bräuchte einen zweiten Vertrauensweg (Kopfzeilen vom Proxy) und die Hubs eigene Behandlung.
- **Werte für die Anmeldung beim Bauen ins Frontend:** ein Image je Installation; `/api/v1/sign-in` vermeidet das.
- **SCIM oder Gruppen-Abgleich statt Anlage beim Login:** sauberer für große Organisationen, braucht aber Entra-ID-P1 und einen öffentlich erreichbaren SCIM-Endpunkt. Bleibt als Option für DEC-013 offen.
- **Traefik oder Nginx mit certbot statt Caddy:** mehr Konfiguration für dasselbe Ergebnis.

## Konsequenzen

- Jede Person des Mandanten kann sich anmelden und wird Mitglied, solange in Entra keine Zuweisung verlangt wird. Mitglieder sehen nur Projekte, in die sie aufgenommen werden, und veröffentlichtes Wissen (DEC-020).
- Personen sperren geht vorerst nur per SQL; eine Admin-Oberfläche für Benutzer fehlt.
- Die CSP erlaubt eine fremde Quelle (Microsoft-Anmeldung).
- Der Single-Server-Aufbau hat keine Ausfallsicherheit und bewahrt Dateien lokal auf (DEC-017).
