#!/usr/bin/env bash
# The corpus gate: every valid corpus manifest must PASS schema validation and every
# invalid one must FAIL — the negative claims are executed, not assumed. Uses the
# published specdrift tool (pin it before calling: dotnet tool install -g specdrift).
set -uo pipefail
ROOT=$(cd "$(dirname "$0")/.." && pwd)
SCHEMA="$ROOT/schemas/manifest/v1/goldpath-manifest.schema.json"
command -v specdrift >/dev/null 2>&1 || { echo "corpus-check: specdrift not on PATH (dotnet tool install -g specdrift)"; exit 1; }

# The tool must actually RUN, not merely exist. `specdrift` is a dotnet tool shim: on a
# machine whose SDK is missing or on the wrong PATH it launches, fails, and exits
# non-zero — which this gate's invalid half reads as "correctly rejected" and reports as
# PASS. A gate that goes green because its validator is broken is worse than no gate, so
# the shim is proven to work on the KNOWN-GOOD corpus entry before anything is judged.
probe=$(ls "$ROOT"/schemas/manifest/v1/corpus/valid/*.json 2>/dev/null | head -1)
[ -n "$probe" ] || { echo "corpus-check: the valid corpus is empty — there is nothing to prove"; exit 1; }
if ! specdrift validate "$probe" --schema "$SCHEMA" >/dev/null 2>&1; then
  echo "corpus-check: specdrift cannot validate a known-good manifest — the tool itself is broken here:"
  specdrift validate "$probe" --schema "$SCHEMA" 2>&1 | tail -n 8 | sed 's/^/  | /'
  exit 1
fi

PASS=0; FAIL=0
for f in "$ROOT"/schemas/manifest/v1/corpus/valid/*.json; do
  if specdrift validate "$f" --schema "$SCHEMA" >/dev/null 2>&1; then
    echo "  PASS valid/$(basename "$f") validates"; PASS=$((PASS+1))
  else
    echo "  FAIL valid/$(basename "$f") should validate but does not:"
    specdrift validate "$f" --schema "$SCHEMA" 2>&1 | tail -n 5 | sed 's/^/       | /'
    FAIL=$((FAIL+1))
  fi
done
for f in "$ROOT"/schemas/manifest/v1/corpus/invalid/*.json; do
  if specdrift validate "$f" --schema "$SCHEMA" >/dev/null 2>&1; then
    echo "  FAIL invalid/$(basename "$f") should be rejected but validates"; FAIL=$((FAIL+1))
  else
    echo "  PASS invalid/$(basename "$f") rejected"; PASS=$((PASS+1))
  fi
done
echo "── corpus gate: $PASS pass, $FAIL fail"
exit $((FAIL > 0 ? 1 : 0))
