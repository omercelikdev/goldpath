#!/usr/bin/env bash
# Outcome-only acceptance for the goldpath-defect eval. Usage: accept.sh <APP_DIR>
#
# The item that matters is #2: it REVERTS the fix and re-runs the new test, so a test that
# would have passed on both sides of the fix fails acceptance even when the product code is
# correct. That is the cycle's §4 checked instead of trusted.
set -uo pipefail
APP=${1:?usage: accept.sh <generated-app-dir>}
NAME=$(basename "$APP")
if [ -d "$HOME/.dotnet/sdk" ]; then export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"; fi
PASS=0; FAIL=0
check() {
  local out
  if out=$(eval "$2" 2>&1); then echo "  PASS $1"; PASS=$((PASS+1));
  else echo "  FAIL $1"; printf '%s\n' "$out" | tail -n 8 | sed 's/^/       | /'; FAIL=$((FAIL+1)); fi
}

echo "── goldpath-defect eval acceptance: $NAME"
check "specdrift tool on PATH"                   "command -v specdrift"
check "a test asks for the SECOND page"          "grep -rqiE 'nextCursor|second page|page 2' '$APP/tests'"
check "the full suite is green with the fix"     "cd '$APP' && dotnet test --nologo"

# The heart of it: put the fault back and prove the new test notices.
TARGET="$APP/src/$NAME.Api/Orders/Features/GetOrders.cs"
if [ -f "$TARGET.original" ]; then
  cp "$TARGET" "$TARGET.fixed"
  bash "$(dirname "$0")/plant.sh" "$APP" >/dev/null 2>&1 || true
  if (cd "$APP" && dotnet test --nologo >/dev/null 2>&1); then
    echo "  FAIL the test does not fail when the fault is put back — it proves nothing"; FAIL=$((FAIL+1))
  else
    echo "  PASS the test goes red when the fault is restored"; PASS=$((PASS+1))
  fi
  cp "$TARGET.fixed" "$TARGET"; rm -f "$TARGET.fixed"
else
  echo "  FAIL no planted original to restore — run plant.sh before the agent works"; FAIL=$((FAIL+1))
fi

check "specdrift validate clean"                 "specdrift validate '$APP/.goldpath/manifest.yaml' --schema '$(cd "$(dirname "$0")/../../.." && pwd)/schemas/manifest/v1/goldpath-manifest.schema.json' --rules '$APP/.specdrift/rules.yaml'"
check "specdrift drift clean"                    "specdrift drift --repo '$APP'"
check "the manifest was NOT modified"            "cd '$APP' && ! git status --porcelain .goldpath/manifest.yaml 2>/dev/null | grep -q ."
check "format clean"                             "cd '$APP' && dotnet format --verify-no-changes"

echo "── $PASS passed, $FAIL failed"
[ "$FAIL" -eq 0 ]
