#!/usr/bin/env bash
# The three remaining D7 proofs of the migrations RFC (goldpath-migrations.md) — H1 closes
# on THIS script being green, not on the tooling existing:
#   1. LIVE feature enablement: a deployed app WITH DATA gains a feature; old rows intact,
#      new tables live, and the app comes up in PRODUCTION mode against the bundled schema
#      (bundle first, app second — the app process never held DDL).
#   2. Upgrade rehearsal: a schema-affecting model change lands on a POPULATED database
#      through a bundle; zero data loss. (Package upgrades ride the exact same mechanics —
#      RFC D1: a package contributes model, the app owns the migration.)
#   3. Multi-head isolation: a jobs worker's migrations carry ONLY its private tables —
#      the shared fleet tables never get second DDL (RFC D3, proven against real SQL).
# Real PostgreSQL in a disposable docker container; everything from the LOCAL feed.
set -euo pipefail

ROOT=$(cd "$(dirname "$0")/.." && pwd)
if [ -d "$HOME/.dotnet/sdk" ]; then
  export DOTNET_ROOT="$HOME/.dotnet"
  export PATH="$HOME/.dotnet:$PATH"
fi
cd "$ROOT"

# The CLI's engine round-trip needs specdrift: PATH first, the sibling checkout as a
# dev-loop fallback (same convention the CLI's GOLDPATH_SPECDRIFT override serves).
if [ -z "${GOLDPATH_SPECDRIFT:-}" ] && ! command -v specdrift >/dev/null; then
  if [ -d "$HOME/Repositories/specdrift/src/Specdrift" ]; then
    export GOLDPATH_SPECDRIFT="dotnet run --project $HOME/Repositories/specdrift/src/Specdrift --"
  else
    echo "validate-migrations: specdrift not found — install it (dotnet tool install -g specdrift) or set GOLDPATH_SPECDRIFT." >&2
    exit 1
  fi
fi

WORK=$(cd "${GOLDPATH_MIG_WORK:-"$(mktemp -d /tmp/goldpath-mig.XXXXXX)"}" && pwd -P)
FEED="$WORK/feed"
APP="$WORK/MigShop"
PG_NAME="goldpath-mig-pg-$$"
PG_PORT=${GOLDPATH_MIG_PG_PORT:-15432}
CONN="Host=localhost;Port=$PG_PORT;Database=ordersdb;Username=postgres;Password=proof"

cleanup() {
  docker rm -f "$PG_NAME" >/dev/null 2>&1 || true
}
trap cleanup EXIT

fail() { echo "── PROOF FAILED: $1" >&2; exit 1; }

psql_q() { docker exec "$PG_NAME" psql -U postgres -d ordersdb -tAc "$1"; }

echo "── shape: MigShop · workdir: $WORK"
rm -rf "$HOME/.nuget/packages/goldpath."*
if [ ! -d "$FEED" ] || [ -z "$(ls -A "$FEED" 2>/dev/null)" ]; then
  echo "── pack repo packages -> local feed"
  dotnet pack "$ROOT/Goldpath.sln" -c Release -o "$FEED" --nologo -v q
fi

dotnet new uninstall "$ROOT/templates/goldpath-solution" >/dev/null 2>&1 || true
dotnet new install "$ROOT/templates/goldpath-solution" --force >/dev/null

echo "── generate v1 (no broker — the lean starting shape)"
rm -rf "$APP"
dotnet new goldpath-solution -n MigShop -o "$APP" --broker none --auth none >/dev/null
python3 - "$APP/nuget.config" "$FEED" <<'PY'
import sys
path, feed = sys.argv[1], sys.argv[2]
s = open(path).read().replace("<!-- GOLDPATH_FEED -->", f'<add key="goldpath-local" value="{feed}" />')
open(path, "w").write(s)
PY

echo "── goldpath db init (the Initial migration)"
(cd "$APP" && dotnet run --project "$ROOT/tools/Goldpath.Cli" -- db init --path . | tail -2)

echo "── postgres up (disposable)"
docker rm -f "$PG_NAME" >/dev/null 2>&1 || true
docker run -d --name "$PG_NAME" -e POSTGRES_PASSWORD=proof -e POSTGRES_DB=ordersdb -p "$PG_PORT:5432" postgres:17-alpine >/dev/null
until docker exec "$PG_NAME" pg_isready -U postgres >/dev/null 2>&1; do sleep 1; done

echo "── v1 bundle -> apply (bundle first...)"
(cd "$APP" && dotnet run --project "$ROOT/tools/Goldpath.Cli" -- db bundle --path . | tail -1)
"$APP/artifacts/migrations/MigShop.Api-migrations" --connection "$CONN" >/dev/null

echo "── seed LIVE data"
psql_q "INSERT INTO \"Orders\" (\"Reference\", \"Status\", \"Amount\", \"CreatedAt\") VALUES ('ORD-1','Confirmed',100.0,now()),('ORD-2','Pending',250.5,now());" >/dev/null
[ "$(psql_q 'SELECT count(*) FROM "Orders";')" = "2" ] || fail "seed did not land"

echo "── (...app second) PRODUCTION smoke against the bundled schema"
ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS="http://127.0.0.1:18080" \
  ConnectionStrings__ordersdb="$CONN" \
  dotnet run --project "$APP/src/MigShop.Api" --no-launch-profile >/dev/null 2>&1 &
