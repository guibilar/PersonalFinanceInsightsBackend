#!/bin/sh
# Fails when a package the API or CLI ships with (direct or transitive) has a known advisory of
# moderate severity or worse. Test-only packages don't block. Run after `dotnet restore`.
# Usage: scripts/check-vulnerable-packages.sh [project-or-solution]
set -eu

report=$(dotnet list ${1:+"$1"} package --vulnerable --include-transitive --format json)
found=$(printf '%s' "$report" | jq -r '
  [ .projects[]
    | select(.path | test("FinanceTests") | not)
    | .frameworks[]?
    | (.topLevelPackages // []) + (.transitivePackages // [])
    | .[]
    | select(any(.vulnerabilities[]; .severity != "Low"))
    | "\(.id) \(.resolvedVersion): \([.vulnerabilities[] | "\(.severity) \(.advisoryurl)"] | join(", "))"
  ] | unique | .[]')

if [ -n "$found" ]; then
  echo "Vulnerable packages:"
  echo "$found"
  exit 1
fi
echo "No known vulnerabilities (moderate or worse) in shipped packages"
