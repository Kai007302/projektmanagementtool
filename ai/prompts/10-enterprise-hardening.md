# AI PROMPT — Phase 10 Enterprise Hardening

Du arbeitest an ProjectHub.

## Pflichtlektüre

- `AGENTS.md` (Prioritäten, Security, Datenbank, Definition of Done)
- `docs/SECURITY.md` (Abschnitt App Security), `docs/ARCHITECTURE.md`
- `docs/OPEN_DECISIONS.md` (DEC-001, DEC-002, DEC-005, DEC-010)
- ADR 0003 (Realtime), ADR 0009 (Whiteboard), ADR 0010 (Outbox)

## Ziel

ProjectHub ist bereit für einen Pilotbetrieb hinter einem Reverse Proxy mit TLS: Die Mindestanforderungen aus `docs/SECURITY.md` sind umgesetzt oder in der CI geprüft, die Anwendung läuft als Container, ist beobachtbar und hat ein Betriebshandbuch. Fachliche Funktionen ändern sich nicht.

Die Phase kommt in zwei Pull Requests (wie Phase 3):

- **10a Sicherheit:** Header, HSTS, Proxy, Rate Limits, Größenlimits, Container, CSP, Sicherheits-Scans in der CI.
- **10b Betrieb und Performance:** Telemetrie, strukturierte Logs, Migrationen unter Sperre, Runbook mit Backup/Restore, Lasttest, Barrierefreiheits-Prüfung.

Hosting (DEC-002) und Region (DEC-001) bleiben offen. Alles hier funktioniert mit App Service und Container Apps gleichermaßen.

## 10a Sicherheit

### Auslieferung (Vorschlag ADR 0012, DEC-031)

- Zwei Container: **API** (`src/ProjectHub.Api/Dockerfile`, ASP.NET-Laufzeit, ohne Root) und **Web** (`src/ProjectHub.Web/Dockerfile`, Nginx ohne Root mit dem Produktions-Build).
- Der Web-Container leitet `/api` (einschließlich WebSockets) und `/health` an die API weiter. Browser sprechen damit nur mit **einer Origin**; es gibt kein CORS.
- TLS endet am Ingress des Hostings. Die API vertraut `X-Forwarded-For`/`X-Forwarded-Proto` nur, wenn `PROJECTHUB_TRUST_FORWARDED_HEADERS=true`, und nur, wenn sie ausschließlich über den Proxy erreichbar ist.
- `docker compose --profile app up` startet beide Container lokal.

### HTTP-Header

- Web: `Content-Security-Policy` ohne `unsafe-inline` und ohne `unsafe-eval` für Skripte, `frame-ancestors 'none'`, `object-src 'none'`, `base-uri 'self'`, `form-action 'self'`; `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`, `Permissions-Policy` ohne Kamera, Mikrofon, Ort; HSTS.
- API: `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'`, `nosniff`, `Referrer-Policy: no-referrer`, `Cache-Control: no-store` für API-Antworten; HSTS außerhalb von Development.
- Die CSP gilt auch für die E2E-Tests: Sie laufen gegen den Produktions-Build und schlagen bei einer CSP-Verletzung fehl.

### Rate Limits (Vorschlag DEC-030)

- Je angemeldeter Person, sonst je IP-Adresse; Antwort 429 mit `Retry-After` und Problem Details.
- Startwerte (konfigurierbar): API 600 Anfragen pro Minute je Person; Datei-Uploads 30 pro Minute je Person; Webex-Webhook 120 pro Minute je IP.
- Health-Endpunkte und laufende Hub-Verbindungen zählen nicht.
- Die Zähler gelten je Instanz. Ein verteilter Limiter (Redis) kommt erst bei Bedarf.

### Größenlimits

- Anfragen höchstens 4 MB (`PROJECTHUB_MAX_REQUEST_BYTES`). Uploads behalten ihr eigenes Limit, der Webex-Webhook 64 KB, Whiteboard-Updates ihr Hub-Limit.

### Sicherheits-Scans in der CI

- SAST: CodeQL für C# und TypeScript.
- Abhängigkeiten: `dotnet list package --vulnerable --include-transitive` und `npm audit --omit=dev` brechen bei bekannten Lücken (hoch/kritisch) ab; Dependabot für NuGet, npm, GitHub Actions und Docker.
- Secrets: gitleaks über den ganzen Verlauf.
- Container: Trivy über beide Images, Abbruch bei behebbaren hohen/kritischen Lücken.
- DAST: OWASP ZAP Baseline gegen die lokal gestarteten Container, wöchentlich und auf Knopfdruck (ersetzt nicht den Pentest vor dem Rollout).

## 10b Betrieb und Performance

- OpenTelemetry für Traces, Metriken und Logs (ASP.NET Core, HttpClient, Npgsql, Laufzeit), Export per OTLP, nur wenn `OTEL_EXPORTER_OTLP_ENDPOINT` gesetzt ist. Eigene Metriken: zugestellte und fehlgeschlagene Benachrichtigungen je Kanal, angenommene Webhooks.
- Logs außerhalb von Development als JSON mit Trace-ID. Keine Tokens, Secrets oder Inhalte (bestehende Regeln).
- Migrationen beim Start laufen unter einer PostgreSQL-Advisory-Sperre, damit mehrere Instanzen gleichzeitig starten können.
- `docs/OPERATIONS.md`: Konfiguration, Probes, Skalierung, Backup und Restore (Vorschlag für RPO/RTO aus DEC-010), Wiederherstellungsübung, was bei Ausfall von Redis, Graph oder Webex passiert.
- Lasttest (`tests/load`, k6) für die häufigsten Lesepfade mit Zielwerten; gefundene Engpässe beheben (Indizes, Abfragen).
- Barrierefreiheit: axe-Prüfung in den E2E-Tests der Hauptansichten, ernsthafte und kritische Verstöße beheben.

## Acceptance Criteria

1. Beide Container bauen in der CI, laufen ohne Root und liefern die App über eine Origin aus
2. Die App funktioniert unter der CSP ohne Verletzungen (E2E gegen den Produktions-Build)
3. API-Antworten tragen die Sicherheits-Header; HSTS außerhalb von Development
4. Zu viele Anfragen werden mit 429 und `Retry-After` beantwortet; Health-Endpunkte nie
5. Zu große Anfragen werden mit 413 abgelehnt, Uploads bis zu ihrem eigenen Limit angenommen
6. CodeQL, Abhängigkeits-, Secret- und Container-Scans laufen in der CI
7. (10b) Traces und Metriken lassen sich per OTLP abholen, Logs sind JSON
8. (10b) Mehrere Instanzen können gleichzeitig starten, ohne Migrationen doppelt anzuwenden
9. (10b) Das Runbook beschreibt Backup, Restore und Ausfälle; der Lasttest erreicht die Zielwerte

Tests müssen Header, Rate Limits (mit niedrigen Werten), Größenlimits, Proxy-Header, die CSP im Browser und (10b) die Migrationssperre abdecken.
