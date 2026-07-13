# Goldpath versioning & support contract (H7)

Status: **ACCEPTED** (2026-07-13, D1–D4 approved). This is the written promise the
PublicAPI ledgers enforce mechanically — what an adopter may rely on before taking a
Goldpath upgrade. Changes to this contract are themselves breaking and follow it.

## D1 — One version train (lockstep)

Every `Goldpath.*` package ships the SAME version from the monorepo: "Goldpath 0.3"
names the whole asset, never a compatibility matrix. The packages are cross-dependent
by design (`Goldpath.Abstractions` under everything, modules composing the jobs run
model), so independent versions would only manufacture matrix bugs. The CLI
(`goldpath`) and the template pack ride the same train.

## D2 — SemVer, with the pre-1.0 rules spelled out

While `0.x`:

- **`0.x.y` (patch): always safe.** No public-API removals/renames (RS0017 gates it),
  no schema changes, no admin-route changes, no behavior change an adopter could have
  depended on. Take it blind.
- **`0.(x+1)` (minor): MAY break — but never silently.** Every break ships with a
  step-by-step entry in that release's upgrade guide, and the PublicAPI ledger diff is
  the mechanical proof of exactly what changed.

From `1.0`: standard SemVer — breaking changes only in a major; minors add, patches fix.

What counts as **breaking** (any of):

- A PublicAPI ledger removal or signature change (the `PublicAPI.Shipped.txt` diff).
- A change to the FROZEN admin surface (`docs/rfc/goldpath-admin-contract.md`) —
  routes, verbs, envelopes, paging semantics.
- A schema change to Goldpath-owned tables — these follow the `[schema]` discipline in
  the migrations runbook and always carry a `goldpath db add` step in the upgrade guide.
- A default flipping (auth floor, sampler profile, clamp bounds) or a diagnostic
  contract change (span names/tags, meter names, analyzer rule semantics GP####).

Analyzer additions and new opt-in capabilities are NOT breaking (new rules ship as
warnings first; a later minor may raise severity, and says so in the guide).

## D3 — Support window (honest for a solo-maintained OSS asset)

- **Pre-1.0:** the latest release only. Older 0.x releases get no backports; the
  upgrade guides are the path forward.
- **Post-1.0:** the latest minor of the current major gets fixes; the previous major
  gets **security fixes only, for 6 months** after the new major ships. Nothing wider
  is promised, so the promise can always be kept.

## D4 — The release gate (mechanical trio)

No release tag without all three, in the same release PR:

1. **PublicAPI roll:** every package's `Unshipped.txt` rolls into `Shipped.txt` (the
   ledger IS the released surface; the roll script lands with the NuGet release work).
2. **CHANGELOG entry** for the train (one file, newest on top).
3. **Upgrade guide:** `docs/upgrades/<version>.md` — step-by-step for every break;
   a single line ("No breaking changes — take it blind.") when there are none.

The release checklist lives at `docs/ops/release-checklist.md`.

## Non-goals

- No LTS designations before the asset has production adopters asking for one.
- No compatibility shims/facades for renamed APIs pre-1.0 — the guide documents the
  rename; the ledger diff proves it. Shims start earning their keep at 1.0.
