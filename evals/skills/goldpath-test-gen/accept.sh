#!/usr/bin/env bash
set -uo pipefail
APP=${1:?usage: accept.sh <generated-app-dir> [transcript]}
TRANSCRIPT=${2:-}
NAME=$(basename "$APP")
if [ -d "$HOME/.dotnet/sdk" ]; then export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"; fi
PASS=0; FAIL=0
check() {
  local out
  if out=$(eval "$2" 2>&1); then
    echo "  PASS $1"; PASS=$((PASS+1))
  else
    echo "  FAIL $1"; printf '%s\n' "$out" | tail -n 8 | sed 's/^/       | /'; FAIL=$((FAIL+1))
  fi
}
echo "── goldpath-test-gen eval acceptance: $NAME"
check "cancel contract tests exist (success + rejection)"  "grep -rlq 'Cancel' '$APP/tests/' && grep -rq 'Conflict\|409\|rejected\|Rejected' '$APP/tests/'"
check "suite green"                                        "dotnet test '$APP' --nologo -m:1"
if [ -n "$TRANSCRIPT" ]; then
  check "context diet held (no Features/ reads in transcript)" "! grep -q 'Orders/Features/.*\.cs' '$TRANSCRIPT'"
else
  echo "  SKIP context-diet check (no transcript provided)"
fi
echo "── result: $PASS pass, $FAIL fail"
exit $((FAIL > 0 ? 1 : 0))
