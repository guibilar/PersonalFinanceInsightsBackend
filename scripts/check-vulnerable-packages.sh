#!/bin/sh
# Fails when a package that the API or CLI ships with (direct or transitive) has a known advisory of
# moderate severity or worse. Test-only packages (FinanceTests) do not block. Run after `dotnet restore`.
# In GitHub Actions (T45, as HestiaBackend T55), each finding is also an annotation on the run:
#   - one `::error::` line for each vulnerable shipped package (name, version, severity, advisory);
#   - one `::notice::` line for each vulnerable test-only package (the check ignores these).
# GitHub shows at most 10 annotations of each type per step. The log has the full list.
# Usage: scripts/check-vulnerable-packages.sh [project-or-solution]
set -eu

report=$(dotnet list ${1:+"$1"} package --vulnerable --include-transitive --format json)

# One line per package and version: <kind><TAB><id><TAB><version><TAB><advisories>.
# kind is "shipped" when a project other than FinanceTests uses the package, else "test-only".
findings=$(printf '%s' "$report" | jq -r '
  [ .projects[]
    | (.path | test("FinanceTests")) as $test
    | .frameworks[]?
    | (.topLevelPackages // []) + (.transitivePackages // [])
    | .[]
    | select(any(.vulnerabilities[]?; .severity != "Low"))
    | { id, version: .resolvedVersion, test: $test,
        advisories: [ .vulnerabilities[] | select(.severity != "Low") | "\(.severity) \(.advisoryurl)" ] }
  ]
  | group_by([.id, .version])
  | .[]
  | [ (if all(.[]; .test) then "test-only" else "shipped" end),
      .[0].id, .[0].version, (map(.advisories[]) | unique | join(", ")) ]
  | @tsv')

shipped=$(printf '%s\n' "$findings" | awk -F '\t' '$1 == "shipped"')
test_only=$(printf '%s\n' "$findings" | awk -F '\t' '$1 == "test-only"')

if [ -n "$test_only" ]; then
  echo "Vulnerable test-only packages (they do not block):"
  printf '%s\n' "$test_only" | while IFS="$(printf '\t')" read -r _ id version advisories; do
    echo "  $id $version: $advisories"
    if [ -n "${GITHUB_ACTIONS:-}" ]; then
      echo "::notice title=Vulnerable test-only package::$id $version: $advisories"
    fi
  done
fi

if [ -n "$shipped" ]; then
  echo "Vulnerable packages:"
  printf '%s\n' "$shipped" | while IFS="$(printf '\t')" read -r _ id version advisories; do
    echo "  $id $version: $advisories"
    if [ -n "${GITHUB_ACTIONS:-}" ]; then
      echo "::error title=Vulnerable package::$id $version: $advisories"
    fi
  done
  exit 1
fi
echo "No known vulnerabilities (moderate or worse) in shipped packages"
