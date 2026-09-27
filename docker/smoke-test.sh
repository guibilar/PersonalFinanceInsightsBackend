#!/bin/sh
# Checks a running API end to end: health, sign-in, the session cookie and a protected call.
# Usage: docker/smoke-test.sh [base-url] (default http://localhost:5096)
# The account comes from FINANCE_USERNAME / FINANCE_PASSWORD, as in .env.
set -u

base=${1:-http://localhost:5096}
user=${FINANCE_USERNAME:?Set FINANCE_USERNAME}
pass=${FINANCE_PASSWORD:?Set FINANCE_PASSWORD}
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
failures=0

check() { # description, expected, actual
  if [ "$2" = "$3" ]; then
    echo "ok   $1"
  else
    echo "FAIL $1: expected $2, got $3"
    failures=$((failures + 1))
  fi
}

status() { # curl arguments; prints the status code
  curl -s -o "$work/body" -w '%{http_code}' "$@"
}

has() { # file, case-insensitive pattern; prints yes/no
  if grep -qi -- "$2" "$1"; then echo yes; else echo no; fi
}

login() { # password, extra curl arguments; headers go to $work/headers
  body=$(printf '{"username":"%s","password":"%s"}' "$user" "$1")
  shift
  status -D "$work/headers" -H 'Content-Type: application/json' -d "$body" "$@" "$base/api/Auth/login"
}

check "live" 200 "$(status "$base/health/live")"
check "ready (database reachable)" 200 "$(status "$base/health/ready")"
check "Swagger is not served outside Development" no "$([ "$(status "$base/swagger/index.html")" = 200 ] && echo yes || echo no)"

check "signed out: who am I" 401 "$(status "$base/api/Auth/me")"
check "signed out: budgets" 401 "$(status -H 'Content-Type: application/json' -d '{}' "$base/api/Budget/Filter")"
check "wrong password" 401 "$(login "not-the-$pass")"

check "sign in behind the HTTPS proxy" 200 "$(login "$pass" -H 'X-Forwarded-Proto: https' -c "$work/cookies")"
grep -i '^set-cookie: finance_session=' "$work/headers" > "$work/cookie"
check "session cookie is set" yes "$(has "$work/cookie" 'finance_session=')"
check "session cookie is HttpOnly" yes "$(has "$work/cookie" '; httponly')"
check "session cookie is SameSite=Strict" yes "$(has "$work/cookie" '; samesite=strict')"
check "session cookie is Secure over HTTPS" yes "$(has "$work/cookie" '; secure')"

check "signed in: who am I" 200 "$(status -b "$work/cookies" "$base/api/Auth/me")"
check "signed in: the username comes back" yes "$(has "$work/body" "\"username\":\"$user\"")"
check "signed in: budgets" 200 "$(status -b "$work/cookies" -H 'Content-Type: application/json' -d '{}' "$base/api/Budget/Filter")"
check "sign out" 204 "$(status -b "$work/cookies" -c "$work/cookies" -X POST "$base/api/Auth/logout")"
check "signed out again" 401 "$(status -b "$work/cookies" "$base/api/Auth/me")"

# Plain HTTP (no proxy header): the cookie must not be marked Secure, or the browser would drop it.
check "sign in over plain HTTP" 200 "$(login "$pass")"
check "session cookie is not Secure over plain HTTP" no "$(has "$work/headers" '^set-cookie: finance_session=.*; secure')"

if [ "$failures" -gt 0 ]; then
  echo "$failures check(s) failed"
  exit 1
fi
echo "All checks passed"
