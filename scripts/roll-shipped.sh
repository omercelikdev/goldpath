#!/usr/bin/env bash
# The D4 release gate's first item (docs/rfc/goldpath-versioning.md): roll every
# package's PublicAPI.Unshipped.txt into PublicAPI.Shipped.txt — the Shipped ledger IS
# the released surface; an empty Unshipped after the roll means "nothing pending".
# Run from the repo root inside the release PR, then build: RS0016/RS0017 verify the roll.
#
# `*REMOVED*` lines are DELETIONS, not entries: they name a Shipped line that this train
# retires. Copying them across verbatim makes the analyzer refuse the whole ledger
# (RS0024: "the shipped API file can't have removed members") — found on the preview.5
# roll, the first train in which a shipped signature actually changed.
set -euo pipefail

rolled=0
removed=0
while IFS= read -r unshipped; do
    shipped="${unshipped%Unshipped.txt}Shipped.txt"
    [ -f "$shipped" ] || { echo "MISSING $shipped" >&2; exit 1; }

    # Deletions first: each *REMOVED* line must name an EXISTING Shipped line — a removal
    # that matches nothing is a typo about to un-ship the wrong thing silently.
    while IFS= read -r removal; do
        target="${removal#\*REMOVED\*}"
        if ! grep -qxF "$target" "$shipped"; then
            echo "REMOVAL NOT FOUND in $shipped: $target" >&2
            exit 1
        fi
        grep -vxF "$target" "$shipped" > "$shipped.tmp" && mv "$shipped.tmp" "$shipped"
        removed=$((removed + 1))
    done < <(grep '^\*REMOVED\*' "$unshipped" || true)

    # Then the additions; the #nullable header stays behind.
    entries=$(grep -v '^#nullable' "$unshipped" | grep -v '^\*REMOVED\*' | grep -v '^[[:space:]]*$' || true)
    if [ -n "$entries" ]; then
        printf '%s\n' "$entries" >> "$shipped"
        rolled=$((rolled + $(printf '%s\n' "$entries" | wc -l | tr -d ' ')))
    fi

    printf '#nullable enable\n' > "$unshipped"
done < <(find packages -name "PublicAPI.Unshipped.txt" -not -path "*/obj/*" -not -path "*/bin/*" | sort)

echo "rolled $rolled entries into the Shipped ledgers ($removed retired)"
