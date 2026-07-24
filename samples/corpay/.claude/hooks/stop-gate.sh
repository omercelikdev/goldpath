#!/bin/sh
# Stop hook: the agent may not end its turn on broken output.
# Exit 2 blocks the stop and feeds stderr back to the agent; exit 0 lets the turn end.
# Gates: dotnet build, then specdrift drift (manifest and repository must agree).
INPUT=$(cat)

# Loop guard: if this stop was already blocked once, let it through instead of looping.
case "$INPUT" in
  *'"stop_hook_active":true'* | *'"stop_hook_active": true'*) exit 0 ;;
esac

cd "${CLAUDE_PROJECT_DIR:-.}" || exit 0

# Cheap skip: nothing code-shaped is pending, so there is nothing to gate.
if command -v git >/dev/null 2>&1 && git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  CHANGED=$(git status --porcelain -- '*.cs' '*.csproj' '*.props' '*.yaml' '*.yml' 2>/dev/null)
  [ -z "$CHANGED" ] && exit 0
fi

LOG=$(mktemp)
if ! dotnet build --nologo -v quiet >"$LOG" 2>&1; then
  echo "stop-gate: the build is red — fix it before ending the turn." >&2
  tail -n 30 "$LOG" >&2
  rm -f "$LOG"
  exit 2
fi

if command -v specdrift >/dev/null 2>&1; then
  if ! specdrift drift --repo . --profile .specdrift/drift.yaml >"$LOG" 2>&1; then
    echo "stop-gate: manifest and repository disagree (specdrift drift):" >&2
    tail -n 30 "$LOG" >&2
    rm -f "$LOG"
    exit 2
  fi
else
  echo "stop-gate: specdrift not installed — drift gate skipped (dotnet tool install -g specdrift)." >&2
fi

rm -f "$LOG"
exit 0
