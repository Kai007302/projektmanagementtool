# ADR 0012 — Auslieferung als Container und HTTP-Härtung

## Status
Proposed

## Kontext

Bis Phase 9 läuft ProjectHub nur lokal: die API über `dotnet run`, das Frontend über den Vite-Dev-Server, der `/api` an die API weiterleitet. `docs/SECURITY.md` verlangt TLS, HSTS, CSP, Rate Limiting sowie Abhängigkeits-, Secret-, Container- und statische Analyse (SAST) und vor dem Rollout DAST/Pentest. Hosting (DEC-002) und Region (DEC-001) sind offen; die Entscheidung hier muss für App Service und Container Apps gleichermaßen taugen.

Das Frontend ruft die API mit relativen Pfaden auf und nutzt WebSockets für die Hubs. Die API vertraut heute keinem Proxy, kennt also weder die Adresse des Browsers noch, ob er HTTPS benutzt hat.

## Entscheidung

### Zwei Container, eine Origin

- **API** (`src/ProjectHub.Api/Dockerfile`): ASP.NET-Laufzeit „chiseled“ (ohne Shell und Paketmanager, ICU und Zeitzonen über „extra“), läuft als Benutzer `app`. Gestartet wird das Apphost-Programm, damit die Whiteboard-Engine (ADR 0009) sich selbst als Kindprozess starten kann. Anhänge und Snapshots liegen unter `/data` (Volume, bis Blob Storage kommt).
- **Web** (`src/ProjectHub.Web/Dockerfile`): der Produktions-Build in Nginx ohne Root. Nginx leitet `/api` (einschließlich WebSockets) und `/health` an die API weiter. Der Browser spricht damit nur mit **einer Origin**: kein CORS, Cookies und Tokens bleiben auf einer Adresse, die CSP kommt mit `'self'` aus.
- **TLS endet am Ingress** des Hostings (Front Door, App Service, Container Apps). Innerhalb der Umgebung läuft HTTP.
- `docker compose --profile app up --build` startet beide Container lokal; die CI testet die Oberfläche gegen genau diese Container.

### Proxy-Vertrauen

- Mit `PROJECTHUB_TRUST_FORWARDED_HEADERS=true` übernimmt die API `X-Forwarded-For` und `X-Forwarded-Proto`, und zwar genau einen Eintrag (den des Ingress). Die Adresse des Ingress ist vorher nicht bekannt, daher gibt es keine Liste bekannter Proxys. Das ist nur sicher, wenn die API **ausschließlich** über den Web-Container bzw. Ingress erreichbar ist (internes Netz, keine öffentliche Adresse).
- Nginx reicht die Kopfzeilen des Ingress unverändert weiter, statt eigene anzuhängen.

### HTTP-Header

- **Web (Nginx):** Content Security Policy nur mit `'self'` (keine Inline-Skripte, kein `eval`, keine Inline-Styles, keine fremden Quellen), `frame-ancestors 'none'`, `object-src 'none'`, `base-uri 'self'`, `form-action 'self'`; `nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`, `Permissions-Policy` ohne Kamera, Mikrofon, Ort, Zahlung, USB; `Cross-Origin-Opener-Policy: same-origin`; HSTS ein Jahr. Die Werte stehen einmal in `deploy/security-headers.json`; Nginx-Zeilen werden beim Bauen daraus erzeugt. Gebaute Dateien mit Hash im Namen sind ein Jahr cachebar, `index.html` nicht.
- **API:** `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'`, `X-Frame-Options: DENY`, `nosniff`, `Referrer-Policy: no-referrer`, `Cross-Origin-Resource-Policy: same-origin` und `Cache-Control: no-store`, wenn ein Endpunkt nichts anderes setzt. HSTS (ein Jahr, ohne Subdomains und Preload) außerhalb von Development.
- Keine Weiterleitung von HTTP auf HTTPS in der API; das macht der Ingress.

### Rate Limits und Größenlimits (Vorschlag DEC-030)

- Je angemeldeter Person (Mandant und Objekt-ID aus dem Token), sonst je Client-Adresse; feste Fenster von einer Minute, keine Warteschlange. Antwort 429 mit `Retry-After` und Problem Details.
- API 600 Anfragen pro Minute (`PROJECTHUB_RATE_LIMIT_PER_MINUTE`), Uploads zusätzlich 30 (`PROJECTHUB_UPLOAD_RATE_LIMIT_PER_MINUTE`), Webex-Webhook 120 je Adresse (`PROJECTHUB_WEBHOOK_RATE_LIMIT_PER_MINUTE`); 0 schaltet ein Limit ab. Health-Endpunkte zählen nie. Eine Hub-Verbindung zählt beim Aufbau, ihre Nachrichten nicht.
- Die Zähler gelten **je Instanz**. Bei zwei Instanzen kann eine Person also bis zum Doppelten kommen; ein verteiltes Limit über Redis kommt erst bei Bedarf. Schutz gegen volumetrische Angriffe gehört an den Ingress/WAF.
- Anfragen höchstens 4 MB (`PROJECTHUB_MAX_REQUEST_BYTES`). Uploads behalten ihr eigenes Limit (`PROJECTHUB_ATTACHMENT_MAX_BYTES`), der Webex-Webhook 64 KB, Whiteboard-Updates ihr Hub-Limit.

### Sicherheits-Scans in der CI

- SAST: CodeQL (`security-extended`) für C# und TypeScript bei jedem Pull Request und wöchentlich.
- Abhängigkeiten: `dotnet list package --vulnerable` und `npm audit --omit=dev --audit-level=high`; Dependabot wöchentlich für NuGet, npm, GitHub Actions und Docker-Basisimages.
- Secrets: gitleaks über den gesamten Verlauf.
- Container: Trivy über beide Produktions-Images, Abbruch bei behebbaren hohen und kritischen Lücken. Das Web-Image spielt beim Bauen die Alpine-Sicherheitsupdates ein.
- DAST: OWASP-ZAP-Baseline (passiv) gegen die Container in Production-Konfiguration, wöchentlich und auf Knopfdruck. Ersetzt nicht den Pentest vor dem Rollout.

### End-to-End-Tests gegen die Container

Die Synthetic-Anmeldung der Oberfläche gibt es im Dev-Server und in Builds mit `VITE_DEV_IDENTITY=true`. Nur eine API in Development beachtet sie; außerhalb entscheidet Entra ID. Die CI baut das Test-Image damit, das Produktions-Image ohne. Jeder E2E-Test schlägt bei einer CSP-Verletzung fehl.

## Alternativen

- **Frontend aus der API ausliefern** (`UseStaticFiles`): ein Container weniger, aber die CSP und das Caching der Oberfläche lägen in der API, und jedes Frontend-Release wäre ein API-Release.
- **Frontend getrennt (Static Web Apps, Blob/CDN) mit CORS:** zwei Origins, CORS-Konfiguration und eine `connect-src` auf die API-Adresse; die WebSockets der Hubs bräuchten eigene Freigaben.
- **Rate Limits in Redis:** genaue Zähler über alle Instanzen, aber jede Anfrage hinge an Redis. Redis ist heute nur für Realtime-Fan-out da (ADR 0003).

## Konsequenzen

- Hosting braucht zwei Container (oder eine Container-App mit zwei Containern) und ein privates Netz zwischen Web und API.
- Eine Inline-Style- oder Inline-Skript-Lösung im Frontend ist künftig ausgeschlossen; neue externe Quellen (z. B. Schriften, Telemetrie im Browser) brauchen eine Änderung der CSP.
- Rate-Limit-Werte sind Startwerte und müssen im Pilotbetrieb beobachtet werden.
