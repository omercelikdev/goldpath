#!/usr/bin/env bash
# The D4 release gate's first item (docs/rfc/goldpath-versioning.md): roll every
# package's PublicAPI.Unshipped.txt into PublicAPI.Shipped.txt — the Shipped ledger IS
# the released surface; an empty Unshipped after the roll means "nothing pending".
# Run from the repo root inside the release PR, then build: RS0016/RS0017 verify the roll.
set -euo pipefail

rolled=0
while IFS= read -r unshipped; do
    shipped="${unshipped%Unshipped.txt}Shipped.txt"
    [ -f "$shipped" ] || { echo "MISSING $shipped" >&2; exit 1; }

    # Everything except the #nullable header moves over; the header stays behind.
    entries=$(grep -v '^#nullable' "$unshipped" | grep -v '^[[:space:]]*$' || true)
    if [ -n "$entries" ]; then
        printf '%s\n' "$entries" >> "$shipped"
        rolled=$((rolled + $(printf '%s\n' "$entries" | wc -l | tr -d ' ')))
    fi

    printf '#nullable enable\n' > "$unshipped"
done < <(find packages -name "PublicAPI.Unshipped.txt" -not -path "*/obj/*" -not -path "*/bin/*" | sort)

echo "rolled $rolled entries into the Shipped ledgers"
