#!/usr/bin/env bash
# The train's Software Bill of Materials (CycloneDX), generated from the RELEASE build
# outputs — the resolved graph a consumer actually runs, not the versions we declare.
#
# Why it exists: when the next log4shell-class advisory lands, an adopter's security team
# must answer "are we affected?" from an artifact, in seconds. CRA Art. 14 makes that
# expectation law from 2026-09-11; enterprise procurement already asks for it.
#
# Scope notes (deliberate):
#   - Debug and obj/ are excluded: neither ships.
#   - BOTH target frameworks are included — the packages multi-target net8.0;net10.0 and
#     both land in the .nupkg, so both belong in the bill.
#   - The nupkg itself is NOT the input: a .nupkg carries the dll and the nuspec, not the
#     resolved dependency graph (verified — scanning one yields zero components).
set -euo pipefail
cd "$(dirname "$0")/.."

OUT=${1:-out/goldpath-sbom.cdx.json}
VERSION=${GOLDPATH_VERSION:-$(grep -m1 '^## \[' CHANGELOG.md | sed 's/^## \[\([^]]*\)\].*/\1/')}

command -v syft >/dev/null 2>&1 || {
  echo "sbom: syft not found — install it (brew install syft) or run this where the CI action provides it." >&2
  exit 1
}

mkdir -p "$(dirname "$OUT")"
syft dir:packages \
  --exclude './**/bin/Debug/**' \
  --exclude './**/obj/**' \
  --source-name "Goldpath" \
  --source-version "$VERSION" \
  -o cyclonedx-json="$OUT" \
  --quiet

# A bill nobody can read is not evidence. Report what it contains, and fail loudly if the
# scan produced nothing — an empty SBOM published as "our dependencies" is worse than none.
python3 - "$OUT" <<'PY'
import json, sys
path = sys.argv[1]
doc = json.load(open(path))
libraries = {(c["name"], c.get("version")) for c in doc.get("components", []) if c.get("type") == "library"}
ours = sorted(n for n, _ in libraries if n.startswith("Goldpath"))
if len(libraries) < 50:
    print(f"sbom: only {len(libraries)} libraries found — the scan almost certainly missed the build output.")
    sys.exit(1)
print(f"sbom: {path}")
print(f"      {len(libraries)} unique libraries, {len(ours)} of them ours ({doc.get('specVersion')} CycloneDX)")
PY
