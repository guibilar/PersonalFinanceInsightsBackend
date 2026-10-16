#!/bin/sh
# Dumps the finance database into $BACKUP_DIR (pg_dump's compressed custom format, for pg_restore).
#   backup.sh        dump now, then every day at $BACKUP_AT (the backup container runs this)
#   backup.sh once   dump now and exit (non-zero on failure)
#   backup.sh verify restore the newest dump into a scratch database and check it (Hestia T99)
# After each scheduled dump, the script also restores it into a scratch database (the restore check), unless
# BACKUP_VERIFY=false. It writes the result of both into $BACKUP_DIR/status/finance.json (mode 644, no secrets).
# Hestia.Api reads that file (its FINANCE_BACKUP_STATUS_DIR) for a readiness check and alerts on Today.
# Connection from the standard PGHOST / PGUSER / PGPASSWORD / PGDATABASE variables. Dumps older than
# $BACKUP_KEEP_DAYS days are deleted, but only after a successful dump.
set -u
# The dumps hold the whole database (password hash, cookie keys, sessions): owner-only files.
umask 077

dir=${BACKUP_DIR:-/backups}
keep_days=${BACKUP_KEEP_DAYS:-14}
at=${BACKUP_AT:-03:00}
status_dir="$dir/status"
verify_enabled=${BACKUP_VERIFY:-true}

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

# The state of the newest backup and of the newest restore check. Each value is kept until the next run of its kind
# changes it, in the shell variables below and in the file. The same format as the Hestia backup.sh.
backup_at=""; backup_ok=""; backup_file=""; backup_size=""; backup_error=""
verify_at=""; verify_ok=""; verify_message=""
read_status() {
  file="$status_dir/finance.json"
  [ -f "$file" ] || return 0
  # One value per line (write_status writes them so).
  value() { sed -n -e "s/^  \"$1\": \"\(.*\)\",\{0,1\}$/\1/p" -e "s/^  \"$1\": \([^\",]*\),\{0,1\}$/\1/p" "$file" | head -n 1; }
  backup_at=$(value lastBackupAt); backup_ok=$(value lastBackupOk); backup_file=$(value lastBackupFile)
  backup_size=$(value lastBackupBytes); backup_error=$(value lastBackupError)
  verify_at=$(value lastVerifyAt); verify_ok=$(value lastVerifyOk); verify_message=$(value lastVerifyMessage)
}
# A JSON string or null. Messages never hold quotes or backslashes (clean removes them).
json() { if [ -z "$1" ] || [ "$1" = null ]; then echo null; else echo "\"$1\""; fi; }
json_raw() { if [ -z "$1" ] || [ "$1" = null ]; then echo null; else echo "$1"; fi; }
clean() { printf '%s' "$*" | tr -d '"\\' | tr '\n\r\t' '   ' | cut -c1-300; }
write_status() {
  # The folder and the file are readable by everyone (another user reads them); the dumps stay owner-only.
  mkdir -p "$status_dir" 2>/dev/null && chmod 755 "$status_dir" 2>/dev/null
  [ -d "$status_dir" ] && [ -w "$status_dir" ] || return 0
  # Written under a temporary name first, so a reader never sees half a file.
  { echo "{"
    echo "  \"name\": \"finance\","
    echo "  \"lastBackupAt\": $(json "$backup_at"),"
    echo "  \"lastBackupOk\": $(json_raw "$backup_ok"),"
    echo "  \"lastBackupFile\": $(json "$backup_file"),"
    echo "  \"lastBackupBytes\": $(json_raw "$backup_size"),"
    echo "  \"lastBackupError\": $(json "$backup_error"),"
    echo "  \"lastVerifyAt\": $(json "$verify_at"),"
    echo "  \"lastVerifyOk\": $(json_raw "$verify_ok"),"
    echo "  \"lastVerifyMessage\": $(json "$verify_message")"
    echo "}"
  } > "$status_dir/.finance.json.partial" && chmod 644 "$status_dir/.finance.json.partial" &&
    mv "$status_dir/.finance.json.partial" "$status_dir/finance.json"
}
now_iso() { date -u +%Y-%m-%dT%H:%M:%SZ; }

