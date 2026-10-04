#!/usr/bin/env bash
# Backup of a docker-compose.prod.yml installation (docs/SELF_HOSTING.md): database dump, then the files
# (attachments, whiteboard snapshots), so the files are never older than the database. Run e.g. hourly by cron:
#   0 * * * * /opt/projecthub/scripts/backup.sh >> /var/log/projecthub-backup.log 2>&1
set -euo pipefail

cd "$(dirname "$0")/.."
compose=(docker compose -f docker-compose.prod.yml)
target="${BACKUP_DIR:-$PWD/backups}"
keep_days="${BACKUP_KEEP_DAYS:-14}"
stamp="$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p "$target"

"${compose[@]}" exec -T postgres pg_dump -U projecthub -d projecthub --format=custom > "$target/projecthub-$stamp.dump.partial"
mv "$target/projecthub-$stamp.dump.partial" "$target/projecthub-$stamp.dump"

docker run --rm -v projecthub_projecthub-data:/data:ro -v "$target:/backups" alpine:3 \
  tar czf "/backups/projecthub-data-$stamp.tar.gz" -C /data .

find "$target" -name 'projecthub-*' -mtime +"$keep_days" -delete
echo "Backup $stamp in $target"
