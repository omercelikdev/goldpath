#!/usr/bin/env bash
# Campaign RFC §6 performance proofs: 1M enumeration + pacer precision (200 TPS, live
# throttle to 50) + 100k outcome sink. Real PostgreSQL via Testcontainers; record the
# numbers in packages/Goldpath.Campaign/ops/campaign-benchmarks.md.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/.." && pwd)
if [ -d "$HOME/.dotnet/sdk" ]; then
  export DOTNET_ROOT="$HOME/.dotnet"
  export PATH="$HOME/.dotnet:$PATH"
fi
cd "$ROOT"
dotnet test tests/Goldpath.IntegrationTests --nologo --filter "FullyQualifiedName~CampaignBenchTests" --logger "console;verbosity=detailed" \
  | grep -E "BENCH-CAMPAIGN|Passed!|Failed"
