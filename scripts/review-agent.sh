#!/usr/bin/env bash
# Review agent v1 (docs/strategy/review-agent-v1.md): a second AI set of eyes on a PR
# BEFORE human review. Gathers the diff + the manifests/specs the PR touches + the PR
# description (context economy: never the whole repo), runs the rule set through the
# Claude CLI, and posts ONE consolidated comment + labels. It never blocks on its own;
# the two hard-stop classes only add a label a human must resolve.
#
# Usage: review-agent.sh <PR-NUMBER> [--dry-run] [--gate]
#   --dry-run: print the comment instead of posting (the local proof mode).
#   --gate:    exit 3 when a hard-stop finding lands (the CI step's teeth — strategy §0:
#              the agent never blocks on its own EXCEPT the two hard-stop classes).
# REVIEW_AGENT_ARTIFACT_DIR, when set, receives verdict.raw/note.md/labels.txt even on
# success — CI uploads it, so the verdict outlives the runner (the temp dir does not).
# Needs: gh (authenticated), claude (the CLI), python3.
set -euo pipefail

PR=${1:?usage: review-agent.sh <PR-NUMBER>|--local <diff-file> [--dry-run] [--gate]}
DRY=""
GATE=""
LOCAL_DIFF=""
if [ "$PR" = "--local" ]; then
  # Local mode: review a raw diff file with no GitHub round-trip — the harness the
  # finding-path proof (and any future eval) runs on.
  LOCAL_DIFF=${2:?usage: review-agent.sh --local <diff-file>}
  DRY="--dry-run"
fi
for arg in "${@:2}"; do
  case "$arg" in
    --dry-run) DRY="--dry-run" ;;
    --gate) GATE=1 ;;
  esac
done
ROOT=$(cd "$(dirname "$0")/.." && pwd)
cd "$ROOT"

if [ -z "$LOCAL_DIFF" ]; then
  command -v gh >/dev/null || { echo "review-agent: gh not found — install and authenticate the GitHub CLI first." >&2; exit 1; }
fi
command -v claude >/dev/null || { echo "review-agent: claude not found — the agent thinks with the Claude CLI; install it (or run this step only where it exists)." >&2; exit 1; }

WORK=$(mktemp -d "${TMPDIR:-/tmp}/goldpath-review.XXXXXX")
# On a clean exit the scratch dir goes; on a FAILURE it stays, because the message below
# promises the raw output is there — and a promise the script breaks is worse than none.
trap '[ "$?" = "0" ] && rm -rf "$WORK"' EXIT

if [ -n "$LOCAL_DIFF" ]; then
  echo "── review-agent: local mode ($LOCAL_DIFF)"
  {
    echo "# MR under review"
    echo "TITLE: (local diff)"
    echo
    echo "## MR description (the promise the diff must keep)"
    echo "(none)"
    echo
    echo "## Diff"
    echo '```diff'
    cat "$LOCAL_DIFF"
    echo '```'
  } > "$WORK/context.md"
else
echo "── review-agent: gathering PR #$PR"
gh pr view "$PR" --json title,body > "$WORK/pr.json"
gh pr diff "$PR" > "$WORK/pr.diff"

python3 - "$WORK" "$ROOT" <<'PY'
import json, os, sys
work, root = sys.argv[1], sys.argv[2]
pr = json.load(open(f"{work}/pr.json"))
diff = open(f"{work}/pr.diff").read()

parts = [
    "# PR under review",
    f"TITLE: {pr['title']}",
    "",
    "## PR description (the promise the diff must keep)",
    pr.get("body") or "(none)",
    "",
    "## Diff",
    "```diff",
    diff.rstrip("\n"),
    "```",
]
touched = [line.split(" b/", 1)[1].strip() for line in diff.splitlines() if line.startswith("diff --git ")]

# Context economy: only the manifests and specs that sit NEXT TO the touched code.
context_files = set()
for path in touched:
    probe = os.path.dirname(path)
    while probe:
        for candidate in (f"{probe}/.goldpath/manifest.yaml", f"{probe}/manifest.yaml"):
            if os.path.exists(os.path.join(root, candidate)):
                context_files.add(candidate)
        probe = os.path.dirname(probe)
    if path.startswith("specs/") or "/specs/" in path:
        context_files.add(path)

