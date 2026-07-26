#!/usr/bin/env bash
# The U2 exit gate (console RFC D3): the REAL console driven against a REAL Goldpath app.
# Brings up Postgres, a web host composed from the packages (jobs + the frozen admin
# surface), and the console dev server; runs Playwright; tears everything down.
#
#   scripts/console-smoke.sh
#
# Needs: docker, dotnet, pnpm. Exits non-zero on the first failure — no green-by-cleanup.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/.." && pwd)
if [ -d "$HOME/.dotnet/sdk" ]; then export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"; fi

PG_NAME="goldpath-console-smoke-pg"
SERVICE_URL="http://localhost:5310"
CONSOLE_URL="http://localhost:5201"
HOST_PID=""
VITE_PID=""

cleanup() {
  [ -n "$HOST_PID" ] && kill "$HOST_PID" 2>/dev/null || true
  [ -n "$VITE_PID" ] && kill "$VITE_PID" 2>/dev/null || true
  docker rm -f "$PG_NAME" >/dev/null 2>&1 || true
}
trap cleanup EXIT

echo "── postgres"
docker rm -f "$PG_NAME" >/dev/null 2>&1 || true
docker run -d --name "$PG_NAME" -e POSTGRES_PASSWORD=smoke -e POSTGRES_DB=smoke -p 55432:5432 postgres:17-alpine >/dev/null
CONNECTION="Host=localhost;Port=55432;Database=smoke;Username=postgres;Password=smoke"
until docker exec "$PG_NAME" pg_isready -U postgres >/dev/null 2>&1; do sleep 1; done

echo "── the app (real packages, real Quartz, the FROZEN admin surface)"
GOLDPATH_CONSOLE_ORIGIN="$CONSOLE_URL" \
  dotnet run --project "$ROOT/tests/Goldpath.Jobs.TestHost" -- "$CONNECTION" --console "$SERVICE_URL" > /tmp/console-smoke-host.log 2>&1 &
HOST_PID=$!
for _ in $(seq 1 60); do
  grep -q "CONSOLEHOST-READY" /tmp/console-smoke-host.log && break
  sleep 1
done
grep -q "CONSOLEHOST-READY" /tmp/console-smoke-host.log || { echo "the app never came up:"; tail -30 /tmp/console-smoke-host.log; exit 1; }

echo "── the console"
(cd "$ROOT/ui/console" && pnpm dev --port 5201 --strictPort > /tmp/console-smoke-vite.log 2>&1) &
VITE_PID=$!
for _ in $(seq 1 60); do
  curl -sf "$CONSOLE_URL" >/dev/null 2>&1 && break
  sleep 1
done
curl -sf "$CONSOLE_URL" >/dev/null || { echo "the console never came up:"; tail -20 /tmp/console-smoke-vite.log; exit 1; }

echo "── playwright"
cd "$ROOT/ui/console"
GOLDPATH_CONSOLE_URL="$CONSOLE_URL" GOLDPATH_SERVICE_URL="$SERVICE_URL" pnpm exec playwright test
