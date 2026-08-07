#!/usr/bin/env bash
# Builds the console's dist and lays it inside Goldpath.Console, which embeds it.
#
#   scripts/build-console.sh
#
# This is the ONLY place Node is needed. Adopters never run it: Goldpath's CI runs it
# before packing, and the package ships the result.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/.." && pwd)
DIST="$ROOT/ui/console/dist"
TARGET="$ROOT/packages/Goldpath.Console/wwwroot"

echo "── building the console"
# The kit arrives PREBUILT from npm (@qorpe/ui) since the extraction — one install.
cd "$ROOT/ui/console"
pnpm install --frozen-lockfile
pnpm exec vite build

[ -f "$DIST/index.html" ] || { echo "the build produced no index.html — refusing to lay an empty console"; exit 1; }

echo "── laying it into Goldpath.Console"
find "$TARGET" -mindepth 1 ! -name ".gitkeep" -delete
cp -R "$DIST"/. "$TARGET"/
# Vite hashes asset names, so a stale file left behind would be served forever by a route
# nothing links to — the delete above is what keeps the package honest.
echo "── console laid: $(find "$TARGET" -type f ! -name '.gitkeep' | wc -l | tr -d ' ') files"
