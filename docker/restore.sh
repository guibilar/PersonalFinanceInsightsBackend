#!/bin/sh
# Restores a dump written by backup.sh over the current database. Run it from the directory with
# docker-compose.yml, with the stack up:
#   docker/restore.sh backups/finance-2026-09-28_030000.dump
# Everything stored since that dump is replaced, so the current database is saved first, as
# before-restore-<time>.dump in the backup folder (never deleted automatically). The API is stopped
# while the restore runs.
set -eu

dump=${1:?usage: docker/restore.sh <path to .dump file>}
[ -f "$dump" ] || { echo "No such file: $dump" >&2; exit 1; }

echo "Saving the current database first..."
docker compose exec -T backup sh -c \
  'pg_dump --format=custom --file="${BACKUP_DIR:-/backups}/before-restore-$(date +%Y-%m-%d_%H%M%S).dump"'

docker compose stop app
# One transaction: if anything fails, the database is left as it was. The connection (PGHOST,
# PGDATABASE, ...) comes from the backup container's environment.
status=0
docker compose exec -T backup pg_restore --clean --if-exists --no-owner --single-transaction \
  --dbname "postgresql:///" < "$dump" || status=$?
docker compose start app
if [ "$status" -eq 0 ]; then
  echo "Restored $dump"
else
  echo "Restore failed; the database was left as it was." >&2
fi
exit "$status"
