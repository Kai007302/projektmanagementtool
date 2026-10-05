# ADR 0019 — Vorgebaute Images und Betrieb als Portainer-Stack

## Status
Proposed

## Kontext

Kai möchte ProjectHub als Stack in Portainer betreiben. `docker-compose.prod.yml` (ADR 0014) setzt einen Checkout des Repositorys auf dem Server voraus:

- Die Images für API und Oberfläche werden auf dem Server gebaut (`build:`). Portainer kann im Web-Editor nicht bauen, und der Server braucht dafür Zugriff auf NuGet, npm und die Basis-Images.
- Caddy liest `./deploy/Caddyfile` und `./legal` als Bind-Mounts mit relativem Pfad. Portainer löst relative Pfade im eigenen Datenordner auf, dort liegen die Dateien nicht.
- `scripts/backup.sh` und `scripts/restore.sh` rufen `docker compose -f docker-compose.prod.yml` auf.

## Entscheidung

- Der Workflow `.github/workflows/images.yml` baut bei jedem Push auf `main` und bei Tags `v*` drei Images und legt sie in der GitHub Container Registry ab: `projecthub-api`, `projecthub-web` und `projecthub-caddy` (Caddy mit eingebautem Caddyfile, `deploy/caddy.Dockerfile`). Tags: `latest`, `sha-<Commit>`, bei Releases die Version.
- `docker-compose.portainer.yml` enthält dieselben Dienste wie `docker-compose.prod.yml`, nur mit diesen Images (`PROJECTHUB_VERSION`, Standard `latest`) und dem Ordner für Datenschutzhinweise als absolutem Pfad (`PROJECTHUB_LEGAL_DIR`, Standard `/opt/projecthub/legal`). Die Einstellungen kommen aus den Umgebungsvariablen des Stacks (`.env.prod.example` laden).
- CI prüft, dass beide Compose-Dateien dieselben Dienste und dieselben Umgebungsvariablen für `api` und `migrate` haben, und validiert das Caddyfile im gebauten Caddy-Image.
- Sicherung und Wiederherstellung finden die Container über die Compose-Labels (`com.docker.compose.project`), funktionieren also ohne Compose-Datei und für beide Varianten. Der Stack heißt deshalb `projecthub` (sonst `PROJECTHUB_PROJECT` setzen).
- Die Images sind öffentlich wie das Repository (Vorschlag DEC-043). Sie enthalten keine Secrets: Mandant, Client-ID und alle anderen Einstellungen kommen zur Laufzeit (ADR 0014).

## Alternativen

- **Git-Stack mit `build:`:** Portainer klont das Repository und baut. Bauen dauert auf dem Server mehrere Minuten, braucht Internetzugriff auf alle Paketquellen, und die relativen Bind-Mounts funktionieren trotzdem nicht zuverlässig.
- **`docker-compose.prod.yml` umstellen statt einer zweiten Datei:** Wer heute mit Checkout betreibt, müsste umziehen; Image und `build:` gemischt verhalten sich je nach Compose-Version unterschiedlich. Zwei Dateien mit CI-Vergleich sind einfacher.
- **Caddyfile als Compose-`configs` mit `content:`:** hängt von der Compose-Version ab, die Portainer mitbringt.

## Konsequenzen

- Ein Update in Portainer ist „Pull and redeploy“; `migrate` läuft dabei vor der neuen API wie bisher.
- `latest` folgt `main`. Wer kontrolliert aktualisieren will, setzt `PROJECTHUB_VERSION` auf einen `sha-…`- oder Versions-Tag.
- Änderungen an Diensten oder Umgebungsvariablen müssen in beide Compose-Dateien; CI erinnert daran.
- Die Images werden erst nach dem Merge dieses Änderungssatzes auf `main` gebaut.
