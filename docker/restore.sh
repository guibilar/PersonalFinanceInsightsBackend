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
# Owner-only, like the nightly dumps (umask 077).
docker compose exec -T backup sh -c \
  'umask 077 && pg_dump --format=custom --file="${BACKUP_DIR:-/backups}/before-restore-$(date +%Y-%m-%d_%H%M%S).dump"'

docker compose stop app
# One transaction: if anything fails, the database is left as it was. Every schema is dropped first
# (public, the Wolverine outbox schema "wolverine", and any later one; not the system schemas), not with pg_restore --clean, which drops only what the dump has: a dump from before a
# migration that added a table couldn't drop the keys that table points at, and a table or schema it
# doesn't know would be left behind for the next migration to trip over. The API makes the Wolverine
# tables again when it starts, if the dump has none. The connection (PGHOST, PGDATABASE, ...) comes
# from the backup container's environment.
reset_sql="DO \$\$ DECLARE s text; BEGIN
  FOR s IN SELECT nspname FROM pg_namespace
    WHERE nspname NOT LIKE 'pg\\_%' AND nspname <> 'information_schema'
  LOOP EXECUTE format('DROP SCHEMA %I CASCADE', s); END LOOP;
END \$\$; CREATE SCHEMA public;"
status=0
docker compose exec -T -e RESET_SQL="$reset_sql" backup sh -c '
  { printf "%s\n" "$RESET_SQL"; pg_restore --no-owner --file=-; } |
    psql --quiet --no-psqlrc --set ON_ERROR_STOP=1 --single-transaction --output=/dev/null --dbname "postgresql:///"
' < "$dump" || status=$?
docker compose start app
if [ "$status" -eq 0 ]; then
  echo "Restored $dump"
else
  echo "Restore failed; the database was left as it was." >&2
fi
exit "$status"
