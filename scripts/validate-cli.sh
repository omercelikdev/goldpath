#!/usr/bin/env bash
# The CLI's OWN proof lane (preview.8 coverage audit, 2026-09-05).
#
# The golden-manifest lane proves the TEMPLATES: it calls dotnet new directly and the only
# verbs it runs are `db init` and a shape's post steps. So six verbs an adopter actually
# types had unit tests and nothing else: the wizard, `init`, `export compose`, `discover`,
# `db status` and `check` — the last one ran in the CorPay job against the PUBLISHED tool,
# never against the working tree. This lane runs every one of them against the tool built
# HERE, on a real generated app, and asserts what each must produce.
#
# No containers: nothing here needs a database to be up. Usage: validate-cli.sh
set -euo pipefail

ROOT=$(cd "$(dirname "$0")/.." && pwd)
if [ -d "$HOME/.dotnet/sdk" ]; then
  export DOTNET_ROOT="$HOME/.dotnet"
  export PATH="$HOME/.dotnet:$PATH"
fi

WORK=$(cd "${GOLDPATH_CLI_WORK:-"$(mktemp -d /tmp/goldpath-cli.XXXXXX)"}" && pwd -P)
FEED="$WORK/feed"
CLI="dotnet run --project $ROOT/tools/Goldpath.Cli --"
echo "── cli proofs · workdir: $WORK"

rm -rf "$HOME/.nuget/packages/goldpath."*
if [ ! -d "$FEED" ] || [ -z "$(ls -A "$FEED" 2>/dev/null)" ]; then
  if command -v pnpm >/dev/null 2>&1; then
    bash "$ROOT/scripts/build-console.sh" >/dev/null
  else
    echo "── pnpm not found; the packed console would ship without its assets. Install pnpm (10.x)."
    exit 1
  fi
  echo "── pack repo packages -> local feed"
  dotnet pack "$ROOT/Goldpath.sln" -c Release -o "$FEED" --nologo -v q
fi

dotnet new uninstall "$ROOT/templates/goldpath-solution" >/dev/null 2>&1 || true
dotnet new install "$ROOT/templates/goldpath-solution" --force >/dev/null

SPECDRIFT_VERSION=0.4.2
[ -x "$WORK/tools/specdrift" ] || dotnet tool install --tool-path "$WORK/tools" specdrift --version "$SPECDRIFT_VERSION" >/dev/null
export GOLDPATH_SPECDRIFT="$WORK/tools/specdrift"
export PATH="$WORK/tools:$PATH"

wire_feed() {
  python3 - "$1/nuget.config" "$FEED" <<'PY'
import sys
path, feed = sys.argv[1], sys.argv[2]
s = open(path).read().replace("<!-- GOLDPATH_FEED -->", f'<add key="goldpath-local" value="{feed}" />')
open(path, "w").write(s)
PY
}

# ── 1. the WIZARD, answered from a script (the interactive shell, not just Derive) ─────
# Answered BY NAME (the prompter accepts a label as well as its number, so the script does
# not silently shift when a menu gains an entry): name, kind, database, auth, layout,
# modules, outbox?, generate?
APP="$WORK/WizardApp"
# A re-run in a kept workdir (GOLDPATH_CLI_WORK) must start from nothing: dotnet new
# refuses to overwrite, and the failure would read as a wizard bug.
rm -rf "$APP" "$WORK/Brownfield"
echo "── goldpath new (wizard, piped answers)"
WIZARD_OUT="$WORK/wizard.txt"
(cd "$WORK" && printf 'WizardApp\nsolution\npostgresql\nnone\nvertical-slice\naudittrail\nn\ny\n' | $CLI new > "$WIZARD_OUT" 2>&1) || { cat "$WIZARD_OUT"; echo "── THE WIZARD FAILED"; exit 1; }
grep -q "equivalent command: goldpath new solution -n WizardApp" "$WIZARD_OUT" || { cat "$WIZARD_OUT"; echo "── the wizard printed no equivalent command"; exit 1; }
grep -q -- "--features audittrail" "$WIZARD_OUT" || { cat "$WIZARD_OUT"; echo "── the wizard dropped the chosen module"; exit 1; }
grep -q -- "--features outbox" "$WIZARD_OUT" && { cat "$WIZARD_OUT"; echo "── the wizard emitted a --features value the template cannot take"; exit 1; }
test -f "$APP/.goldpath/manifest.yaml" || { cat "$WIZARD_OUT"; echo "── the wizard generated no app"; exit 1; }
echo "   wizard app generated and its equivalent command is honest"