backup() {
  name="finance-$(date +%Y-%m-%d_%H%M%S).dump"
  # Written under a temporary name first: a dump that fails halfway never looks like a backup.
  if error=$(pg_dump --format=custom --file="$dir/.$name.partial" 2>&1) && mv "$dir/.$name.partial" "$dir/$name"; then
    log "wrote $name ($(du -h "$dir/$name" | cut -f1))"
    find "$dir" -maxdepth 1 -name 'finance-*.dump' -mmin +$((keep_days * 1440)) -print -delete |
      while read -r old; do log "removed $(basename "$old") (older than $keep_days days)"; done
    read_status; backup_at=$(now_iso); backup_ok=true; backup_file=$name; backup_error=""
    backup_size=$(stat -c %s "$dir/$name"); write_status
  else
    rm -f "$dir/.$name.partial"
    log "BACKUP FAILED: $error" >&2
    read_status; backup_at=$(now_iso); backup_ok=false; backup_error=$(clean "pg_dump failed: $error"); write_status
    return 1
  fi
}

# The restore check: restores the newest scheduled dump into a scratch database (never the real one), and checks that
# it has tables with the migration history. Then drops the scratch database.
verify() {
  newest=$(ls -1t "$dir"/finance-*.dump 2>/dev/null | head -n 1)
  read_status
  verify_at=$(now_iso)
  if [ -z "$newest" ]; then
    verify_ok=false; verify_message="No dump to check."; write_status
    log "RESTORE CHECK FAILED: no dump" >&2
    return 1
  fi
  scratch="${PGDATABASE}_restore_check"
  dropdb --if-exists "$scratch" 2>/dev/null
  message=""
  if ! error=$(createdb "$scratch" 2>&1); then
    message="Could not make the scratch database: $error"
  elif ! error=$(pg_restore --no-owner --exit-on-error --dbname="$scratch" "$newest" 2>&1); then
    message="pg_restore failed: $error"
  else
    tables=$(psql --no-psqlrc --tuples-only --no-align --dbname="$scratch" -c \
      "SELECT count(*) FROM information_schema.tables WHERE table_schema NOT IN ('pg_catalog', 'information_schema')")
    migrations=$(psql --no-psqlrc --tuples-only --no-align --dbname="$scratch" -c \
      "SELECT count(*) FROM information_schema.tables WHERE table_name = '__EFMigrationsHistory'")
    if [ "${tables:-0}" -eq 0 ] || [ "${migrations:-0}" -eq 0 ]; then
      message="The dump restored, but it has no tables or no migration history."
    else
      verify_ok=true
      verify_message=$(clean "$(basename "$newest") restored: $tables tables.")
    fi
  fi
  dropdb --if-exists "$scratch" 2>/dev/null
  if [ -n "$message" ]; then
    verify_ok=false; verify_message=$(clean "$(basename "$newest"): $message"); write_status
    log "RESTORE CHECK FAILED: $verify_message" >&2
    return 1
  fi
  write_status
  log "restore check: $verify_message"
}

# The restore check after a scheduled dump, unless it is off. Its failure does not fail the backup.
verify_after_backup() {
  [ "$verify_enabled" = true ] || return 0
  verify || true
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
  verify)
    check_dir && verify
    ;;
  schedule)
    # As the container's main process, stop at once on `docker compose stop`/`down` (a foreground sleep
    # would hold the signal until Docker's 10-second timeout kills the container).
    sleeper=""
    trap 'log "stopping"; [ -n "$sleeper" ] && kill "$sleeper" 2>/dev/null; exit 0' TERM INT
    # Exits, so the container restarts and `docker compose up --wait` fails with the message above.
    check_dir || exit 1
    backup && verify_after_backup
    while true; do
      wait=$(seconds_until_next_run)
      log "next backup at $at (in $((wait / 60)) min)"
      sleep "$wait" &
      sleeper=$!
      wait "$sleeper"
      # A failure is logged and retried the next day; the health check flags a missing recent dump.
      { backup && verify_after_backup; } || true
    done
    ;;
  *)
    echo "usage: $0 [once|verify|schedule]" >&2
    exit 2
    ;;
esac
