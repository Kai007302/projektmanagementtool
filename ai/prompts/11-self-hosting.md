# AI PROMPT — Phase 11 Eigener Server mit Docker

Du arbeitest an ProjectHub.

## Pflichtlektüre

- `AGENTS.md`, `docs/SECURITY.md`, `docs/PERMISSIONS.md`
- `docs/OPEN_DECISIONS.md` (DEC-013, DEC-017)
- ADR 0012 (Auslieferung), ADR 0013 (Betrieb)

## Ziel

ProjectHub lässt sich auf einem einzelnen Linux-Server mit Docker betreiben: Personen melden sich mit ihrem Microsoft-Konto an (Entscheidung Kai, 2026-10-04), TLS kommt automatisch, Daten lassen sich sichern und wiederherstellen. Fachliche Funktionen ändern sich nicht.

## Umfang

- Anmeldung in der Oberfläche mit Entra ID (MSAL, Authorization Code mit PKCE). Die Werte kommen zur Laufzeit von der API, nicht aus dem Build.
- Benutzer beim ersten Login anlegen, abschaltbar (DEC-013); die erste Person wird Admin; gesperrte Personen bleiben gesperrt.
- `docker-compose.prod.yml` mit Reverse Proxy und TLS, Migrationen als eigener Schritt vor dem API-Start, keine offenen Datenbank-Ports, keine Testdaten.
- Skripte für Sicherung und Wiederherstellung.
- `docs/SELF_HOSTING.md`: App-Registrierung, Installation, erste Anmeldung, Updates, Sicherung, Fehlersuche. ADR 0014.

## Acceptance Criteria

1. Im Produktionsmodus leitet die Oberfläche zur Microsoft-Anmeldung des konfigurierten Mandanten weiter und ruft die API mit dem Access Token auf; die CSP bleibt ohne Verletzungen
2. Mit eingeschalteter Anlage wird die erste Person Admin, weitere Mitglied, auch bei gleichzeitigen ersten Anmeldungen; Token anderer Mandanten werden abgewiesen; gesperrte Personen bleiben draußen
3. `docker compose -f docker-compose.prod.yml up -d --build` startet eine leere Installation mit gültigem Zertifikat; die API startet erst nach erfolgreicher Migration
4. Eine Sicherung lässt sich vollständig wiederherstellen
5. Die Entwicklung (Dev-Anmeldung, E2E-Tests) funktioniert unverändert