wire_feed "$APP"
(cd "$APP" && $CLI db init --path .)

# ── 2. db status — the verb no lane ran on its own ─────────────────────────────────────
echo "── goldpath db status"
STATUS_OUT="$WORK/status.txt"
(cd "$APP" && $CLI db status --path . > "$STATUS_OUT" 2>&1) || { cat "$STATUS_OUT"; exit 1; }
grep -q "every owner's migrations match its model" "$STATUS_OUT" \
  || { cat "$STATUS_OUT"; echo "── db status did not report a clean model/migration match on a freshly initialised app"; exit 1; }

# ── 3. export compose — and the file it writes must be VALID compose ───────────────────
echo "── goldpath export compose"
(cd "$APP" && $CLI export compose --path .)
test -s "$APP/docker-compose.yml" || { echo "── export compose wrote no docker-compose.yml"; exit 1; }
if command -v docker >/dev/null 2>&1 && docker compose version >/dev/null 2>&1; then
  (cd "$APP" && docker compose -f docker-compose.yml config -q) || { echo "── the exported compose file is not valid compose"; exit 1; }
  echo "   docker compose config accepted the exported file"
else
  python3 -c "
import sys, re
text = open('$APP/docker-compose.yml').read()
assert 'services:' in text, 'no services block'
assert re.search(r'^\s+\w[\w-]*:', text, re.M), 'no service entries'
print('   docker not available — structural check only')
"
fi

# ── 4. discover — the inventory verb ───────────────────────────────────────────────────
echo "── goldpath discover"
DISCOVER_OUT="$WORK/discover.txt"
(cd "$WORK" && $CLI discover --path . > "$DISCOVER_OUT" 2>&1) || { cat "$DISCOVER_OUT"; exit 1; }
grep -q "WizardApp" "$DISCOVER_OUT" || { cat "$DISCOVER_OUT"; echo "── discover did not find the generated app"; exit 1; }

# ── 5. init — the brownfield attach, on a directory that is NOT a Goldpath app ─────────
echo "── goldpath init (brownfield attach)"
BROWN="$WORK/Brownfield"
mkdir -p "$BROWN"
# init ATTACHES to an existing solution — a bare directory is refused by design, so the
# fixture is a plain .NET project, the brownfield an adopter actually has.
(cd "$BROWN" && dotnet new console -n Legacy -o . >/dev/null && dotnet new sln -n Legacy >/dev/null && dotnet sln add Legacy.csproj >/dev/null)
(cd "$BROWN" && printf 'Legacy\nteam-legacy\nA legacy service adopting Goldpath\n' | $CLI init --path . > "$WORK/init.txt" 2>&1) || { cat "$WORK/init.txt"; exit 1; }
test -f "$BROWN/.goldpath/manifest.yaml" || { cat "$WORK/init.txt"; echo "── init wrote no manifest"; exit 1; }
"$GOLDPATH_SPECDRIFT" validate "$BROWN/.goldpath/manifest.yaml" --schema "$ROOT/schemas/manifest/v1/goldpath-manifest.schema.json" \
  || { echo "── init wrote a manifest the ENGINE rejects"; exit 1; }

# ── 6. check — validate + drift + db status + build, against THIS tree's tool ──────────
# The CorPay job runs `check` against the PUBLISHED tool, so a regression in CheckCommand
# on a branch could not be caught by CI at all until this lane existed.
echo "── goldpath check"
dotnet build "$APP" --nologo -v q -m:1
mkdir -p "$APP/specs"
cp "$APP/src/WizardApp.Api/openapi/WizardApp.Api.json" "$APP/specs/WizardApp.Api.json"
CHECK_OUT="$WORK/check.txt"
(cd "$APP" && $CLI check --path . > "$CHECK_OUT" 2>&1) || { cat "$CHECK_OUT"; echo "── goldpath check went red on a freshly generated app"; exit 1; }
for step in "specdrift validate" "specdrift drift" "db status" "dotnet build" "GREEN"; do
  grep -q "$step" "$CHECK_OUT" || { cat "$CHECK_OUT"; echo "── goldpath check skipped its '$step' step"; exit 1; }
done

echo "── CLI PROOFS GREEN"
