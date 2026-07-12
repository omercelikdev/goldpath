#!/usr/bin/env bash
# Archival RFC §6 performance proofs: recall @1M entries, graph round-trip, 100k row purge.
# Real PostgreSQL via Testcontainers; record the numbers in ops/archival-benchmarks.md.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/.." && pwd)
if [ -d "$HOME/.dotnet/sdk" ]; then
  export DOTNET_ROOT="$HOME/.dotnet"
  export PATH="$HOME/.dotnet:$PATH"
fi
cd "$ROOT"
dotnet test tests/Goldpath.IntegrationTests --nologo --filter "FullyQualifiedName~ArchivalBenchTests" --logger "console;verbosity=detailed" \
  | grep -E "BENCH-ARCHIVAL|Passed!|Failed"
