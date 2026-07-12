#!/usr/bin/env bash
set -uo pipefail
APP=${1:?usage: accept.sh <generated-app-dir>}
NAME=$(basename "$APP"); ROOT=$(cd "$(dirname "$0")/../../.." && pwd)
if [ -d "$HOME/.dotnet/sdk" ]; then export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"; fi
SPECDRIFT_SRC="${SPECDRIFT_SRC:-$HOME/Repositories/specdrift}"
PASS=0; FAIL=0
check() { if eval "$2" >/dev/null 2>&1; then echo "  PASS $1"; PASS=$((PASS+1)); else echo "  FAIL $1"; FAIL=$((FAIL+1)); fi; }
echo "── goldpath-manifest eval acceptance: $NAME"
check "manifest declares softDelete"       "grep -q 'softDelete: true' '$APP/.goldpath/manifest.yaml'"
check "csproj references Goldpath.SoftDelete"   "grep -rq '\"Goldpath.SoftDelete\"' '$APP/src/$NAME.Api/'"
check "AddGoldpathSoftDelete registered"        "grep -rq 'AddGoldpathSoftDelete' '$APP/src/$NAME.Api/'"
check "ApplyGoldpathSoftDelete in the model"    "grep -rq 'ApplyGoldpathSoftDelete' '$APP/src/$NAME.Api/'"
check "spec_validate clean"                "dotnet run --project '$SPECDRIFT_SRC/src/Specdrift' -- validate '$APP/.goldpath/manifest.yaml' --schema '$ROOT/schemas/manifest/v1/goldpath-manifest.schema.json' --rules '$APP/.specdrift/rules.yaml'"
check "spec_drift clean"                   "dotnet run --project '$SPECDRIFT_SRC/src/Specdrift' -- drift --repo '$APP'"
check "build green"                        "dotnet build '$APP' -v q -m:1 --nologo"
check "tests green"                        "dotnet test '$APP' --no-build --nologo"
echo "── result: $PASS pass, $FAIL fail"
exit $((FAIL > 0 ? 1 : 0))
