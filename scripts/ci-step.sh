#!/usr/bin/env bash
# The shell of the CI "docker" job steps (T45): `shell: bash scripts/ci-step.sh {0}`.
# Runs the step's script with pipefail, as `shell: bash` does, and keeps a copy of its output. When the step fails, it
# writes one ::error:: annotation with the step's first command and the last 20 lines of its output, so a failure can
# be read without the log.
set -uo pipefail
script="$1"
output="$(mktemp)"
bash --noprofile --norc -eo pipefail "$script" 2>&1 | tee "$output"
status=${PIPESTATUS[0]}
if [ "$status" -ne 0 ]; then
  # The first line that is not empty or a comment names the step well enough.
  first=$(grep -v -E '^\s*(#|$)' "$script" | head -1 | cut -c1-120)
  # Annotations need %, CR and LF encoded.
  tail_lines=$(tail -n 20 "$output" | sed -e 's/%/%25/g' -e 's/\r/%0D/g' | awk '{printf "%s%%0A", $0}')
  title=$(printf '%s' "$first" | sed -e 's/%/%25/g' -e 's/:/%3A/g' -e 's/,/%2C/g')
  echo "::error title=Docker step failed (exit $status): $title::$tail_lines"
fi
rm -f "$output"
exit "$status"
