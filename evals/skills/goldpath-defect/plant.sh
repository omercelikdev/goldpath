#!/usr/bin/env bash
# Plants the eval's fault into a generated app: the keyset cursor reports the FIRST row of the
# page instead of the last, so page two repeats page one. Usage: plant.sh <APP_DIR>
set -euo pipefail
APP=${1:?usage: plant.sh <generated-app-dir>}
NAME=$(basename "$APP")
TARGET="$APP/src/$NAME.Api/Orders/Features/GetOrders.cs"
[ -f "$TARGET" ] || { echo "plant: $TARGET not found — is this a default generated app?" >&2; exit 2; }
cp "$TARGET" "$TARGET.original"
python3 - "$TARGET" <<'PY'
import sys, re
p = sys.argv[1]
s = open(p).read()
# The fault: take the cursor from the first item rather than the last. Deliberately small and
# plausible — the kind of edit a real change introduces by accident.
swapped = re.sub(r"\.Last\(\)", ".First()", s, count=1)
if swapped == s:
    swapped = re.sub(r"items\[\^1\]", "items[0]", s, count=1)
if swapped == s:
    print("plant: could not find the cursor expression to corrupt — the template changed shape", file=sys.stderr)
    sys.exit(3)
open(p, "w").write(swapped)
PY
echo "plant: fault planted in $TARGET (original kept alongside as .original)"
