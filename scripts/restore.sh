#!/usr/bin/env bash
# Restores a backup from scripts/backup.sh (docs/SELF_HOSTING.md). Replaces ALL current data.
#   scripts/restore.sh backups/projecthub-20261004T120000Z.dump backups/projecthub-data-20261004T120000Z.tar.gz
set -euo pipefail

if [[ $# -ne 2 || ! -f "$1" || ! -f "$2" ]]; then
  echo "Aufruf: $0 <projecthub-….dump> <projecthub-data-….tar.gz>" >&2
  exit 1
fi

dump="$(cd "$(dirname "$1")" && pwd)/$(basename "$1")"
files="$(cd "$(dirname "$2")" && pwd)/$(basename "$2")"
cd "$(dirname "$0")/.."
compose=(docker compose -f docker-compose.prod.yml)

read -r -p "Alle aktuellen Daten werden durch die Sicherung ersetzt. Fortfahren? (ja/nein) " answer
[[ "$answer" == "ja" ]] || exit 1

# Nobody may write while the data is replaced.
"${compose[@]}" stop caddy web api

"${compose[@]}" exec -T postgres pg_restore -U projecthub -d projecthub --clean --if-exists --no-owner < "$dump"

docker run --rm -v projecthub_projecthub-data:/data -v "$files:/backup.tar.gz:ro" alpine:3 \
  sh -c 'find /data -mindepth 1 -delete && tar xzf /backup.tar.gz -C /data && chown -R 1654:1654 /data'

# Migrations newer than the backup run again before the API starts.
"${compose[@]}" up -d
echo "Wiederhergestellt aus $(basename "$dump") und $(basename "$files")."
