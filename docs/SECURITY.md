# Security Baseline

## Identity

Microsoft Entra ID / OIDC. Keine eigene Passwortauthentifizierung.

## Authorization

Serverseitig. Jeder geschützte Zugriff prüft mindestens:

```text
User -> Organization -> Project -> Resource -> Permission
```

Frontend-only authorization ist nicht ausreichend.

## Secrets

Secrets ausschließlich über Azure Key Vault / Secret Store.

Nie committen:

- Client Secrets
- Access Tokens
- Refresh Tokens
- API Keys
- DB Credentials

## Microsoft Graph

Systemmails vorzugsweise über dediziertes Funktionspostfach. Application Permissions restriktiv konfigurieren.

`Mail.Send` kann app-only das Senden ohne angemeldeten Benutzer erlauben; diese Application Permission benötigt Admin Consent und sollte über Exchange Application RBAC bzw. geeignete Zugriffsbeschränkungen auf notwendige Postfächer begrenzt werden.

## Webex

Ein zentraler Bot (ADR 0011). `WEBEX_BOT_TOKEN` und `WEBEX_WEBHOOK_SECRET` kommen nur aus dem Secret Store, nie in den Browser und nie ins Log; Fehlermeldungen tragen nur den HTTP-Status. ProjectHub speichert keine persönlichen Webex-Tokens. Falls später OAuth je Person dazukommt: Tokens verschlüsselt speichern.

Webhooks validieren (HMAC-SHA1-Signatur, Vergleich in konstanter Zeit, ohne Secret wird nichts angenommen), deduplizieren und idempotent verarbeiten. Meeting- und Raum-Links nur als `https` auf `webex.com`.

## KI (ADR 0015)

- Ohne Konfiguration aus. `ANTHROPIC_API_KEY` und `PROJECTHUB_AI_API_KEY` nur aus dem Secret Store bzw. `.env`, nie im Browser oder Log.
- Jedes Werkzeug läuft als die angemeldete Person über die vorhandenen Services; Wissen wird in der Datenbankabfrage gefiltert. Ein Modell sieht und tut nichts, was die Person nicht darf.
- Prompt Injection: Werkzeugergebnisse gelten als Daten. Jede Änderung durch den Assistenten braucht die Freigabe der Person in der Oberfläche; über MCP gibt es Schreibwerkzeuge nur mit `PROJECTHUB_MCP_WRITE_TOOLS=true`, und der Client fragt nach (ADR 0016).
- Der auf Freigabe wartende Schritt geht verschlüsselt und signiert an den Browser (Data Protection, an die Person gebunden, 30 Minuten, einmal verwendbar); freigegeben wird genau, was die Karte zeigt. Die Oberfläche macht nur Links auf ProjectHub-Artikel klickbar und lädt keine Bilder, damit kein Inhalt Daten an fremde Server schicken kann.
- Gespräche werden nicht gespeichert; Telemetrie enthält Modell, Tokens und Werkzeugnamen, keine Prompts oder Antworten.
- Eigenes Rate Limit je Person für den Assistenten; Eingaben sind in Anzahl und Länge begrenzt.
- MCP: nur Entra-ID-Token des Mandanten; der Endpunkt liegt unter `/api/v1` mit derselben Autorisierung.

## Logging

Nicht loggen:

- Passwörter
- OAuth-Tokens
- Secrets
- unnötige Mail-/Chat-Inhalte
- unnötige personenbezogene Daten

## Audit

Sicherheitsrelevante Aktionen auditieren:

- Login/Logout
- Rollenänderungen
- Member-Änderungen
- Permission Changes
- Projekt-/Task-Löschungen
- Integrations-Änderungen

## App Security

Umsetzung und Werte: ADR 0012. TLS und die Weiterleitung auf HTTPS übernimmt der Ingress; HSTS, CSP und die übrigen Header setzen Web-Container und API; Rate Limits und Größenlimits die API; Dependency-, Secret- und Container-Scanning sowie SAST (CodeQL) laufen in der CI, eine ZAP-Baseline (DAST) wöchentlich. Pentest vor dem Rollout bleibt offen.

Mindestens:

- TLS
- HSTS
- CSP
- Rate limiting
- Input Validation
- Output Encoding
- parameterized queries / EF Core
- dependency scanning
- secret scanning
- container scanning
- SAST
- DAST/Pentest vor Produktiv-Rollout

## Datenschutz

DSGVO und interne Richtlinien mit IT/Datenschutz/Security abstimmen. Die AI darf rechtliche Freigaben nicht voraussetzen.

Überblick über die rechtlichen Anforderungen und eine Checkliste für Betreiber: `docs/LEGAL.md`. Umgesetzt (ADR 0017): Export der eigenen Daten, Anonymisieren von Personen durch Organisations-Admins, Löschfristen für Benachrichtigungen, Aktivität und Audit-Log, Links auf Datenschutzhinweise und Impressum. Zugriffslogs des Web-Containers enthalten weder IP-Adresse noch Query-String (dort stünde das Token der Hubs und des Kalender-Abos).

## Import, Export und Kalender-Abo

Umgesetzt in ADR 0018:

- Import von Aufgaben nur mit Schreibrecht im Projekt, höchstens 2 MB, 1.000 Zeilen und 50 Spalten; Excel-Dateien werden vor dem Öffnen auf entpackte Größe (50 MB) und Anzahl der Teile geprüft (Zip-Bombe). Jede Zeile durchläuft dieselbe Validierung wie das Anlegen über die API.
- CSV-Export setzt vor Freitext, der wie eine Formel beginnt, ein `'` (CSV-Injection); Excel-Zellen werden nur als Text, Zahl oder Datum geschrieben, nie als Formel.
- Das Kalender-Abo ist die einzige Abfrage von Inhalten ohne Anmeldung. Das Token (256 Bit, zufällig) steht im Query-String, wird nur als SHA-256-Hash gespeichert und nur beim Erzeugen angezeigt. Rechte werden bei jedem Abruf neu geprüft; inaktive oder anonymisierte Personen bekommen 404.
