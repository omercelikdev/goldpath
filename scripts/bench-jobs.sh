#!/usr/bin/env bash
# Jobs RFC §6 performance proofs (module excellence bar): runs the Bench-trait tests and
# prints the numbers to record in ops/jobs-benchmarks.md. Real PostgreSQL via Testcontainers.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/.." && pwd)
if [ -d "$HOME/.dotnet/sdk" ]; then
  export DOTNET_ROOT="$HOME/.dotnet"
  export PATH="$HOME/.dotnet:$PATH"
fi
cd "$ROOT"
dotnet test tests/Goldpath.IntegrationTests --nologo --filter "Category=Bench" --logger "console;verbosity=detailed" \
  | grep -E "BENCH|Passed!|Failed"
