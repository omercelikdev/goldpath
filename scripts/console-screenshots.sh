#!/usr/bin/env bash
# Captures the README's console pictures from a REAL console driving REAL modules.
#
#   scripts/console-screenshots.sh
#
# Never a mockup: the stack below is the same one the console smoke drives, given enough
# to show — a batch at the four-eyes gate, a run with a repair item, a failed notification.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/.." && pwd)
if [ -d "$HOME/.dotnet/sdk" ]; then export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"; fi

PG=goldpath-shot-pg
MQ=goldpath-shot-mq
URL=http://localhost:5330
HOST_PID=""

cleanup() {
  [ -n "$HOST_PID" ] && kill "$HOST_PID" 2>/dev/null || true
  docker rm -f "$PG" "$MQ" >/dev/null 2>&1 || true
}
trap cleanup EXIT

echo "── the console dist"
bash "$ROOT/scripts/build-console.sh" >/dev/null

echo "── containers"
docker rm -f "$PG" "$MQ" >/dev/null 2>&1 || true
docker run -d --name "$PG" -e POSTGRES_PASSWORD=smoke -e POSTGRES_DB=smoke -p 55450:5432 postgres:17-alpine >/dev/null
docker run -d --name "$MQ" -p 55680:5672 rabbitmq:4-alpine sh -c \
  'echo shot > /var/lib/rabbitmq/.erlang.cookie && chmod 400 /var/lib/rabbitmq/.erlang.cookie \
   && chown rabbitmq:rabbitmq /var/lib/rabbitmq/.erlang.cookie && exec docker-entrypoint.sh rabbitmq-server' >/dev/null
for _ in $(seq 1 120); do docker exec "$PG" pg_isready -U postgres >/dev/null 2>&1 && break; sleep 1; done
for _ in $(seq 1 90); do docker exec "$MQ" rabbitmq-diagnostics -q check_port_connectivity >/dev/null 2>&1 && break; sleep 2; done

echo "── the app (serving its own console)"
dotnet run --project "$ROOT/tests/Goldpath.Jobs.TestHost" -- \
  "Host=localhost;Port=55450;Database=smoke;Username=postgres;Password=smoke" \
  --console "$URL" --broker "amqp://guest:guest@localhost:55680" --fleet payments-eod > /tmp/goldpath-shot-host.log 2>&1 &
HOST_PID=$!
for _ in $(seq 1 120); do grep -q CONSOLEHOST-READY /tmp/goldpath-shot-host.log && break; sleep 1; done
grep -q CONSOLEHOST-READY /tmp/goldpath-shot-host.log || { echo "the app never came up:"; tail -20 /tmp/goldpath-shot-host.log; exit 1; }

echo "── something to show (a gate, a run, a campaign)"
printf 'EndToEndId,Amount\nE2E-1,10.00\nE2E-2,20.00\nE2E-3,30.00,stray\n' > /tmp/goldpath-shot.csv
curl -sf -X POST "$URL/goldpath/admin/bulk/batches/payments?fileName=june-payouts.csv" \
  -H "content-type: application/octet-stream" --data-binary @/tmp/goldpath-shot.csv >/dev/null
curl -sf -X POST "$URL/goldpath/admin/campaign/" -H "content-type: application/json" \
  -d '{"type":"welcome","name":"june-welcome","policy":{"tps":2,"maxInFlight":5}}' >/dev/null
curl -sf -X POST "$URL/goldpath/admin/jobs/fleets/payments-eod/jobs/SmokeJob/trigger" >/dev/null
sleep 25   # the validate job and the run are REAL: give them their own cron a turn

echo "── capture"
cd "$ROOT/ui/console"
GOLDPATH_SHOT_DIR="$ROOT/docs/assets" GOLDPATH_SHOT_SERVICE_URL="$URL" \
  pnpm exec playwright test e2e/screenshots.spec.ts
echo "── written: $(ls "$ROOT/docs/assets"/console-*.png | wc -l | tr -d ' ') pictures"
