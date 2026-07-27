#!/usr/bin/env bash
# The template must generate apps on THIS train (D1: the train version covers every
# Goldpath.* package, the CLI AND the template pack — its inner pins are part of that).
#
# Why this gate exists: nuget.org is a source in the generated app, so a stale pin does
# not fail — it silently restores an OLD published train, and the golden-manifest matrix
# then validates a version nobody is shipping. Found on 2026-07-27, when the templates
# were still pinned to preview.1 four trains later.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/.." && pwd)

train=$(python3 - "$ROOT/Directory.Build.props" <<'PY'
import re, sys
s = open(sys.argv[1]).read()
prefix = re.search(r"<VersionPrefix>([^<]+)</VersionPrefix>", s).group(1)
suffix = re.search(r"<VersionSuffix>([^<]*)</VersionSuffix>", s)
print(prefix + ("-" + suffix.group(1) if suffix and suffix.group(1) else ""))
PY
)
echo "── train: $train"

bad=0
for props in "$ROOT"/templates/*/Directory.Packages.props; do
  while IFS= read -r line; do
    version=$(printf '%s' "$line" | sed -n 's/.*Version="\([^"]*\)".*/\1/p')
    name=$(printf '%s' "$line" | sed -n 's/.*Include="\([^"]*\)".*/\1/p')
    if [ "$version" != "$train" ]; then
      echo "── STALE $(basename "$(dirname "$props")"): $name pinned at $version, train is $train"
      bad=1
    fi
  done < <(grep -E 'PackageVersion Include="Goldpath\.' "$props" || true)
done

[ "$bad" = "0" ] && echo "── template pins: every Goldpath.* package is on the train" || exit 1