if context_files:
    parts.append("\n## Touched-service context (manifests/specs — approved material only)")
    for candidate in sorted(context_files):
        full = os.path.join(root, candidate)
        if os.path.exists(full):
            parts.append(f"### {candidate}")
            parts.append("```")
            parts.append(open(full).read().rstrip("\n"))
            parts.append("```")

open(f"{work}/context.md", "w").write("\n".join(parts))
print(f"   {len(touched)} files in the diff, {len(context_files)} context files")
PY
fi

echo "── review-agent: thinking (rule set: .claude/skills/goldpath-review/SKILL.md)"
cat .claude/skills/goldpath-review/SKILL.md "$WORK/context.md" > "$WORK/prompt.md"
claude -p < "$WORK/prompt.md" > "$WORK/verdict.raw"

# Contract hardening: extraction tolerates a fenced block or surrounding prose but accepts
# ONLY a complete, schema-valid verdict — a finding missing class/claim is a broken
# contract, not a finding to guess at. One corrective retry, then fail loudly.
render_verdict() {
python3 - "$WORK" <<'PY'
import json, re, sys
work = sys.argv[1]
raw = open(f"{work}/verdict.raw").read()

def candidates(text):
    # Fenced blocks first (the contract tolerates exactly that much), then the bare text,
    # then every balanced top-level {...} — two objects in one answer stay two candidates
    # instead of one unparseable span (the old greedy regex's failure mode).
    for fenced in re.findall(r"```(?:json)?\s*(\{.*?\})\s*```", text, re.DOTALL):
        yield fenced
    yield text.strip()
    depth, start = 0, None
    for index, ch in enumerate(text):
        if ch == "{":
            if depth == 0:
                start = index
            depth += 1
        elif ch == "}" and depth > 0:
            depth -= 1
            if depth == 0:
                yield text[start : index + 1]

CLASSES = {"R1", "R2", "R3", "R4", "R5", "R6"}
verdict = None
for candidate in candidates(raw):
    try:
        parsed = json.loads(candidate)
    except json.JSONDecodeError:
        continue
    if isinstance(parsed, dict) and isinstance(parsed.get("findings"), list):
        verdict = parsed
        break
if verdict is None:
    sys.exit(42)

findings = verdict["findings"]
if not all(isinstance(f, dict) and f.get("class") in CLASSES and f.get("claim") for f in findings):
    sys.exit(42)   # a shapeless finding is a broken contract, not data

labels = sorted({
    {"R1": "review:spec-mismatch", "R2": "review:domain", "R3": "review:logic",
     "R4": "review:security", "R5": "review:test-quality", "R6": "review:simplify"}[f["class"]]
    for f in findings
} | ({"review:hard-stop"} if any(
    f["class"] in {"R2", "R4"} and f.get("confidence") == "high" for f in findings) else set()))

if not findings:
    body = "**Review agent v1** — R1–R6 scanned, no findings."
else:
    lines = [f"**Review agent v1** — {len(findings)} finding(s). The human decides; hard-stop labels need explicit resolution.", ""]
    for f in findings:
        where = f"`{f.get('file','?')}:{f.get('line','?')}`"
        lines.append(f"- **{f.get('class')}** ({f.get('confidence')}) {where} — {f.get('claim')}")
        lines.append(f"  - evidence: {f.get('evidence')} · action: {f.get('action')}")
    lines.append("")
    lines.append("_Calibration: mark each finding accepted/dismissed in a reply — dismiss rate >40%/class revises that class (strategy §5)._")
    body = "\n".join(lines)

json.dump({"body": body}, open(f"{work}/note.json", "w"))
open(f"{work}/labels.txt", "w").write(",".join(labels))
print(body)
PY
}

