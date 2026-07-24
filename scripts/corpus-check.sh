#!/usr/bin/env bash
# The corpus gate: every valid corpus manifest must PASS schema validation and every
# invalid one must FAIL — the negative claims are executed, not assumed. Uses the
# published specdrift tool (pin it before calling: dotnet tool install -g specdrift).
set -uo pipefail
ROOT=$(cd "$(dirname "$0")/.." && pwd)
SCHEMA="$ROOT/schemas/manifest/v1/goldpath-manifest.schema.json"
command -v specdrift >/dev/null 2>&1 || { echo "corpus-check: specdrift not on PATH (dotnet tool install -g specdrift)"; exit 1; }

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
