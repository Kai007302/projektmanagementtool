# ProjectHub auf einem eigenen Server (Docker)

Anleitung für den Betrieb auf einem einzelnen Linux-Server mit Docker. Anmeldung über Microsoft Entra ID, TLS über Caddy mit Let's-Encrypt-Zertifikat. Architektur und Entscheidungen: ADR 0014. Für Azure (App Service, Container Apps) gilt `docs/OPERATIONS.md`.

```text
Internet ──443──▶ caddy (TLS) ──▶ web (Nginx, Oberfläche) ──/api, /health──▶ api ──▶ postgres
                                                                              └──▶ redis
```

## Voraussetzungen

- Linux-Server mit Docker Engine und Docker Compose Plugin (`docker compose version`), mindestens 2 vCPU, 4 GB RAM, 20 GB Platte.
- Ein Domainname (z. B. `projecthub.example.com`), dessen DNS-Eintrag (A/AAAA) auf den Server zeigt.
- Ports 80 und 443 vom Internet erreichbar (Let's Encrypt prüft über Port 80). Sonst keine offenen Ports: PostgreSQL und Redis sind nur intern erreichbar.
- Ein Microsoft-Entra-ID-Mandant (jeder Microsoft-365-Mandant hat einen) und ein Konto, das dort App-Registrierungen anlegen darf.

## 1. App-Registrierung in Entra ID

Im [Entra Admin Center](https://entra.microsoft.com) unter **Identität → Anwendungen → App-Registrierungen → Neue Registrierung**:

1. **Name:** `ProjectHub`. **Unterstützte Kontotypen:** „Nur Konten in diesem Organisationsverzeichnis“ (ein Mandant).
2. **Umleitungs-URI:** Plattform **Single-Page-Anwendung (SPA)**, URI `https://projecthub.example.com/` (mit Schrägstrich am Ende, eigene Domain einsetzen).
3. Nach dem Anlegen auf der Übersicht notieren: **Anwendungs-ID (Client)** → `ENTRA_CLIENT_ID`, **Verzeichnis-ID (Mandant)** → `ENTRA_TENANT_ID`.
4. **Eine API verfügbar machen:** Anwendungs-ID-URI festlegen (Vorschlag `api://<Client-ID>` übernehmen), dann **Bereich hinzufügen**: Name `access_as_user`, Zustimmung „Administratoren und Benutzer“, Anzeigename z. B. „ProjectHub verwenden“, Status aktiviert.
5. **API-Berechtigungen → Berechtigung hinzufügen → Meine APIs → ProjectHub →** `access_as_user` (delegiert). Danach **Administratorzustimmung erteilen**, damit niemand einzeln zustimmen muss.
6. **Manifest:** `"requestedAccessTokenVersion": 2` setzen (im Abschnitt `api`). ProjectHub nimmt auch v1-Token an, v2 ist aber der vorgesehene Weg.
7. Optional: Unter **Unternehmensanwendungen → ProjectHub → Eigenschaften** „Zuweisung erforderlich“ auf **Ja** setzen und unter **Benutzer und Gruppen** festlegen, wer ProjectHub benutzen darf. Ohne Zuweisung kann sich jede Person des Mandanten anmelden.

Es gibt **kein Client Secret** für die Anmeldung: Die Oberfläche meldet sich per Authorization Code mit PKCE an, die API prüft nur Token.

## 2. Installation

```bash
git clone https://github.com/Kai007302/projektmanagementtool.git /opt/projecthub
cd /opt/projecthub
cp .env.prod.example .env
openssl rand -base64 32          # Ergebnis als POSTGRES_PASSWORD eintragen
nano .env                        # PROJECTHUB_DOMAIN, POSTGRES_PASSWORD, ENTRA_TENANT_ID, ENTRA_CLIENT_ID
chmod 600 .env
docker compose -f docker-compose.prod.yml up -d --build
```

Beim ersten Start baut Docker beide Images, `migrate` legt das Datenbankschema an und beendet sich, danach starten API, Oberfläche und Caddy. Caddy holt das Zertifikat beim ersten Aufruf der Domain.

Prüfen:

```bash
docker compose -f docker-compose.prod.yml ps        # migrate: exited (0), alle anderen: running
curl https://projecthub.example.com/health/ready    # Healthy
```

## 3. Erste Anmeldung

`https://projecthub.example.com` öffnen und mit dem Microsoft-Konto anmelden. **Die erste Person wird Organisations-Admin**, alle weiteren werden beim ersten Login als Mitglied angelegt (`PROJECTHUB_USER_PROVISIONING=first-sign-in`). Der Name der Organisation kommt aus `PROJECTHUB_ORGANIZATION_NAME`.

Deshalb: Gleich nach der Installation selbst als Erste/r anmelden. Wer nur bestimmte Personen zulassen will, schaltet in Entra „Zuweisung erforderlich“ ein (Schritt 1.7).

Eine Person sperren (eine Oberfläche dafür gibt es noch nicht; zum Löschen nach DSGVO gibt es „Person anonymisieren“, siehe Abschnitt Rechtliches):

```bash
docker compose -f docker-compose.prod.yml exec postgres \
  psql -U projecthub -d projecthub -c "update app_user set status = 'inactive' where lower(email) = lower('person@example.com')"
```

Gesperrte Personen bleiben gesperrt, auch wenn sie sich erneut anmelden.

## 4. Optionale Integrationen

- **Benachrichtigungsmails:** Ohne Einrichtung verschickt ProjectHub keine Mails (`PROJECTHUB_MAIL_TRANSPORT=fake`); Benachrichtigungen erscheinen trotzdem in der App. Für Mails über ein Microsoft-365-Postfach: `docs/integrations/microsoft-365-setup.md`, dann `PROJECTHUB_MAIL_TRANSPORT=graph` und die `MICROSOFT_GRAPH_*`-Werte in `.env`.
- **Webex:** `docs/integrations/webex-setup.md`, dann `PROJECTHUB_WEBEX_TRANSPORT=bot`, `WEBEX_BOT_TOKEN` und `WEBEX_WEBHOOK_SECRET`.
- **Telemetrie:** `OTEL_EXPORTER_OTLP_ENDPOINT` auf einen OpenTelemetry-Collector (ADR 0013). Ohne: `docker compose -f docker-compose.prod.yml logs api` zeigt die JSON-Logs.

- **KI-Assistent und MCP:** siehe unten.

Nach Änderungen an `.env`: `docker compose -f docker-compose.prod.yml up -d`.

### KI-Assistent und MCP (ADR 0015, ADR 0016)

Ohne Einstellung ist KI aus (`PROJECTHUB_AI_PROVIDER=off`); der Reiter „Assistent“ erscheint dann nicht. Mit einem externen Anbieter verlassen Fragen und gefundene Inhalte den Server; das ist eine Freigabeentscheidung.

Der Assistent kann alles anlegen und ändern, was die angemeldete Person darf (Aufgaben, Wissensartikel, Projekte, Teams …). Jede Änderung zeigt er vorher als Karte; erst mit „Ausführen“ passiert sie. `PROJECTHUB_AI_WRITE_TOOLS=false` macht ihn wieder rein lesend.

**Claude (Standard):** API-Schlüssel in der [Claude Console](https://platform.claude.com) anlegen, dann in `.env`:

```dotenv
PROJECTHUB_AI_PROVIDER=anthropic
ANTHROPIC_API_KEY=sk-ant-...
# leer = claude-opus-5-5; ein neueres Modell nur hier eintragen
PROJECTHUB_AI_MODEL=
# low | medium | high | xhigh | max (mehr Aufwand: bessere Antworten, mehr Zeit und Tokens)
PROJECTHUB_AI_EFFORT=medium
```

**Lokales Modell (Daten bleiben auf dem Server):** jeder OpenAI-kompatible Server, z. B. Ollama als zusätzlicher Dienst in einer `docker-compose.override.yml` neben der Compose-Datei:

```yaml
services:
  ollama:
    image: ollama/ollama
    volumes:
      - ollama:/root/.ollama
volumes:
  ollama:
```

Modell laden (`docker compose -f docker-compose.prod.yml -f docker-compose.override.yml exec ollama ollama pull qwen3:32b`) und in `.env`:

```dotenv
PROJECTHUB_AI_PROVIDER=openai
PROJECTHUB_AI_BASE_URL=http://ollama:11434/v1
PROJECTHUB_AI_MODEL=qwen3:32b
```

Das Modell muss Werkzeugaufrufe (Tool Calling) können. Die Antwortqualität hängt stark vom Modell und von der Hardware ab.

**MCP-Server für Agenten:** `PROJECTHUB_MCP=on` stellt `https://<Domain>/api/v1/mcp` bereit (Streamable HTTP). Agenten wie Claude oder VS Code arbeiten damit als die angemeldete Person: Wissen durchsuchen und lesen, Projekte und Aufgaben ansehen. Mit `PROJECTHUB_MCP_WRITE_TOOLS=true` dürfen sie zusätzlich ändern: Projekte, Aufgaben, Wissensartikel und Teams, immer mit den Rechten der Person; der MCP-Client fragt vor jedem Aufruf nach (ADR 0016).

Anmeldung: Der Server verlangt ein Entra-ID-Token für den Bereich `api://<Client-ID>/access_as_user` und nennt das MCP-Clients selbst (401 mit `resource_metadata`, Dokument unter `/.well-known/oauth-protected-resource/api/v1/mcp`). Entra kennt keine dynamische Client-Registrierung, deshalb:

- Clients mit eigener Microsoft-Anmeldung fragen nach Zustimmung für den Bereich; einmal als Admin zustimmen genügt.
- Andere Clients bekommen die Client-ID von ProjectHub (`ENTRA_CLIENT_ID`) eingetragen. Ihre Redirect-URI kommt in der App-Registrierung unter **Authentifizierung → Plattform hinzufügen → Mobile- und Desktopanwendungen** dazu, und **Öffentliche Clientflows zulassen** steht auf **Ja**.

In der Entwicklung (ohne Entra) meldet der Header `X-Dev-User: dev-ben` am MCP-Endpunkt eine Testperson an, z. B. für den MCP Inspector.

## Rechtliches und Datenschutz

Überblick und Checkliste: `docs/LEGAL.md` (keine Rechtsberatung). Kurz:

```bash
mkdir -p legal
cp deploy/legal/datenschutz.vorlage.html legal/datenschutz.html
nano legal/datenschutz.html                         # alle [eckigen Klammern] ausfüllen
```

Caddy zeigt die Datei unter `https://<Domain>/rechtliches/datenschutz.html`, die App verlinkt sie unten auf jeder Seite (`PROJECTHUB_PRIVACY_NOTICE_URL`). Ein Impressum kommt genauso nach `legal/impressum.html` und wird über `PROJECTHUB_IMPRINT_URL=/rechtliches/impressum.html` eingeblendet. Nach Änderungen an `.env`: `docker compose -f docker-compose.prod.yml up -d`.

ProjectHub löscht Benachrichtigungen, Projektaktivität und Audit-Einträge nach 3 Jahren (`PROJECTHUB_RETENTION_*` in `.env`, `0` = nie). Jede Person lädt ihre Daten unten in der App über „Meine Daten herunterladen“ herunter; Organisations-Admins anonymisieren Personen unter **Teams → Person anonymisieren**.

## 5. Updates

```bash
cd /opt/projecthub
scripts/backup.sh                                   # vorher sichern
git pull
docker compose -f docker-compose.prod.yml up -d --build
```

Neue Migrationen laufen dabei automatisch im Schritt `migrate`, bevor die neue API startet. Migrationen sind nur vorwärts; zurück geht es über die Sicherung. Vor dem Update die neuen Dateien unter `database/migrations/` ansehen (Prüfschritt aus `AGENTS.md`).

## 6. Sicherung und Wiederherstellung

`scripts/backup.sh` sichert erst die Datenbank (`pg_dump`), dann Anhänge und Whiteboard-Dateien nach `backups/` und löscht Sicherungen, die älter als 14 Tage sind (`BACKUP_KEEP_DAYS`). Stündlich per cron:

```cron
0 * * * * /opt/projecthub/scripts/backup.sh >> /var/log/projecthub-backup.log 2>&1
```

Das ergibt höchstens eine Stunde Datenverlust. Die Sicherungen liegen auf demselben Server; mindestens täglich an einen anderen Ort kopieren (z. B. `rclone`, `restic` oder das Backup des Hosters), sonst sind sie beim Verlust des Servers mit weg.

Wiederherstellen (ersetzt **alle** aktuellen Daten, fragt vorher nach):

```bash
scripts/restore.sh backups/projecthub-<Zeit>.dump backups/projecthub-data-<Zeit>.tar.gz
```

Das Skript hält Caddy, Oberfläche und API an, spielt Datenbank und Dateien ein und startet alles wieder. Vor dem Ernstfall einmal üben (`docs/OPERATIONS.md`, Wiederherstellungsübung).

## 7. Grenzen dieses Aufbaus

- **Eine Instanz:** Fällt der Server aus, ist ProjectHub weg, bis er oder die Sicherung wieder läuft. Für mehrere Instanzen: `docs/OPERATIONS.md`, Skalierung.
- **Dateien lokal:** Anhänge liegen im Docker-Volume `projecthub_projecthub-data` ohne Virenscan (DEC-017).
- **Redis ohne Persistenz:** Es trägt nur Live-Nachrichten; ein Neustart verliert nichts.
- Die Images werden auf dem Server gebaut. Dafür braucht der Server Zugriff auf Docker Hub, `mcr.microsoft.com`, NuGet und npm.

## Fehlersuche

| Symptom | Ursache und Abhilfe |
|---|---|
| Reiter „Assistent“ fehlt | `PROJECTHUB_AI_PROVIDER` ist `off`; Fehler beim Start: `docker compose -f docker-compose.prod.yml logs api` nennt die fehlende Einstellung (z. B. `ANTHROPIC_API_KEY`) |
| Assistent meldet „KI-Dienst nicht erreichbar“ | Schlüssel, Modellname oder Endpunkt falsch, oder der Server erreicht den Anbieter nicht (`api.anthropic.com`); Details im API-Log |
| Assistent meldet „Die Freigabe ist abgelaufen“ | Zwischen Vorschlag und Klick lagen mehr als 30 Minuten, die API wurde neu gestartet oder die Freigabe wurde schon beantwortet: Frage noch einmal stellen |
| Browser zeigt Zertifikatsfehler | DNS zeigt nicht auf den Server oder Port 80 ist zu: `docker compose -f docker-compose.prod.yml logs caddy` |
| Microsoft meldet „AADSTS50011: redirect URI mismatch“ | Umleitungs-URI in der App-Registrierung muss genau `https://<Domain>/` sein, Plattform SPA |
| Microsoft meldet „AADSTS65001“ (Zustimmung fehlt) | Schritt 1.5: Administratorzustimmung erteilen |
| Nach der Anmeldung „Anfrage fehlgeschlagen (401)“ | `ENTRA_TENANT_ID`/`ENTRA_CLIENT_ID` in `.env` passen nicht zur Registrierung, oder der Bereich heißt nicht `access_as_user` (dann `ENTRA_API_SCOPE` setzen) |
| Nach der Anmeldung „Anfrage fehlgeschlagen (403)“ | Person ist gesperrt, `PROJECTHUB_USER_PROVISIONING=off`, oder ihre E-Mail-Adresse gehört schon zu einem anderen Konto: `docker compose -f docker-compose.prod.yml logs api` |
| `migrate` endet mit Fehler | `docker compose -f docker-compose.prod.yml logs migrate`; die API startet erst nach erfolgreicher Migration |
