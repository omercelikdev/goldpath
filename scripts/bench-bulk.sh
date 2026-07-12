#!/usr/bin/env bash
# Bulk RFC §6 performance proofs: 10k-file intake (the finance card's number) and 100k-row
# execute throughput. Real PostgreSQL via Testcontainers; record the numbers in
# packages/Goldpath.Bulk/ops/bulk-benchmarks.md.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/.." && pwd)
if [ -d "$HOME/.dotnet/sdk" ]; then
  export DOTNET_ROOT="$HOME/.dotnet"
  export PATH="$HOME/.dotnet:$PATH"
fi
cd "$ROOT"
dotnet test tests/Goldpath.IntegrationTests --nologo --filter "FullyQualifiedName~BulkBenchTests" --logger "console;verbosity=detailed" \
  | grep -E "BENCH-BULK|Passed!|Failed"
