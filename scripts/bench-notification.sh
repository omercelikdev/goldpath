#!/usr/bin/env bash
# Notification RFC §6 performance proofs: render micro + the insurance night (10k request
# + send pass). Real PostgreSQL via Testcontainers; record the numbers in
# packages/Goldpath.Notification/ops/notification-benchmarks.md.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/.." && pwd)
if [ -d "$HOME/.dotnet/sdk" ]; then
  export DOTNET_ROOT="$HOME/.dotnet"
  export PATH="$HOME/.dotnet:$PATH"
fi
cd "$ROOT"
dotnet test tests/Goldpath.IntegrationTests --nologo --filter "FullyQualifiedName~NotificationBenchTests" --logger "console;verbosity=detailed" \
  | grep -E "BENCH-NOTIFICATION|Passed!|Failed"
