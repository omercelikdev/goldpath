# Master plan — 2026-08-03 (owner-approved order)

The single ordered list of everything open, set with the owner on 2026-08-03 after a
repo-wide sweep (code, RFCs, ledgers, schema-vs-generator, samples, CI). Nothing is
open outside this list and the trigger-parked set at the bottom. Each step lands with
the standing discipline: tests → PR → review findings answered BEFORE merge → CI
green → merge → ledgers updated in the same PR.

## The order

1. **Campaign R1 implementation** — ExcludedDays, EndDate→ExpiredIncomplete, the retry
   ladder (30s → 2m → repair queue), GlobalTps; schema + template + CLI recipe +
   console fields + tests. The one accepted RFC with zero code; blocks S-FLEET-01/03.
2. **Housekeeping slice** (one group) — **DONE 2026-08-03**: T14 four `.Produces<T>` (+
   the response-side export test; CorPay's re-export proof rides preview.6) · T8 bulk
   `?definition=` filter (server + facet + smoke past the page bound) · ops runbooks for
   Idempotency/AuditTrail/SoftDelete · analyzers GP1001/1002/1004 (batch 4) · T4 review
   agent as a CI step + output-contract hardening (waits only on the `ANTHROPIC_API_KEY`
   secret — owner decision) · the monthly bench cadence (bench.yml, 1st of the month) ·
   the GM coverage-table reality note and the stale doc lines (2026-08-03, PR #131).
3. **preview.6 train** — R3 contract + console U7–U9 + R1 + housekeeping to NuGet;
   T12 (CorPay EOD worker readiness) rides this train and unblocks T1; CorPay pins +
   upgrade guide.
4. **ADR-0012 + Platform/Module SDK RFC** — the philosophy in writing: Goldpath is a
   PLATFORM; first-party product modules (Sync, API Portal, …) ride the shared core,
   stand up standalone, and wear the one console family. Decisions to settle: the SDK
   surface (shared files → packages + semver promise) · namespaced module declarations
   in the manifest · the console contribution model (per-product console vs plugin
   panels) · private distribution (repo/feed/license; closed modules on the open
   core) · publishing the ui kit to npm (the @qorpe/ui question) · the container/deploy
   story · federation auth across products.
5. **Shapes + CLI package** (the RFC's implementation) — clean-architecture template ·
   microservice/multi-service layout + gateway · plain-monolith variant ·
   `goldpath new module|service` · the INTERACTIVE wizard (asks which modules, then
   derives the infrastructure itself: "no broker needed — removed") · `goldpath init`
   (L2 attach) · GM matrix 6/6 + a bulk-only GM row + the module×infrastructure
   matrix as an adopter/sales document.
6. **Pilot product module** — the API Portal or Sync skeleton: the module path's
   CorPay. Its console composes from the kit — the third consumer that fires the
   @qorpe/ui decision for real.
7. **Scenario campaign** — starts after step 3 and runs alongside: S-FIN-01 first,
   the S-FLEET set once R1 ships, the 30M rig spec co-written before any scale claim.
   All seven seeded scenarios get evidence.
8. **The samples — LAST, as the full-set exam** — Insurance and Telco from scratch
   (with the step-5 wizard, proving it in anger) plus CorPay's open cells (SoftDelete,
   Caching, a real Contracts-idiom worker consumer, the Archival slice, SPEC-GAPS
   G3–G7, GAP-LEDGER #32/#33/#34). `add feature`, `db bundle`, the manifest skill —
   all proven in real use; no empty core cell remains in the coverage matrix.

## Parked on written triggers (deliberately open)

Oracle provider (T16 — first committed Oracle-mandating adopter) · `upgrade` skill
(first real preview→preview migration) · console URL routes / OIDC login / tenant
picker (T9/T10/T11) · ⌘K entity-id search (needs a cross-module lookup endpoint) ·
stat-card trends (no time series in the contract) · TS mutation for the ui kit (T3) ·
saga + rules engine (Ring C — decided inside the step-4 RFC) · Phase 3 transformation
package (reverse-engineer, differential-test, strangler guide — the phase after the
samples).
