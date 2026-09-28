#!/bin/sh
# Dumps the finance database into $BACKUP_DIR (pg_dump's compressed custom format, for pg_restore).
#   backup.sh        dump now, then every day at $BACKUP_AT (the backup container runs this)
#   backup.sh once   dump now and exit (non-zero on failure)
# Connection from the standard PGHOST / PGUSER / PGPASSWORD / PGDATABASE variables. Dumps older than
# $BACKUP_KEEP_DAYS days are deleted, but only after a successful dump.
set -u
# The dumps hold the whole database (password hash, cookie keys, sessions): owner-only files.
umask 077

dir=${BACKUP_DIR:-/backups}
keep_days=${BACKUP_KEEP_DAYS:-14}
at=${BACKUP_AT:-03:00}

log() { echo "$(date '+%Y-%m-%d %H:%M:%S') $*"; }

# The container runs as the host user (BACKUP_UID), so it can't write to a folder that belongs to
# root, which is what Docker creates when ./backups doesn't exist yet.
check_dir() {
  mkdir -p "$dir" 2>/dev/null
  [ -d "$dir" ] && [ -w "$dir" ] && return 0
  if [ -d "$dir" ]; then
    log "BACKUP FAILED: $dir belongs to uid $(stat -c %u "$dir") and is not writable by uid $(id -u)." >&2
  else
    log "BACKUP FAILED: cannot create $dir as uid $(id -u)." >&2
  fi
  log "On the host, give the backup folder (BACKUP_DIR, default ./backups) to that user:" >&2
  log "  sudo chown -R $(id -u):$(id -g) backups" >&2
  log "or set BACKUP_UID and BACKUP_GID in .env to the folder owner's (\`id -u\`, \`id -g\`)." >&2
  return 1
}

backup() {
  name="finance-$(date +%Y-%m-%d_%H%M%S).dump"
  # Written under a temporary name first: a dump that fails halfway never looks like a backup.
  if pg_dump --format=custom --file="$dir/.$name.partial" && mv "$dir/.$name.partial" "$dir/$name"; then
    log "wrote $name ($(du -h "$dir/$name" | cut -f1))"
    find "$dir" -maxdepth 1 -name 'finance-*.dump' -mmin +$((keep_days * 1440)) -print -delete |
      while read -r old; do log "removed $(basename "$old") (older than $keep_days days)"; done
  else
    rm -f "$dir/.$name.partial"
    log "BACKUP FAILED" >&2
    return 1
  fi
}

seconds_until_next_run() {
  now=$(date +%s)
  next=$(date -d "today $at" +%s)
  [ "$next" -gt "$now" ] || next=$(date -d "tomorrow $at" +%s)
  echo $((next - now))
}

case "${1:-schedule}" in
  once)
    check_dir && backup
    ;;
  schedule)
    # As the container's main process, stop at once on `docker compose stop`/`down` (a foreground sleep
    # would hold the signal until Docker's 10-second timeout kills the container).
    sleeper=""
    trap 'log "stopping"; [ -n "$sleeper" ] && kill "$sleeper" 2>/dev/null; exit 0' TERM INT
    # Exits, so the container restarts and `docker compose up --wait` fails with the message above.
    check_dir || exit 1
    backup
    while true; do
      wait=$(seconds_until_next_run)
      log "next backup at $at (in $((wait / 60)) min)"
      sleep "$wait" &
      sleeper=$!
      wait "$sleeper"
      # A failure is logged and retried the next day; the health check flags a missing recent dump.
      backup || true
    done
    ;;
  *)
    echo "usage: $0 [once|schedule]" >&2
    exit 2
    ;;
esac
