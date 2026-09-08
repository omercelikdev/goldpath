---
name: goldpath-change
description: Run a change to the ACCELERATOR ITSELF through the delivery cycle — a package, an analyzer, a template, the CLI, a gate. Use when working inside the goldpath repository on an issue, a defect or a feature. For changes to an app BUILT with Goldpath, the generated repo carries its own skills.
---

# goldpath-change — the maintainer's path through the cycle

You are changing the accelerator, not an application built with it. The difference matters: a
mistake here is inherited by every generated app, and it is inherited silently, because the
adopter never reads this repository.

This skill enforces the SEQUENCE in `.claude/cycle.md`. It carries no rules of its own — the
rules live in `docs/adr/` (the constitution), `CLAUDE.md` (the invariants in summary) and the
ledgers. If a rule seems missing, that is a finding to report, not a gap to fill from taste.

## Before anything else

Read, in this order. They are short and they answer most questions faster than the code will:

1. `CLAUDE.md` — the invariants and the current status
2. `docs/adr/` — the constitution. Nothing may contradict an ADR; a conflict is a conversation
3. `docs/strategy/open-threads.md` — whether the thing you are about to build is already
   deferred, and what its trigger was
4. `.claude/cycle.md` — the nine steps

## What is different here, and why

**Your spec is the public surface.** An application's contract is its OpenAPI document; this
repository's contract is `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt` and the analyzer
release files. They fail the build when they drift (RS0016, RS2000/RS2007). `cycle.md` §6 says
consult the engine before declaring done — here, that is what the engine is.

**Templates ship on a train.** A template may only consume PUBLISHED API. New surface reaches
the templates at the next train boundary, not when it lands — `docs/ops/release-checklist.md`
carries the adoptions waiting for that boundary, and `scripts/template-pins.sh` enforces it.

**A change to the agent layer is copied.** `.claude/` exists in the two templates and in
CorPay. Change one, change all — `scripts/skills-parity.sh` will tell you, and it is cheaper
to hear it now than in CI.

**The ledgers are part of the change, not paperwork after it.** A deferral without a row in
open-threads carrying its trigger and its proof did not happen. `scripts/ledger-check.sh`.

## The gates

Everything in `.claude/cycle.md`, plus these before you offer the change:

```
./scripts/docs-freshness.sh      # the docs still describe the repository
./scripts/ledger-check.sh        # the ledgers are true
./scripts/schema-honesty.sh      # roadmap-only values say so
./scripts/skills-parity.sh       # the agent layer has not drifted between its copies
./scripts/template-pins.sh       # templates generate on the PUBLISHED train
dotnet build && dotnet test      # the projects your change touches, then the affected suites
```

A package whose engine paths changed needs its mutation gate re-run and its score recorded in
`stryker/README.md` — four packages clear the break by under three points, so "the tests still
pass" is not the same claim as "the tests would still notice".

## Never

- Contradict an ADR to make a change fit. Supersede it deliberately, or stop.
- Let a template consume API that is not on the published train.
- Leave a ledger stale in the same pull request that made it stale.
- Close a defect you could not reproduce.
