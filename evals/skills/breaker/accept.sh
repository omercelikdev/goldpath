#!/usr/bin/env bash
set -uo pipefail
APP=${1:?usage: accept.sh <generated-app-dir>}
if [ -d "$HOME/.dotnet/sdk" ]; then export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"; fi
PASS=0; FAIL=0
check() { if eval "$2" >/dev/null 2>&1; then echo "  PASS $1"; PASS=$((PASS+1)); else echo "  FAIL $1"; FAIL=$((FAIL+1)); fi; }
echo "── breaker eval acceptance"
check "Breaker_ scenarios exist as tests"  "grep -rlq 'Breaker_' '$APP/tests/'"
check "verdict file exists and names targets" "test -s '$APP/tests/BREAKER-VERDICT.md' && grep -qi 'target' '$APP/tests/BREAKER-VERDICT.md'"
check "suite runs to completion"           "dotnet test '$APP' --nologo -m:1 || grep -qi 'failure' '$APP/tests/BREAKER-VERDICT.md'"
echo "── result: $PASS pass, $FAIL fail"
exit $((FAIL > 0 ? 1 : 0))
