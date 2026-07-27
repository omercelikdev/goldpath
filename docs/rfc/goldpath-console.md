# RFC: Goldpath Console — the InfraOps surface (UI phase)

Status: **ACCEPTED** (2026-07-14) — D1 (React+Tailwind, dist-shipped; supersedes the
RCL wording), D2 (triage-first home) and D3 (component tests + the GmConsole nightly
shape) approved by the owner. U1 (kit) is live.
Visual contract: `docs/strategy/ui-standard-v1.md` (adopted). Locked antecedents:
ONE run console; the UI knows CAPABILITIES, not levels; products (Mockifyr, Praxis)
own their UI — the console links, never embeds; UI is written ONCE against the full,
sample-proven capability set.

## 1. What the console is

The shipped InfraOps surface of every Goldpath app: ALL module screens out of the box,
riding the FROZEN admin contract (`goldpath-admin-contract.md`) and nothing else — the
console is a client of the same API adopters script, which is why the contract froze
first. It is also the asset's extension point: adopters get a dashboard they can
custom-develop ON, with the same kit, the same way they add features to the backend.

## 2. Architecture — three layers, mirroring backend composition

1. **`ui/kit`** — the token system + primitives of ui-standard-v1 (npm package,
   versioned with the train). Everything an adopter's custom screen needs; nothing
   Goldpath-schema-specific.
2. **`Goldpath.Console`** — the screens, capability-driven:
   - **Run console** (the core): fleets → jobs → runs → chunk breakdown → repair queue;
     verbs trigger/pause/resume/reschedule/rerun/replay-items with confirm + audit hints.
   - **Module panels**, lit by capability discovery: bulk intake (upload/report/
     four-eyes gate), campaign governor (pacer + LIVE throttle), notification evidence
     views (masked), archival (holds/erasure/verify).
   - **Capability discovery**: the console probes each registered service's admin
     surfaces (the frozen route roots); a 404 root = capability absent = the panel does
     not exist. No manifest upload, no config drift — the API is the truth here too.
3. **The adopter's console app** — references `Goldpath.Console`, adds custom pages
   with the kit. Extension is composition: a route table contribution, not a fork.

## 3. N services, one console — the service registry

- **Within one app**: nothing to configure — n workers share the app database and the
  fleet registry is store-discovered (jobs D9); the API's admin surface already speaks
  for every fleet. CorPay proves it: api + payments + eod under one console today.
- **Across services**: a registry of entries `{ name, adminBaseUrl }` — config-file or
  Aspire service discovery. Each service contributes its capability panels under its
  name; cross-service home aggregates them.
- **Auth**: the console signs in ONCE against the adopter's IdP and carries the token
  to every service — all surfaces demand `goldpath-ops` (H2); the console is just a
  well-dressed client of that floor. The login-gate primitive exists in the kit.
- **External products**: Mockifyr (mocks) and other products appear as configured LINK
  tiles — their UI is theirs.

## 4. Decisions

- **D1 — Stack: React + Tailwind, dist-shipped (supersedes the earlier RCL wording).**
  One design system and one stack across the product family (Mockifyr/Praxis lineage);
  adopters NEVER run Node — CI builds the dist, `Goldpath.Console` ships it as embedded
  static assets served by `MapGoldpathConsole()` on the management head. The "no Node
  in generated apps" principle holds by construction. Custom pages: the kit npm package
  for teams that build UI, and a config-driven link/iframe-free tile row for those that
  do not.
- **D2 — The operator's first screen: cross-service TRIAGE ("today").**
  Red/overrun-predicted runs, repair-queue depths, gates awaiting four-eyes, DLQ depth —
  each row deep-links into its panel. Fleet browsing is one click away, never the
  landing page: operators open consoles to answer "is anything wrong", not to browse.
- **D3 — Proof bar (UI is claims-are-proofs too).**
  Kit: component tests (vitest) on the composites (keyset table paging, verb button's
  refusal surface, state mapping), under a COVERAGE FLOOR that CI enforces (kit 95/90,
  console 97/85 statements/branches) — the floor exists because the gaps that hid real
  bugs here were branches nobody had thought to exercise, not tests anyone had deleted.
  ACCESSIBILITY is part of the bar, not a later polish pass: every panel and the confirm
  dialog are checked with axe (WCAG 2.1 A/AA, serious + critical) in the same smoke. An
  ops console is used at 3am over a remote session, keyboard-only, on whatever screen is
  to hand — its first run found the secondary text below the contrast threshold and a
  confirm dialog that Escape could not close. Console: the smoke drives THREE real apps — one open,
  one behind the auth floor, one tenant-scoped — because how the console behaves when it
  is REFUSED is as much a claim as how it behaves when it is welcome; plus a service that
  dies mid-session. Runnable on demand (`.github/workflows/console-smoke.yml`), not only
  nightly. Playwright smoke against a REAL generated
  app (a `GmConsole` shape joins the GM matrix: generate → run AppHost → drive triage →
  trigger a run → watch it complete → replay a repair item). No screenshot-diff theater;
  behavior only.

## 5. Phases

| Phase | Delivers | Exit gate |
|---|---|---|
| U1 | `ui/kit`: tokens + primitives + composites | component tests green; kit gallery page |
| U2 | Run console over the registry (single-app path) | **MET** (2026-07-26): `scripts/console-smoke.sh` drives the real console against a real Goldpath app (Postgres + Quartz + the frozen surface) — capability discovery, confirm gate, run reaching terminal, repair queue replayed; runs nightly |
| U3 | Module panels (bulk/campaign/notification/archival), capability-lit | **MET** (2026-07-27): all four panels ship, and the console smoke drives every module's real verbs against one app — bulk's four-eyes gate (including the engine's refusal on invalid rows), the campaign governor over a real broker (release, throttle, pause, resume, abort), the notification evidence in all three kinds (sent, failed, suppressed, recipients masked), and archival's chain verification, keyed retrieval, legal hold, the hold's refusal of an erasure, and the erasure itself — after which the chain still verifies |
| U4 | Cross-service registry + triage home + auth story + `MapGoldpathConsole()` + GmConsole in nightly | the full D3 bar; the console driven against CorPay and its screenshots into the README (open-threads T1) |
| U5 | The styling pass — one sweep over spacing, density, filter layout, panel rhythm | deliberately LAST (open-threads T2): cosmetics before the flows settle get done twice. The axe and smoke gates must stay green through it, and the tokens change in `ui-standard-v1`, not per panel |

## 6. Non-goals

No CRUD generator for business entities (vertical slices own their screens); no
embedded Grafana re-implementation (panels link/embed the boards the modules already
ship); no theme marketplace — one standard, one accent swap.
