#!/usr/bin/env bash
# Mutation gate (foundation §8.2: "an ungameable metric"): every package below runs Stryker
# with break-at 70 — below the threshold, no merge. Usage: mutation-gate.sh [PackageName]
#
# Iteration mode: GOLDPATH_MUTATE_SINCE=origin/main mutation-gate.sh Goldpath.X mutates ONLY the
# files changed since that git target (minutes instead of hours). Diff mode has a blind
# spot — deleting a test that guarded UNCHANGED code goes unseen — so the AUTHORITATIVE
# score stays the full run (CI on main / the pre-MR local pass).
set -uo pipefail

ROOT=$(cd "$(dirname "$0")/.." && pwd)
if [ -d "$HOME/.dotnet/sdk" ]; then
  export DOTNET_ROOT="$HOME/.dotnet"
  export PATH="$HOME/.dotnet:$PATH"
fi

cd "$ROOT"
FAILED=()
for cfg in stryker/*.json; do
  name=$(basename "$cfg" .json)
  if [ $# -ge 1 ] && [ "$name" != "$1" ]; then continue; fi
  echo "── mutating $name"
  SINCE_FLAG=""
  if [ -n "${GOLDPATH_MUTATE_SINCE:-}" ]; then
    SINCE_FLAG="--since:${GOLDPATH_MUTATE_SINCE}"
    echo "   (diff mode: only changes since $GOLDPATH_MUTATE_SINCE — full run stays the authoritative score)"
  fi
  # shellcheck disable=SC2086 — the flag is a single token by construction
  if ! dotnet stryker -f "$cfg" $SINCE_FLAG 2>&1 | tail -4; then
    FAILED+=("$name")
  fi
done

if [ ${#FAILED[@]} -gt 0 ]; then
  echo "── MUTATION GATE RED: ${FAILED[*]}"
  exit 1
fi
echo "── MUTATION GATE GREEN"
