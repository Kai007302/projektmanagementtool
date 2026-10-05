#!/usr/bin/env bash
# Backup of a docker-compose.prod.yml installation or a Portainer stack named "projecthub" (docs/SELF_HOSTING.md):
# database dump, then the files (attachments, whiteboard snapshots), so the files are never older than the database.
# Run e.g. hourly by cron:
#   0 * * * * /opt/projecthub/scripts/backup.sh >> /var/log/projecthub-backup.log 2>&1
# PROJECTHUB_PROJECT: compose project (= Portainer stack name), default projecthub.
set -euo pipefail

cd "$(dirname "$0")/.."
project="${PROJECTHUB_PROJECT:-projecthub}"
target="${BACKUP_DIR:-$PWD/backups}"
keep_days="${BACKUP_KEEP_DAYS:-14}"
stamp="$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p "$target"

# Containers by their compose labels: works without the compose file, so also for Portainer stacks.
postgres="$(docker ps -q --filter "label=com.docker.compose.project=$project" --filter label=com.docker.compose.service=postgres)"
[[ -n "$postgres" ]] || { echo "Kein laufender postgres-Container im Projekt $project (PROJECTHUB_PROJECT)" >&2; exit 1; }

docker exec "$postgres" pg_dump -U projecthub -d projecthub --format=custom > "$target/projecthub-$stamp.dump.partial"
mv "$target/projecthub-$stamp.dump.partial" "$target/projecthub-$stamp.dump"

docker run --rm -v "${project}_projecthub-data:/data:ro" -v "$target:/backups" alpine:3 \
  tar czf "/backups/projecthub-data-$stamp.tar.gz" -C /data .

find "$target" -name 'projecthub-*' -mtime +"$keep_days" -delete
echo "Backup $stamp in $target"