API_PID=$!
HEALTH=""
for _ in $(seq 1 60); do
  HEALTH=$(curl -s -o /dev/null -w "%{http_code}" http://127.0.0.1:18080/health/ready || true)
  [ "$HEALTH" = "200" ] && break
  sleep 1
done
kill "$API_PID" >/dev/null 2>&1 || true
[ "$HEALTH" = "200" ] || fail "production app did not come healthy on the bundled schema (got '$HEALTH')"
echo "   /health/ready 200 — the app never held DDL and never needed to"

# The app's FIRST contract commit (what a real team does in the first PR; the generator
# leaving specs/ empty is filed as its own finding — the drift gate rightly demands it).
mkdir -p "$APP/specs"
cp "$APP/src/MigShop.Api/openapi/MigShop.Api.json" "$APP/specs/MigShop.Api.json"

echo "── PROOF 1: enable a feature on the LIVE system (add feature notification)"
(cd "$APP" && dotnet run --project "$ROOT/tools/Goldpath.Cli" -- add feature notification --path . | tail -3)
(cd "$APP" && dotnet run --project "$ROOT/tools/Goldpath.Cli" -- db add AddNotification --path . | tail -1)
(cd "$APP" && dotnet run --project "$ROOT/tools/Goldpath.Cli" -- db bundle --path . | tail -1)
"$APP/artifacts/migrations/MigShop.Api-migrations" --connection "$CONN" >/dev/null
[ "$(psql_q 'SELECT count(*) FROM "Orders";')" = "2" ] || fail "proof 1: existing data was lost"
[ "$(psql_q "SELECT count(*) FROM information_schema.tables WHERE table_name='GoldpathNotifications';")" = "1" ] || fail "proof 1: notification tables did not arrive"
[ "$(psql_q "SELECT count(*) FROM information_schema.tables WHERE table_name ILIKE 'qrtz%';")" -gt 0 ] || fail "proof 1: jobs (quartz) tables did not arrive with the rider"
echo "   PROOF 1 GREEN: 2 rows intact, notification + jobs tables live"

echo "── PROOF 2: upgrade rehearsal — a schema change lands on the POPULATED database"
python3 - "$APP/src/MigShop.Api/Orders/Order.cs" <<'PY'
import sys
path = sys.argv[1]
s = open(path).read()
assert "LoyaltyTier" not in s
s = s.replace("    public DateTimeOffset CreatedAt { get; set; }",
    "    public DateTimeOffset CreatedAt { get; set; }\n\n    /// <summary>Upgrade-rehearsal column (validate-migrations proof 2).</summary>\n    public string? LoyaltyTier { get; set; }")
open(path, "w").write(s)
PY
(cd "$APP" && dotnet run --project "$ROOT/tools/Goldpath.Cli" -- db add UpgradeRehearsal --path . | tail -1)
(cd "$APP" && dotnet run --project "$ROOT/tools/Goldpath.Cli" -- db bundle --path . | tail -1)
"$APP/artifacts/migrations/MigShop.Api-migrations" --connection "$CONN" >/dev/null
[ "$(psql_q 'SELECT count(*) FROM "Orders";')" = "2" ] || fail "proof 2: data lost during upgrade"
[ "$(psql_q 'SELECT "Amount" FROM "Orders" WHERE "Reference"='"'"'ORD-2'"'"';')" = "250.5000" ] || fail "proof 2: values corrupted"
[ "$(psql_q "SELECT count(*) FROM information_schema.columns WHERE table_name='Orders' AND column_name='LoyaltyTier';")" = "1" ] || fail "proof 2: the new column did not arrive"
echo "   PROOF 2 GREEN: populated table altered in place, zero loss"

# The feature changed the API surface — re-commit the contract (in real life: review
# the OpenAPI diff in the PR; the SPEC0212 gate exists precisely to force this look).
cp "$APP/src/MigShop.Api/openapi/MigShop.Api.json" "$APP/specs/MigShop.Api.json"

echo "── PROOF 3: multi-head isolation — the worker's migrations carry ONLY its own tables"
(cd "$APP" && dotnet run --project "$ROOT/tools/Goldpath.Cli" -- add worker eod-report --trigger jobs --path . | tail -2)
(cd "$APP" && dotnet run --project "$ROOT/tools/Goldpath.Cli" -- db init --path . | tail -2)
WORKER_PROJ=$(ls -d "$APP"/src/*Worker | head -1)
WORKER_SQL="$WORK/worker-migrations.sql"
(cd "$APP" && dotnet ef migrations script --project "$WORKER_PROJ" --startup-project "$WORKER_PROJ" -o "$WORKER_SQL" >/dev/null)
grep -qi "DailyReports" "$WORKER_SQL" || fail "proof 3: the worker's PRIVATE table is missing from its migrations"
if grep -qiE "CREATE TABLE.*(qrtz|GoldpathJobRun)" "$WORKER_SQL"; then
  fail "proof 3: the worker's migrations carry DDL for SHARED tables — D3 exclusion broken"
fi
echo "   PROOF 3 GREEN: worker DDL = private tables only (shared fleet tables excluded)"

echo "── MIGRATIONS PROOFS GREEN (D7 2-4) — H1's evidence is complete"
