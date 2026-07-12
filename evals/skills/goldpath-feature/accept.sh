#!/usr/bin/env bash
# Outcome-only acceptance for the goldpath-feature eval (foundation §5). Usage: accept.sh <APP_DIR>
set -uo pipefail
APP=${1:?usage: accept.sh <generated-app-dir>}
NAME=$(basename "$APP")
ROOT=$(cd "$(dirname "$0")/../../.." && pwd)
if [ -d "$HOME/.dotnet/sdk" ]; then export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"; fi
SPECDRIFT_SRC="${SPECDRIFT_SRC:-$HOME/Repositories/specdrift}"
PASS=0; FAIL=0
check() { if eval "$2" >/dev/null 2>&1; then echo "  PASS $1"; PASS=$((PASS+1)); else echo "  FAIL $1"; FAIL=$((FAIL+1)); fi; }

echo "── goldpath-feature eval acceptance: $NAME"
check "cancel operation in the COMMITTED OpenAPI"        "grep -qi 'cancel' '$APP/specs/$NAME.Api.json'"
check "slice shape: Orders/Features/CancelOrder.cs"      "test -s '$APP/src/$NAME.Api/Orders/Features/CancelOrder.cs'"
check "tests referencing CancelOrder exist"              "grep -rql 'CancelOrder' '$APP/tests/'"
check "manifest untouched by the feature"                "! git -C '$APP' status --porcelain 2>/dev/null | grep -q manifest.yaml || true; grep -q 'outbox' '$APP/.goldpath/manifest.yaml'"
check "build green (analyzers ride the compiler)"        "dotnet build '$APP' -v q -m:1 --nologo"
check "full test suite green"                            "dotnet test '$APP' --no-build --nologo"
check "format clean"                                     "dotnet format '$APP/$NAME.sln' --verify-no-changes"
check "spec_validate clean (schema + rules)"             "dotnet run --project '$SPECDRIFT_SRC/src/Specdrift' -- validate '$APP/.goldpath/manifest.yaml' --schema '$ROOT/schemas/manifest/v1/goldpath-manifest.schema.json' --rules '$APP/.specdrift/rules.yaml'"
check "spec_drift clean (contract re-exported)"          "dotnet run --project '$SPECDRIFT_SRC/src/Specdrift' -- drift --repo '$APP'"

echo "── result: $PASS pass, $FAIL fail"
exit $((FAIL > 0 ? 1 : 0))