# CI has no durable temp dir: when asked, whatever exists survives the runner — called on
# the failure paths too, because the double-break verdict.raw is exactly the artifact that
# explains a red run (review R3 on this very script's PR).
persist_artifacts() {
  if [ -n "${REVIEW_AGENT_ARTIFACT_DIR:-}" ]; then
    mkdir -p "$REVIEW_AGENT_ARTIFACT_DIR"
    cp "$WORK/verdict.raw" "$WORK/note.json" "$WORK/labels.txt" "$REVIEW_AGENT_ARTIFACT_DIR/" 2>/dev/null || true
  fi
}

if ! render_verdict; then
  echo "── review-agent: the model broke the output contract — one corrective retry"
  cp "$WORK/verdict.raw" "$WORK/verdict.first-attempt.raw"
  {
    cat "$WORK/prompt.md"
    echo
    echo "REMINDER: the previous answer broke the output contract. Respond with EXACTLY one"
    echo "JSON object matching the contract above — no prose around it, no second object,"
    echo "and every finding carries class (R1–R6) and claim."
  } > "$WORK/prompt-retry.md"
  claude -p < "$WORK/prompt-retry.md" > "$WORK/verdict.raw"
  if ! render_verdict; then
    persist_artifacts
    [ -n "${REVIEW_AGENT_ARTIFACT_DIR:-}" ] && cp "$WORK/verdict.first-attempt.raw" "$REVIEW_AGENT_ARTIFACT_DIR/" 2>/dev/null || true
    # A broken CONTRACT is not a missing REVIEW. Both attempts usually contain real findings
    # in prose — this path used to exit silently and drop them on the floor, which is the
    # worst failure a review tool can have: it looks like "no findings" to whoever merges.
    # PR #161 proved it: the unparsed verdict named two stale saga promises in adjacent RFCs
    # that the parsed run would have reported. Post the raw text, clearly labelled unparsed,
    # and still exit 1 so CI treats the run as failed.
    if [ "$PR" != "--local" ] && [ "$DRY" != "--dry-run" ]; then
      {
        echo "## Review agent — OUTPUT CONTRACT BROKE (unparsed verdict)"
        echo
        echo "The agent answered twice without matching the JSON contract, so nothing could be"
        echo "rendered into findings. **The prose below is unverified and unlabelled — read it"
        echo "yourself; do not treat this comment as 'no findings'.**"
        echo
        echo "### Second attempt"
        echo '```'; cat "$WORK/verdict.raw"; echo '```'
        echo
        echo "### First attempt"
        echo '```'; cat "$WORK/verdict.first-attempt.raw"; echo '```'
      } > "$WORK/broken.md"
      gh pr comment "$PR" --body-file "$WORK/broken.md" >/dev/null 2>&1 \
        && echo "── review-agent: raw verdict posted to PR #$PR (unparsed)"
    fi
    echo "review-agent: the output contract broke twice — raw output kept in $WORK" >&2
    exit 1
  fi
fi

persist_artifacts

gate() {
  if [ -n "$GATE" ] && grep -q "review:hard-stop" "$WORK/labels.txt"; then
    echo "── review-agent: hard-stop finding — gating the PR (exit 3)"
    exit 3
  fi
}

if [ "$DRY" = "--dry-run" ]; then
  echo "── review-agent: dry run — nothing posted (labels would be: $(cat "$WORK/labels.txt"))"
  gate
  exit 0
fi

echo "── review-agent: posting the consolidated comment"
python3 -c "import json,sys; print(json.load(open(sys.argv[1]))['body'])" "$WORK/note.json" > "$WORK/note.md"
gh pr comment "$PR" --body-file "$WORK/note.md" >/dev/null
LABELS=$(cat "$WORK/labels.txt")
if [ -n "$LABELS" ]; then
  IFS=, read -ra LABEL_ARR <<< "$LABELS"
  for label in "${LABEL_ARR[@]}"; do
    gh label create "$label" --force >/dev/null 2>&1 || true
  done
  gh pr edit "$PR" --add-label "$LABELS" >/dev/null
  echo "── review-agent: labels applied: $LABELS"
fi
gate
echo "── review-agent: done"
