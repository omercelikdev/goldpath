#!/usr/bin/env bash
# Golden-manifest shape validation (ADR-0008 locally): pack → install template from source →
# generate the given shape → build against the local feed → run the smoke suite (real containers).
# Usage: validate-gm.sh <ShapeName> [extra dotnet-new args, e.g. --db sqlserver --broker none]
set -euo pipefail

NAME=${1:?usage: validate-gm.sh <ShapeName> [dotnet-new args...]}
shift || true
# Which template the shape exercises (goldpath-solution | goldpath-worker).
TEMPLATE=${GOLDPATH_GM_TEMPLATE:-goldpath-solution}

ROOT=$(cd "$(dirname "$0")/.." && pwd)
# Local macOS: prefer the user SDK install; CI images already have dotnet on PATH.
if [ -d "$HOME/.dotnet/sdk" ]; then
  export DOTNET_ROOT="$HOME/.dotnet"
  export PATH="$HOME/.dotnet:$PATH"
fi

# pwd -P: canonicalize (macOS /tmp is a symlink to /private/tmp — two spellings of one
# path make NuGet restore collide on its own generated files).
WORK=$(cd "${GOLDPATH_GM_WORK:-"$(mktemp -d /tmp/goldpath-gm.XXXXXX)"}" && pwd -P)
FEED="$WORK/feed"
APP="$WORK/$NAME"
echo "── shape: $NAME ($*) · workdir: $WORK"

# Same-version repacks poison the global cache (0.1.0-preview.1 never bumps between local
# runs): purge cached Goldpath packages so the generated app restores from THIS run's feed.
rm -rf "$HOME/.nuget/packages/goldpath."*

if [ ! -d "$FEED" ] || [ -z "$(ls -A "$FEED" 2>/dev/null)" ]; then
  echo "── pack repo packages -> local feed"
  dotnet pack "$ROOT/Goldpath.sln" -c Release -o "$FEED" --nologo -v q
fi

dotnet new uninstall "$ROOT/templates/$TEMPLATE" >/dev/null 2>&1 || true
dotnet new install "$ROOT/templates/$TEMPLATE" --force >/dev/null

echo "── generate ($TEMPLATE)"
rm -rf "$APP"
dotnet new "$TEMPLATE" -n "$NAME" -o "$APP" "$@" >/dev/null
python3 - "$APP/nuget.config" "$FEED" <<'PY'
import sys
path, feed = sys.argv[1], sys.argv[2]
s = open(path).read().replace("<!-- GOLDPATH_FEED -->", f'<add key="goldpath-local" value="{feed}" />')
open(path, "w").write(s)
PY

echo "── initial migration (goldpath db init — Development migrates, EnsureCreated is gone)"
(cd "$APP" && dotnet run --project "$ROOT/tools/Goldpath.Cli" -- db init --path .)

echo "── build"
# -m:1: the generated app has multiple heads referencing the Api project; .NET 10's
# StaticWebAssets cache (rjsmrazor.dswa.cache.json) races under parallel node builds.
dotnet build "$APP" --nologo -v q -m:1
# Worker shapes have no API head: probes, not business contracts — no OpenAPI artifact.
if [ -d "$APP/src/$NAME.Api" ]; then
  test -s "$APP/src/$NAME.Api/openapi/$NAME.Api.json"   && echo "── openapi artifact OK (Spec Engine drift input)"   || { echo "── OPENAPI ARTIFACT MISSING"; exit 1; }
fi

echo "── spec-lint (specdrift: validate + drift)"
# Pinned engine: local checkout when present (dev loop), otherwise a shallow clone of the tag.
SPECDRIFT_REF=v0.4.0
if [ -d "$HOME/Repositories/specdrift" ]; then
  SPECDRIFT_SRC="$HOME/Repositories/specdrift"
else
  SPECDRIFT_SRC="$WORK/specdrift"
  [ -d "$SPECDRIFT_SRC" ] || git clone --quiet --depth 1 --branch "$SPECDRIFT_REF" https://github.com/omercelikdev/specdrift "$SPECDRIFT_SRC"
fi
SPECDRIFT="dotnet run --project $SPECDRIFT_SRC/src/Specdrift --"
$SPECDRIFT validate "$APP/.goldpath/manifest.yaml"   --schema "$ROOT/schemas/manifest/v1/goldpath-manifest.schema.json"   --rules "$APP/.specdrift/rules.yaml"
# First generation: commit the contract (what a team does on day one), then drift must be clean.
if [ -d "$APP/src/$NAME.Api" ]; then
  mkdir -p "$APP/specs"
  cp "$APP/src/$NAME.Api/openapi/$NAME.Api.json" "$APP/specs/$NAME.Api.json"
fi
$SPECDRIFT drift --repo "$APP"

echo "── smoke (real AppHost + containers)"
dotnet test "$APP/tests/$NAME.SmokeTests" --no-build --nologo

echo "── $NAME GREEN"
