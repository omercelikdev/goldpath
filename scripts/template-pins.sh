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

# The DOCS are a pin too: an adopter installs the version the quickstart tells them to,
# and a stale one hands them an old train on their very first command. Found on
# 2026-07-28, when README and the getting-started guide still said preview.2.
for doc in "$ROOT"/README.md "$ROOT"/docs/guide/getting-started.md; do
  while IFS= read -r pinned; do
    if [ "$pinned" != "$train" ]; then
      echo "── STALE $(basename "$doc"): Goldpath.Templates@$pinned, train is $train"
      bad=1
    fi
  done < <(grep -oE 'Goldpath\.Templates@[0-9][^ ]*' "$doc" | sed 's/.*@//' || true)
done

[ "$bad" = "0" ] && echo "── pins: templates and the adopter docs are all on the train" || exit 1
