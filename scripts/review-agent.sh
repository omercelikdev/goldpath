#!/usr/bin/env bash
# Review agent v1 (docs/strategy/review-agent-v1.md): a second AI set of eyes on a PR
# BEFORE human review. Gathers the diff + the manifests/specs the PR touches + the PR
# description (context economy: never the whole repo), runs the rule set through the
# Claude CLI, and posts ONE consolidated comment + labels. It never blocks on its own;
# the two hard-stop classes only add a label a human must resolve.
#
# Usage: review-agent.sh <PR-NUMBER> [--dry-run]
#   --dry-run: print the comment instead of posting (the local proof mode).
# Needs: gh (authenticated), claude (the CLI), python3.
set -euo pipefail

PR=${1:?usage: review-agent.sh <PR-NUMBER>|--local <diff-file> [--dry-run]}
DRY=${2:-}
LOCAL_DIFF=""
if [ "$PR" = "--local" ]; then
  # Local mode: review a raw diff file with no GitHub round-trip — the harness the
  # finding-path proof (and any future eval) runs on.
  LOCAL_DIFF=${2:?usage: review-agent.sh --local <diff-file>}
  DRY="--dry-run"
fi
ROOT=$(cd "$(dirname "$0")/.." && pwd)
cd "$ROOT"

if [ -z "$LOCAL_DIFF" ]; then
  command -v gh >/dev/null || { echo "review-agent: gh not found — install and authenticate the GitHub CLI first." >&2; exit 1; }
fi
command -v claude >/dev/null || { echo "review-agent: claude not found — the agent thinks with the Claude CLI; install it (or run this step only where it exists)." >&2; exit 1; }

WORK=$(mktemp -d "${TMPDIR:-/tmp}/goldpath-review.XXXXXX")
trap 'rm -rf "$WORK"' EXIT

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

python3 - "$WORK" <<'PY'
import json, re, sys
work = sys.argv[1]
raw = open(f"{work}/verdict.raw").read()
# The contract is bare JSON; tolerate a fenced block, nothing looser.
match = re.search(r"\{.*\}", raw, re.DOTALL)
if not match:
    sys.exit("review-agent: the model broke the output contract (no JSON object) — raw output kept in " + work)
verdict = json.loads(match.group(0))
findings = verdict.get("findings", [])

labels = sorted({
    {"R1": "review:spec-mismatch", "R2": "review:domain", "R3": "review:logic",
     "R4": "review:security", "R5": "review:test-quality", "R6": "review:simplify"}[f["class"]]
    for f in findings if f.get("class") in {"R1","R2","R3","R4","R5","R6"}
})
if any(f.get("class") in {"R2", "R4"} and f.get("confidence") == "high" for f in findings):
    labels.append("review:hard-stop")

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

if [ "$DRY" = "--dry-run" ]; then
  echo "── review-agent: dry run — nothing posted (labels would be: $(cat "$WORK/labels.txt"))"
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
echo "── review-agent: done"
