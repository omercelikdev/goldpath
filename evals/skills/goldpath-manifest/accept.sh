#!/usr/bin/env bash
set -uo pipefail
APP=${1:?usage: accept.sh <generated-app-dir>}
NAME=$(basename "$APP"); ROOT=$(cd "$(dirname "$0")/../../.." && pwd)
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
echo "── goldpath-manifest eval acceptance: $NAME"
check "specdrift tool on PATH (dotnet tool install -g specdrift)" "command -v specdrift"
check "manifest declares softDelete"       "grep -q 'softDelete: true' '$APP/.goldpath/manifest.yaml'"
check "csproj references Goldpath.SoftDelete"   "grep -rq '\"Goldpath.SoftDelete\"' '$APP/src/$NAME.Api/'"
check "AddGoldpathSoftDelete registered"        "grep -rq 'AddGoldpathSoftDelete' '$APP/src/$NAME.Api/'"
check "ApplyGoldpathSoftDelete in the model"    "grep -rq 'ApplyGoldpathSoftDelete' '$APP/src/$NAME.Api/'"
check "spec_validate clean"                "specdrift validate '$APP/.goldpath/manifest.yaml' --schema '$ROOT/schemas/manifest/v1/goldpath-manifest.schema.json' --rules '$APP/.specdrift/rules.yaml'"
check "spec_drift clean"                   "specdrift drift --repo '$APP'"
check "build green"                        "dotnet build '$APP' -v q -m:1 --nologo"
check "tests green"                        "dotnet test '$APP' --no-build --nologo"
echo "── result: $PASS pass, $FAIL fail"
exit $((FAIL > 0 ? 1 : 0))
