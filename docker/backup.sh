#!/bin/sh
# Dumps the finance database into $BACKUP_DIR (pg_dump's compressed custom format, for pg_restore).
#   backup.sh        dump now, then every day at $BACKUP_AT (the backup container runs this)
#   backup.sh once   dump now and exit (non-zero on failure)
# Connection from the standard PGHOST / PGUSER / PGPASSWORD / PGDATABASE variables. Dumps older than
# $BACKUP_KEEP_DAYS days are deleted, but only after a successful dump.
set -u

dir=${BACKUP_DIR:-/backups}
keep_days=${BACKUP_KEEP_DAYS:-14}
at=${BACKUP_AT:-03:00}

log() { echo "$(date '+%Y-%m-%d %H:%M:%S') $*"; }

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
    backup
    ;;
  schedule)
    mkdir -p "$dir"
    backup
    while true; do
      wait=$(seconds_until_next_run)
      log "next backup at $at (in $((wait / 60)) min)"
      sleep "$wait"
      # A failure is logged and retried the next day; the health check flags a missing recent dump.
      backup || true
    done
    ;;
  *)
    echo "usage: $0 [once|schedule]" >&2
    exit 2
    ;;
esac
