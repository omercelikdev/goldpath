# Release checklist (the D4 gate)

One release PR per train version, containing ALL of the following — the tag happens
only after that PR merges green.

## Before the release PR

- [ ] Local full gates green: build, unit loop, `dotnet format --verify-no-changes`,
      license gate, integration suite.
- [ ] Golden-manifest matrix green (nightly or a fresh dispatch — ADR-0008: no release
      while GM is red).
- [ ] Mutation scores current for every package whose engine paths changed since the
      last release. Hosted-fit ten: nightly. The big six (Jobs, Archival, Bulk,
      Notification, Campaign, Caching): `mutation-heavy.yml` dispatch when time allows,
      otherwise the LOCAL run is authoritative (`scripts/mutation-gate.sh Goldpath.<Package>` —
      the FULL config name; the script now refuses a name that matches no config, because
      `mutation-gate.sh Jobs` once ran nothing and printed GREEN).
- [ ] Bench reference numbers re-measured (`bench.yml` dispatch) IF any engine path
      changed; ops docs updated from the run.

## In the release PR

- [ ] Version bump: the single train version (D1 — every `Goldpath.*` package, the CLI
      and the template pack) — INCLUDING the templates' own `Goldpath.*` pins, which
      decide which train a generated app restores. `scripts/template-pins.sh` gates this
      on every PR; it exists because the pins sat four trains behind while the golden
      manifest matrix quietly validated the old one.
- [ ] PublicAPI roll: each package's `PublicAPI.Unshipped.txt` content moves to
      `PublicAPI.Shipped.txt` (empty Unshipped after the roll).
- [ ] `CHANGELOG.md`: the train's entry, newest on top.
- [ ] `docs/upgrades/<version>.md`: a step for every breaking change (PublicAPI diff,
      admin-contract change, `[schema]` migration step, default flip) — or the single
      line "No breaking changes — take it blind."
- [ ] Admin contract check: if any route/envelope changed, `goldpath-admin-contract.md`
      was updated in the SAME PR that changed it (the route-freeze test forces this).

## After the merge

- [ ] Tag `v<version>` on the merge commit.
- [ ] NuGet push via `release.yml` (OIDC trusted publishing — tag-gated; shipped with issue #10).
- [ ] GitHub release notes = the CHANGELOG entry + a link to the upgrade guide.
