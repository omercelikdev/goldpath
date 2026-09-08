#!/usr/bin/env bash
# The agent layer is COPIED to three places in this repository, and the copies are supposed to
# be identical except where a shape genuinely differs. Until 2026-09-08 that was a claim in
# skills/README.md with nothing behind it — and api-portal's stop-gate.sh had already drifted
# from the template's without anyone noticing.
#
# This gate makes the claim checkable: files that must match, match; files that differ, differ
# for a REASON that is written here rather than remembered.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/.." && pwd)
cd "$ROOT"
python3 - "$ROOT" <<'PY'
import os, sys, hashlib
root = sys.argv[1]
SOLUTION = "templates/goldpath-solution/.claude"
COPIES = {
    "worker template": "templates/goldpath-worker/.claude",
    "CorPay": "samples/corpay/.claude",
}

# Deliberate divergences, each with the reason it is not a defect. A file listed here is NOT
# compared; a file NOT listed here must be byte-identical wherever it exists.
EXCEPTIONS = {
    ("worker template", "conventions.md"): "worker conventions: no HTTP surface, no vertical slice",
    ("worker template", "agents/breaker.md"): "the worker's attack surface is its triggers, not an HTTP contract",
    ("worker template", "skills/goldpath-feature/SKILL.md"): "a worker has no OpenAPI artefact; its contracts are its integration events",
    ("worker template", "skills/goldpath-manifest/SKILL.md"): "the worker manifest has a trigger and a narrower feature set",
    ("CorPay", "conventions.md"): "the sample's own domain conventions ride on top of the template's",
}

def digest(path):
    with open(path, "rb") as f:
        return hashlib.md5(f.read()).hexdigest()

source_root = os.path.join(root, SOLUTION)
if not os.path.isdir(source_root):
    print(f"── skills-parity: {SOLUTION} does not exist — nothing to compare"); sys.exit(1)

sources = {}
for base, dirs, names in os.walk(source_root):
    dirs[:] = [d for d in dirs if d not in {"bin", "obj"}]
    for n in names:
        rel = os.path.relpath(os.path.join(base, n), source_root)
        sources[rel] = digest(os.path.join(base, n))

fail, compared, excused = [], 0, 0
for label, copy in COPIES.items():
    copy_root = os.path.join(root, copy)
    if not os.path.isdir(copy_root):
        fail.append(f"{label}: {copy} is missing entirely")
        continue
    for rel, want in sources.items():
        if (label, rel) in EXCEPTIONS:
            excused += 1
            target = os.path.join(copy_root, rel)
            if not os.path.exists(target):
                fail.append(f"{label}: {rel} is excused from matching but does not exist at all")
            continue
        target = os.path.join(copy_root, rel)
        if not os.path.exists(target):
            fail.append(f"{label}: {rel} exists in the template and is missing here")
            continue
        compared += 1
        if digest(target) != want:
            fail.append(f"{label}: {rel} has drifted from the template")

# An exception that no longer applies is stale bookkeeping — it would hide a real drift.
for (label, rel) in EXCEPTIONS:
    if rel not in sources:
        fail.append(f"exception ({label}, {rel}) names a file the template no longer has")

if fail:
    print("── skills-parity: the agent layer has drifted:")
    for f in fail:
        print(f"  {f}")
    sys.exit(1)
print(f"── skills-parity: {compared} files identical across {len(COPIES)} copies, {excused} deliberate divergences accounted for")
PY
