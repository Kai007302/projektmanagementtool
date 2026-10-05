#!/usr/bin/env bash
# Restores a backup from scripts/backup.sh (docs/SELF_HOSTING.md). Replaces ALL current data.
#   scripts/restore.sh backups/projecthub-20261004T120000Z.dump backups/projecthub-data-20261004T120000Z.tar.gz
# Works for docker-compose.prod.yml and for a Portainer stack; PROJECTHUB_PROJECT as in scripts/backup.sh.
set -euo pipefail

if [[ $# -ne 2 || ! -f "$1" || ! -f "$2" ]]; then
  echo "Aufruf: $0 <projecthub-….dump> <projecthub-data-….tar.gz>" >&2
  exit 1
fi

dump="$(cd "$(dirname "$1")" && pwd)/$(basename "$1")"
files="$(cd "$(dirname "$2")" && pwd)/$(basename "$2")"
project="${PROJECTHUB_PROJECT:-projecthub}"

# Container of a service by its compose label, also when it is stopped.
container() {
  docker ps -aq --filter "label=com.docker.compose.project=$project" --filter "label=com.docker.compose.service=$1"
}
postgres="$(container postgres)"
[[ -n "$postgres" ]] || { echo "Kein postgres-Container im Projekt $project (PROJECTHUB_PROJECT)" >&2; exit 1; }

read -r -p "Alle aktuellen Daten werden durch die Sicherung ersetzt. Fortfahren? (ja/nein) " answer
[[ "$answer" == "ja" ]] || exit 1

# Nobody may write while the data is replaced.
docker stop $(container caddy) $(container web) $(container api) > /dev/null

docker exec -i "$postgres" pg_restore -U projecthub -d projecthub --clean --if-exists --no-owner < "$dump"

docker run --rm -v "${project}_projecthub-data:/data" -v "$files:/backup.tar.gz:ro" alpine:3 \
  sh -c 'find /data -mindepth 1 -delete && tar xzf /backup.tar.gz -C /data && chown -R 1654:1654 /data'

# Migrations newer than the backup run again before the API starts.
migrate="$(container migrate)"
docker start "$migrate" > /dev/null
[[ "$(docker wait "$migrate")" == "0" ]] || { echo "Migration fehlgeschlagen: docker logs $migrate" >&2; exit 1; }
docker start $(container api) $(container web) $(container caddy) > /dev/null
echo "Wiederhergestellt aus $(basename "$dump") und $(basename "$files")."
