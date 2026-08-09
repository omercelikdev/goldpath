#!/usr/bin/env bash
# An audit (2026-08-09) found the manifest schema accepting five values with no code
# behind them (db: oracle, broker: kafka|inmemory, exporters: prometheus, auditTrail.store:
# eventstream). A schema is a PROMISE an adopter reads before they build; accepting a value
# that cannot run is the most expensive kind of documentation error.
#
# The rule: every such value carries a $comment saying SCHEMA-ONLY / RESERVED and naming
# the ledger row. This gate fails when a roadmap value loses its annotation — or when a new
# one is added without one.
set -euo pipefail
cd "$(dirname "$0")/.."
python3 - <<'PY'
import json, sys
schema = json.load(open("schemas/manifest/v1/goldpath-manifest.schema.json"))
defs = schema["$defs"]
expected = {
    ("providersFull", "db", "oracle"),
    ("providersFull", "broker", "kafka"),
    ("providersFull", "broker", "inmemory"),
}
missing = []
for holder, prop, value in expected:
    node = defs[holder]["properties"][prop]
    if value not in node.get("enum", []):
        continue                      # the value was removed — fine, nothing to annotate
    comment = node.get("$comment", "")
    if "SCHEMA-ONLY" not in comment.upper():
        missing.append(f"{holder}.{prop} accepts '{value}' with no SCHEMA-ONLY $comment")
obs = defs["observability"]["properties"]["exporters"]
if "prometheus" in json.dumps(obs) and "SCHEMA-ONLY" not in obs.get("$comment", "").upper():
    missing.append("observability.exporters accepts 'prometheus' with no SCHEMA-ONLY $comment")
# auditTrail is a oneOf(boolean | object) — the store lives in the object branch.
audit = defs["features"]["properties"]["auditTrail"]
for branch in audit.get("oneOf", [audit]):
    store = branch.get("properties", {}).get("store")
    if not store or "eventstream" not in store.get("enum", []):
        continue
    if "RESERVED" not in store.get("$comment", "").upper():
        missing.append("features.auditTrail.store accepts 'eventstream' with no RESERVED $comment")
if missing:
    print("schema-honesty: values a manifest may declare but no code can run:")
    for m in missing:
        print(f"  - {m}")
    print("Annotate them ($comment) or remove them from the enum.")
    sys.exit(1)
print("schema-honesty: every roadmap-only value is annotated as such.")
PY
